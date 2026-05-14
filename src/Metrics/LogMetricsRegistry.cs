#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace MMP.Herald.Metrics;

/// <summary>
/// Holds all per-sink metrics collectors. Populated at bootstrap, read-only after.
/// Exposed via LoggingBootstrapResult so the app can query metrics at runtime.
///
/// <para>
/// Implements <see cref="IPipelineDropSink"/> so pipeline decorators can
/// report drops directly against the registry. Named drops find the
/// matching collector; unnamed drops fan out across every registered
/// collector so the aggregate pipeline drop count stays honest.
/// </para>
/// </summary>
public sealed class LogMetricsRegistry : IPipelineDropSink
{
    private readonly Dictionary<string, ILogMetricsCollector> _collectors = new(StringComparer.OrdinalIgnoreCase);

    public void RegisterCollector(string sinkName, ILogMetricsCollector collector)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sinkName);
        ArgumentNullException.ThrowIfNull(collector);
        _collectors[sinkName] = collector;
    }

    public LogMetricsSnapshot? GetSnapshot(string sinkName)
    {
        return _collectors.TryGetValue(sinkName, out var collector)
            ? collector.GetSnapshot()
            : null;
    }

    public IReadOnlyList<LogMetricsSnapshot> GetAllSnapshots()
    {
        return _collectors.Values.Select(c => c.GetSnapshot()).ToList();
    }

    /// <inheritdoc />
    public void RecordDrop(string? sinkName = null)
    {
        if (sinkName is not null)
        {
            if (_collectors.TryGetValue(sinkName, out var collector))
            {
                collector.RecordDrop();
            }
            return;
        }

        // Pipeline-level drop — no single sink owns it. Attribute to every
        // collector so the per-sink Prometheus numbers match the pipeline
        // total. Callers querying a single sink's drop count see the drops
        // that would have reached that sink had the pipeline not discarded
        // upstream.
        foreach (var collector in _collectors.Values)
        {
            collector.RecordDrop();
        }
    }

    /// <inheritdoc />
    public void RecordDrop(string? sinkName, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (sinkName is not null)
        {
            if (_collectors.TryGetValue(sinkName, out var collector))
            {
                collector.RecordDrop(reason);
            }
            return;
        }

        // Pipeline-level reason-tagged drop. Fan out so per-sink dashboards
        // still see the drop; the per-reason breakdown on each collector
        // then attributes the category.
        foreach (var collector in _collectors.Values)
        {
            collector.RecordDrop(reason);
        }
    }
}

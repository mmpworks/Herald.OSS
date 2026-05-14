#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using MMP.Herald.Events;
using MMP.Herald.Failures;
using MMP.Herald.Levels;
using MMP.Herald.Routing;

namespace MMP.Herald.Pipeline;

/// <summary>
/// A composite logger with named sink slots that can be atomically swapped at runtime.
/// Unlike SwappableLogger (which swaps the entire pipeline), this targets individual
/// sinks without rebuilding the full pipeline.
///
/// Thread-safe: reads use Volatile.Read, writes use Interlocked.Exchange.
///
/// Usage:
///   var router = new SwappableSinkRouter(
///       new Dictionary&lt;string, ILogger&gt; { ["file"] = fileSink, ["console"] = consoleSink },
///       failureSink);
///
///   // Later, swap just the file sink:
///   router.SwapSink("file", newFileSink);
/// </summary>
public sealed class SwappableSinkRouter : ILogger, ISinkChainLevelIntrospection
{
    private readonly Dictionary<string, SinkSlot> _slots;
    private readonly ILogFailureSink _failureSink;

    public SwappableSinkRouter(
        IReadOnlyDictionary<string, ILogger> namedSinks,
        ILogFailureSink? failureSink = null) {
        ArgumentNullException.ThrowIfNull(namedSinks);

        _failureSink = failureSink ?? NullLogFailureSink.Instance;
        _slots = new Dictionary<string, SinkSlot>(StringComparer.OrdinalIgnoreCase);

        foreach (var (name, sink) in namedSinks)
        {
            _slots[name] = new SinkSlot(sink);
        }
    }

    /// <summary>
    /// Atomically replace one named sink. The old sink continues processing
    /// any in-flight events; new events go to the replacement.
    /// Returns the previous sink (caller is responsible for disposing if needed).
    /// </summary>
    public ILogger? SwapSink(string sinkName, ILogger newSink) {
        ArgumentException.ThrowIfNullOrWhiteSpace(sinkName);
        ArgumentNullException.ThrowIfNull(newSink);

        if (!_slots.TryGetValue(sinkName, out var slot))
        {
            return null;
        }

        return slot.Swap(newSink);
    }

    /// <summary>
    /// Get the current sink for a named slot.
    /// </summary>
    public ILogger? GetSink(string sinkName) {
        return _slots.TryGetValue(sinkName, out var slot) ? slot.Current : null;
    }

    /// <summary>
    /// Get all registered sink names.
    /// </summary>
    public IReadOnlyCollection<string> SinkNames => _slots.Keys;

    // Report the minimum level over the CURRENT set of slots. Snapshot taken
    // at call time; a SwapSink after this returns does not invalidate a
    // previously-read value. Pipeline recomposition handles that at the
    // hot-reload boundary. Unknown-or-unaware slot → null.
    public LogLevel? GetMinimumLevel(ILogLevelRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        if (_slots.Count == 0) return null;

        LogLevel? aggregated = null;
        foreach (var slot in _slots.Values)
        {
            var sink = slot.Current;
            if (sink is not ISinkChainLevelIntrospection aware) return null;

            var slotMin = aware.GetMinimumLevel(registry);
            if (slotMin is null) return null;

            if (aggregated is null || registry.IsAtOrAbove(aggregated, slotMin))
            {
                aggregated = slotMin;
            }
        }
        return aggregated;
    }

    public void Log(LogEvent logEvent) {
        ArgumentNullException.ThrowIfNull(logEvent);

        foreach (var slot in _slots.Values)
        {
            try
            {
                slot.Current.Log(logEvent);
            }
            catch (Exception exception)
            {
                _failureSink.ReportFailure(logEvent, exception, nameof(SwappableSinkRouter));
            }
        }
    }

    /// <summary>
    /// Thread-safe sink holder with atomic swap.
    /// </summary>
    private sealed class SinkSlot
    {
        private ILogger _sink;

        public SinkSlot(ILogger sink) {
            _sink = sink;
        }

        public ILogger Current => Volatile.Read(ref _sink);

        public ILogger Swap(ILogger newSink) {
            return Interlocked.Exchange(ref _sink, newSink);
        }
    }
}

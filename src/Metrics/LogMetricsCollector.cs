#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace MMP.Herald.Metrics;

/// <summary>
/// Thread-safe per-sink metrics collector using Interlocked operations.
/// Lock-free for game loop hot path safety.
/// </summary>
public sealed class LogMetricsCollector : ILogMetricsCollector
{
    private readonly string _sinkName;
    private long _eventCount;
    private long _failureCount;
    private long _dropCount;
    private long _totalLatencyMs;

    // Per-reason drop buckets. Lazily allocated (stays null until the first
    // reason-tagged drop) so collectors that never see a reason-tagged call
    // pay zero memory overhead. Long[1] box lets us keep Interlocked on a ref.
    private ConcurrentDictionary<string, long[]>? _dropsByReason;

    public LogMetricsCollector(string sinkName)
    {
        _sinkName = sinkName;
    }

    public void RecordDelivery(long elapsedMilliseconds)
    {
        Interlocked.Increment(ref _eventCount);
        Interlocked.Add(ref _totalLatencyMs, elapsedMilliseconds);
    }

    public void RecordFailure()
    {
        Interlocked.Increment(ref _failureCount);
    }

    public void RecordDrop()
    {
        Interlocked.Increment(ref _dropCount);
    }

    public void RecordDrop(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        // Aggregate total still advances so existing dashboards stay honest.
        Interlocked.Increment(ref _dropCount);

        // Allocate the reason bucket only when we actually see per-reason
        // traffic. The double-checked pattern mirrors the lazy map idiom
        // used elsewhere in the metrics layer.
        var buckets = _dropsByReason;
        if (buckets is null)
        {
            var fresh = new ConcurrentDictionary<string, long[]>(StringComparer.Ordinal);
            buckets = Interlocked.CompareExchange(ref _dropsByReason, fresh, null) ?? fresh;
        }

        // Each reason owns a single-slot long[]. GetOrAdd returns the shared
        // slot; Interlocked.Increment on slot[0] keeps the counter correct
        // under concurrent producers.
        var slot = buckets.GetOrAdd(reason, static _ => new long[1]);
        Interlocked.Increment(ref slot[0]);
    }

    public LogMetricsSnapshot GetSnapshot()
    {
        var events = Interlocked.Read(ref _eventCount);
        var failures = Interlocked.Read(ref _failureCount);
        var drops = Interlocked.Read(ref _dropCount);
        var totalLatency = Interlocked.Read(ref _totalLatencyMs);
        var averageLatency = events > 0 ? (double)totalLatency / events : 0.0;

        IReadOnlyDictionary<string, long>? reasonSnapshot = null;
        var buckets = _dropsByReason;
        if (buckets is not null && !buckets.IsEmpty)
        {
            var projected = new Dictionary<string, long>(buckets.Count, StringComparer.Ordinal);
            foreach (var kvp in buckets)
            {
                projected[kvp.Key] = Interlocked.Read(ref kvp.Value[0]);
            }
            reasonSnapshot = projected;
        }

        return new LogMetricsSnapshot(
            SinkName: _sinkName,
            EventCount: events,
            FailureCount: failures,
            DropCount: drops,
            TotalLatencyMs: totalLatency,
            AverageLatencyMs: averageLatency,
            DropsByReason: reasonSnapshot);
    }
}

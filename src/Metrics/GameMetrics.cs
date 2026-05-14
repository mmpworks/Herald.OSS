#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace MMP.Herald.Metrics;

/// <summary>
/// First-class game metrics API for recording counters and gauges without
/// emitting log events. Lock-free and allocation-free on the hot path.
///
/// Use cases:
///   - FPS tracking
///   - Entity count per frame
///   - Network round-trip time
///   - Memory usage sampling
///   - Per-system frame budget consumption
///
/// Usage:
///   var metrics = new GameMetrics();
///
///   // In game loop (lock-free, ~5ns per call):
///   metrics.Increment("physics.collisionChecks");
///   metrics.Gauge("render.fps", currentFps);
///   metrics.Record("network.rttMs", latency);
///
///   // Periodic export (e.g., every 5 seconds):
///   var snapshot = metrics.Snapshot();
///   logger.Info(LogCategory.App, "Metrics: {metrics}",
///       properties: [LogProperty.Lazy("metrics", () => snapshot.ToString())]);
///
///   // Or integrate with OTLP via OtlpMetricsExporter addon.
/// </summary>
public sealed class GameMetrics
{
    private readonly ConcurrentDictionary<string, MetricEntry> _metrics = new(StringComparer.Ordinal);

    /// <summary>Increment a counter by 1.</summary>
    public void Increment(string name)
    {
        var entry = GetOrCreate(name, MetricKind.Counter);
        Interlocked.Increment(ref entry.Value);
    }

    /// <summary>Increment a counter by a specific amount.</summary>
    public void Add(string name, long amount)
    {
        var entry = GetOrCreate(name, MetricKind.Counter);
        Interlocked.Add(ref entry.Value, amount);
    }

    /// <summary>Set a gauge to an absolute value (last-write-wins).</summary>
    public void Gauge(string name, long value)
    {
        var entry = GetOrCreate(name, MetricKind.Gauge);
        Interlocked.Exchange(ref entry.Value, value);
    }

    /// <summary>Set a gauge to a floating-point value.</summary>
    public void Gauge(string name, double value)
    {
        var entry = GetOrCreate(name, MetricKind.Gauge);
        Interlocked.Exchange(ref entry.FloatValue, value);
        entry.IsFloat = true;
    }

    /// <summary>Record a sample value (updates min, max, sum, count for averaging).</summary>
    public void Record(string name, long value)
    {
        var entry = GetOrCreate(name, MetricKind.Histogram);
        Interlocked.Increment(ref entry.SampleCount);
        Interlocked.Add(ref entry.Value, value);
        UpdateMin(entry, value);
        UpdateMax(entry, value);
    }

    /// <summary>Take a point-in-time snapshot of all metrics. Counters are NOT reset.</summary>
    public GameMetricsSnapshot Snapshot()
    {
        var entries = new List<GameMetricValue>();
        foreach (var kvp in _metrics)
        {
            var e = kvp.Value;
            entries.Add(new GameMetricValue(
                Name: kvp.Key,
                Kind: e.Kind,
                Value: e.IsFloat ? (double)0 : Interlocked.Read(ref e.Value),
                FloatValue: e.IsFloat ? Volatile.Read(ref e.FloatValue) : null,
                SampleCount: e.Kind == MetricKind.Histogram ? Interlocked.Read(ref e.SampleCount) : null,
                Min: e.Kind == MetricKind.Histogram ? Interlocked.Read(ref e.Min) : null,
                Max: e.Kind == MetricKind.Histogram ? Interlocked.Read(ref e.Max) : null));
        }
        return new GameMetricsSnapshot(entries);
    }

    /// <summary>Take a snapshot and reset all counters/histograms to zero. Gauges are NOT reset.</summary>
    public GameMetricsSnapshot SnapshotAndReset()
    {
        var snapshot = Snapshot();
        foreach (var kvp in _metrics)
        {
            var e = kvp.Value;
            if (e.Kind == MetricKind.Counter)
            {
                Interlocked.Exchange(ref e.Value, 0);
            }
            else if (e.Kind == MetricKind.Histogram)
            {
                Interlocked.Exchange(ref e.Value, 0);
                Interlocked.Exchange(ref e.SampleCount, 0);
                Interlocked.Exchange(ref e.Min, long.MaxValue);
                Interlocked.Exchange(ref e.Max, long.MinValue);
            }
        }
        return snapshot;
    }

    private MetricEntry GetOrCreate(string name, MetricKind kind) =>
        _metrics.GetOrAdd(name, static (_, k) => new MetricEntry(k), kind);

    private static void UpdateMin(MetricEntry entry, long value)
    {
        long current;
        do { current = Interlocked.Read(ref entry.Min); }
        while (value < current && Interlocked.CompareExchange(ref entry.Min, value, current) != current);
    }

    private static void UpdateMax(MetricEntry entry, long value)
    {
        long current;
        do { current = Interlocked.Read(ref entry.Max); }
        while (value > current && Interlocked.CompareExchange(ref entry.Max, value, current) != current);
    }

    private sealed class MetricEntry
    {
        public readonly MetricKind Kind;
        public long Value;
        public long SampleCount;
        public long Min = long.MaxValue;
        public long Max = long.MinValue;
        public double FloatValue;
        public bool IsFloat;
        public MetricEntry(MetricKind kind) => Kind = kind;
    }
}

/// <summary>Kind of metric being tracked.</summary>
public enum MetricKind
{
    /// <summary>Monotonically increasing counter (e.g., total events processed).</summary>
    Counter,
    /// <summary>Point-in-time gauge (e.g., current FPS, entity count).</summary>
    Gauge,
    /// <summary>Sampled distribution (e.g., frame time, network RTT).</summary>
    Histogram
}

/// <summary>Single metric value in a snapshot.</summary>
public sealed record GameMetricValue(
    string Name,
    MetricKind Kind,
    double Value,
    double? FloatValue,
    long? SampleCount,
    long? Min,
    long? Max);

/// <summary>Point-in-time snapshot of all game metrics.</summary>
public sealed record GameMetricsSnapshot(IReadOnlyList<GameMetricValue> Metrics)
{
    public override string ToString()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var m in Metrics)
        {
            if (sb.Length > 0) sb.Append(", ");
            sb.Append(m.Name);
            sb.Append('=');
            if (m.FloatValue.HasValue)
                sb.Append(m.FloatValue.Value.ToString("F2"));
            else if (m.Kind == MetricKind.Histogram && m.SampleCount > 0)
                sb.Append($"avg:{m.Value / m.SampleCount:F1} min:{m.Min} max:{m.Max} n:{m.SampleCount}");
            else
                sb.Append(m.Value);
        }
        return sb.ToString();
    }
}

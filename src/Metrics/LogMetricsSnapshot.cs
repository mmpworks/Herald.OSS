#nullable enable

using System.Collections.Generic;

namespace MMP.Herald.Metrics;

/// <summary>
/// Point-in-time snapshot of per-sink delivery metrics.
/// <para>
/// <b>DropCount</b> counts events the pipeline discarded before calling
/// the sink — queue overflow, sync-wait timeout, circuit-breaker open,
/// cardinality-guard trip, sampling threshold exceeded. It is distinct
/// from <b>FailureCount</b>, which counts deliveries the sink rejected.
/// Snapshot reads use <see cref="System.Threading.Interlocked.Read(ref long)"/>
/// so every field is a consistent 64-bit read — no torn counter values.
/// </para>
/// <para>
/// <b>DropsByReason</b> breaks the drop total down by filter category
/// (<see cref="DropReasons.Level"/>, <see cref="DropReasons.Sampling"/>, etc.)
/// when the pipeline uses the reason-tagged drop hook. When no reason
/// tags have been recorded the map is empty and the aggregate
/// <see cref="DropCount"/> is still authoritative.
/// </para>
/// </summary>
public sealed record LogMetricsSnapshot(
    string SinkName,
    long EventCount,
    long FailureCount,
    long DropCount,
    long TotalLatencyMs,
    double AverageLatencyMs,
    IReadOnlyDictionary<string, long>? DropsByReason = null);

#nullable enable

using System.Collections.Generic;
using MMP.Herald.Pipeline;

namespace MMP.Herald.Metrics;

/// <summary>
/// Unified health snapshot of the entire logging pipeline.
/// Aggregates per-sink health, metrics, and queue status.
/// </summary>
public sealed record PipelineHealthReport(
    IReadOnlyList<SinkHealthStatus> SinkStatuses,
    SinkState AggregateState,
    long TotalEventsDelivered,
    long TotalFailures,
    int? QueueDepth);

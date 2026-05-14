#nullable enable

using MMP.Herald.Predicates;

namespace MMP.Herald.Configuration.Json;

/// <summary>
/// JSON-facing configuration for conditional batch filtering (NLog PostFilteringWrapper equivalent).
/// When enabled, events are buffered and filtered as a batch. If any event in the batch
/// matches the trigger condition, the escalated filter is applied; otherwise the normal filter.
/// </summary>
public sealed record JsonPostFilteringConfig(
    bool Enabled = false,
    int MaxBatchSize = Services.PipelineDefaults.BatchSize,
    int MaxBatchDelayMs = Services.PipelineDefaults.BatchDelayMs,
    PredicateSpec? TriggerCondition = null,
    PredicateSpec? NormalFilter = null,
    PredicateSpec? EscalatedFilter = null);

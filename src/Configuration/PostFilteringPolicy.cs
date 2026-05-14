#nullable enable

using MMP.Herald.Filters;

namespace MMP.Herald.Configuration;

/// <summary>
/// Runtime policy for conditional batch filtering.
/// Contains the compiled batch filter and buffering parameters.
/// </summary>
public sealed record PostFilteringPolicy(
    IBatchLogFilter BatchFilter,
    int MaxBatchSize = Services.PipelineDefaults.BatchSize,
    int MaxBatchDelayMs = Services.PipelineDefaults.BatchDelayMs);

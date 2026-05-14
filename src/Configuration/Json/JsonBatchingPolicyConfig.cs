#nullable enable

namespace MMP.Herald.Configuration.Json;

/// <summary>
/// JSON-facing batching policy.
/// </summary>
public sealed record JsonBatchingPolicyConfig(
    bool Enabled = false,
    int MaxBatchSize = Services.PipelineDefaults.BatchSize,
    int MaxBatchDelayMs = Services.PipelineDefaults.BatchDelayMs);
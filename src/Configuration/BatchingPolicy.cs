#nullable enable

namespace MMP.Herald.Configuration;

/// <summary>
/// Controls batched delivery behavior for the sink pipeline.
/// </summary>
public sealed record BatchingPolicy(
    bool IsEnabled,
    int MaxBatchSize,
    int MaxBatchDelayMs)
{
    public static BatchingPolicy Disabled { get; } = new(
        IsEnabled: false,
        MaxBatchSize: 1,
        MaxBatchDelayMs: Services.PipelineDefaults.BatchDelayMs);
}
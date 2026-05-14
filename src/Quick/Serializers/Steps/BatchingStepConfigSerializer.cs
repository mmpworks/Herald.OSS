#nullable enable

using System.Collections.Generic;

namespace MMP.Herald.Quick.Serializers.Steps;

/// <summary>
/// Config serializer for the <c>"batching"</c> pipeline step. Mirrors the
/// fields set by <see cref="QuickLogBuilder.WithBatching"/>.
/// </summary>
internal sealed class BatchingStepConfigSerializer : IStepConfigSerializer
{
    public string StepName => "batching";

    public Dictionary<string, object?>? BuildConfig(QuickLogBuilder builder) => new()
    {
        ["enabled"] = builder.BatchingEnabled,
        ["maxBatchSize"] = builder.BatchMaxSize,
        ["maxBatchDelayMs"] = builder.BatchDelayMs,
    };
}

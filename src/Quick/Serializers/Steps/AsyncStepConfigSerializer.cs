#nullable enable

using System.Collections.Generic;

namespace MMP.Herald.Quick.Serializers.Steps;

/// <summary>
/// Config serializer for the <c>"async"</c> pipeline step. Mirrors the
/// fields set by <see cref="QuickLogBuilder.WithAsyncLogging"/>.
/// </summary>
internal sealed class AsyncStepConfigSerializer : IStepConfigSerializer
{
    public string StepName => "async";

    public Dictionary<string, object?>? BuildConfig(QuickLogBuilder builder) => new()
    {
        ["enabled"] = builder.AsyncEnabled,
        ["capacity"] = builder.AsyncCapacity,
        ["dropStrategy"] = builder.AsyncDropStrategy,
        ["deferRendering"] = builder.AsyncDeferRendering,
    };
}

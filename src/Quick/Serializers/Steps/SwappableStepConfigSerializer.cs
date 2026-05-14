#nullable enable

using System.Collections.Generic;

namespace MMP.Herald.Quick.Serializers.Steps;

/// <summary>
/// Config serializer for the <c>"swappable"</c> pipeline step (hot reload
/// decorator). When the swappable step is in the strategy, hot reload is
/// implicitly enabled; only the debounce window is builder-configurable.
/// </summary>
internal sealed class SwappableStepConfigSerializer : IStepConfigSerializer
{
    public string StepName => "swappable";

    public Dictionary<string, object?>? BuildConfig(QuickLogBuilder builder) => new()
    {
        ["hotReloadEnabled"] = true,
        ["debounceMs"] = builder.HotReloadDebounceMsValue,
    };
}

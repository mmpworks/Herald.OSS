#nullable enable

using System.Collections.Generic;

namespace MMP.Herald.Quick.Serializers.Steps;

/// <summary>
/// Config serializer for the <c>"filtering"</c> pipeline step. Reports the
/// minimum level, dynamic-level state, per-category overrides (when set),
/// and sampling rate (when active).
/// </summary>
internal sealed class FilteringStepConfigSerializer : IStepConfigSerializer
{
    public string StepName => "filtering";

    public Dictionary<string, object?>? BuildConfig(QuickLogBuilder builder)
    {
        var config = new Dictionary<string, object?>
        {
            ["minimumLevel"] = builder.MinimumLevelValue,
            ["dynamicLevels"] = builder.DynamicLevelsEnabled,
        };

        if (builder.CategoryLevelOverridesView is { Count: > 0 } overrides)
            config["categoryOverrides"] = overrides;

        if (builder.SamplingRateValue > 0)
            config["samplingRate"] = builder.SamplingRateValue;

        return config;
    }
}

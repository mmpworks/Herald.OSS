#nullable enable

using System.Collections.Generic;

namespace MMP.Herald.Quick.Serializers.Steps;

/// <summary>
/// Config serializer for the <c>"postFiltering"</c> pipeline step. Emits
/// only the primitive batch-sizing knobs because <see cref="JsonPipelineStepConfig.Config"/>
/// goes through <c>ObjectDictionaryJsonConverter</c>, which rejects
/// custom types. The three <see cref="MMP.Herald.Predicates.PredicateSpec"/>
/// records travel through the top-level <see cref="MMP.Herald.Configuration.Json.JsonLoggingConfig.PostFiltering"/>
/// section, which keeps full polymorphic round-trip via <c>[JsonPolymorphic]</c>.
/// </summary>
internal sealed class PostFilteringStepConfigSerializer : IStepConfigSerializer
{
    public string StepName => "postFiltering";

    public Dictionary<string, object?>? BuildConfig(QuickLogBuilder builder) => new()
    {
        ["enabled"] = builder.PostFilteringEnabled,
        ["maxBatchSize"] = builder.PostFilteringMaxBatchSize,
        ["maxBatchDelayMs"] = builder.PostFilteringMaxBatchDelayMs,
    };
}

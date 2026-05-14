#nullable enable

using System.Collections.Generic;

namespace MMP.Herald.Quick.Serializers.Steps;

/// <summary>
/// Config serializer for the <c>"eventProcessing"</c> pipeline step.
/// Reports the count of registered processors and their names (when any
/// are registered).
/// </summary>
internal sealed class EventProcessingStepConfigSerializer : IStepConfigSerializer
{
    public string StepName => "eventProcessing";

    public Dictionary<string, object?>? BuildConfig(QuickLogBuilder builder)
    {
        var items = builder.EventProcessors.Items;
        var config = new Dictionary<string, object?>
        {
            ["processorCount"] = items.Count,
        };

        if (items.Count > 0)
        {
            var names = new List<string>(items.Count);
            foreach (var p in items)
                names.Add(p.Name);
            config["processors"] = names;
        }

        return config;
    }
}

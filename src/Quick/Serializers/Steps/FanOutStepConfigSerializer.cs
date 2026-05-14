#nullable enable

using System.Collections.Generic;

namespace MMP.Herald.Quick.Serializers.Steps;

/// <summary>
/// Config serializer for the <c>"fanOut"</c> pipeline step. The fanOut step's
/// config enumerates every configured sink so the dashboard can render the
/// layout; each sink kind supplies its own descriptor via the sink-serializer
/// registry, which keeps the fanOut step agnostic of individual sink shapes.
/// </summary>
internal sealed class FanOutStepConfigSerializer : IStepConfigSerializer
{
    public string StepName => "fanOut";

    public Dictionary<string, object?>? BuildConfig(QuickLogBuilder builder)
    {
        var sinkList = new List<Dictionary<string, object?>>();
        foreach (var serializer in QuickLogBuilderSerializers.Sinks)
        {
            foreach (var entry in serializer.BuildFanOutEntries(builder))
                sinkList.Add(entry);
        }

        return new Dictionary<string, object?>
        {
            ["sinkCount"] = sinkList.Count,
            ["sinks"] = sinkList,
        };
    }
}

#nullable enable

using System.Collections.Generic;
using System.Linq;
using MMP.Herald.Configuration.Json;

namespace MMP.Herald.Quick.Serializers.Sinks;

/// <summary>
/// Config serializer for user-registered custom sink providers. These do not
/// emit JsonLogSinkConfig entries (the provider supplies the sink directly at
/// runtime), but they DO appear in the fanOut step config so the dashboard
/// can show them alongside built-in sinks.
/// </summary>
internal sealed class CustomSinkProviderConfigSerializer : ISinkConfigSerializer
{
    public string SinkKind => "custom";

    public IEnumerable<(JsonLogSinkConfig Sink, JsonLogRouteConfig Route)> BuildSinkRoutes(
        QuickLogBuilder builder, SinkSerializerContext context) =>
        Enumerable.Empty<(JsonLogSinkConfig, JsonLogRouteConfig)>();

    public IEnumerable<Dictionary<string, object?>> BuildFanOutEntries(QuickLogBuilder builder)
    {
        foreach (var provider in builder.SinkProviders.Items)
        {
            yield return new Dictionary<string, object?>
            {
                ["kind"] = provider.Name,
                ["isCustom"] = true,
            };
        }
    }
}

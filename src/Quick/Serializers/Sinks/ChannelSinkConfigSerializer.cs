#nullable enable

using System.Collections.Generic;
using MMP.Herald.Configuration.Json;
using MMP.Herald.Predicates;

namespace MMP.Herald.Quick.Serializers.Sinks;

/// <summary>
/// Config serializer for channel sinks. Emits one entry per registered
/// channel; each channel gets its own sink kind (<c>channel_{name}</c>)
/// and a route predicate that matches the channel context key.
/// </summary>
internal sealed class ChannelSinkConfigSerializer : ISinkConfigSerializer
{
    public string SinkKind => Services.KnownSinkKinds.Channel;

    public IEnumerable<(JsonLogSinkConfig Sink, JsonLogRouteConfig Route)> BuildSinkRoutes(
        QuickLogBuilder builder, SinkSerializerContext context)
    {
        foreach (var channel in builder.Channels.Items)
        {
            var sinkKind = $"channel_{channel.Name}";
            yield return (
                new JsonLogSinkConfig(sinkKind, sinkKind),
                new JsonLogRouteConfig(sinkKind,
                    new PredicateSpec.ContextValueEquals("channel", channel.Name)));
        }
    }

    public IEnumerable<Dictionary<string, object?>> BuildFanOutEntries(QuickLogBuilder builder)
    {
        foreach (var channel in builder.Channels.Items)
        {
            yield return new Dictionary<string, object?>
            {
                ["kind"] = Services.KnownSinkKinds.Channel,
                ["name"] = channel.Name,
            };
        }
    }
}

#nullable enable

using System.Collections.Generic;
using MMP.Herald.Configuration.Json;

namespace MMP.Herald.Quick.Serializers.Sinks;

/// <summary>
/// Config serializer for the built-in console sink. Emits a single
/// JsonLogSinkConfig+JsonLogRouteConfig pair when the console sink is
/// enabled on the builder, and a matching fanOut descriptor.
/// </summary>
internal sealed class ConsoleSinkConfigSerializer : ISinkConfigSerializer
{
    public string SinkKind => Services.KnownSinkKinds.Console;

    public IEnumerable<(JsonLogSinkConfig Sink, JsonLogRouteConfig Route)> BuildSinkRoutes(
        QuickLogBuilder builder, SinkSerializerContext context)
    {
        if (!builder.IncludeConsole)
            yield break;

        yield return (
            new JsonLogSinkConfig(
                Services.KnownSinkKinds.Console,
                Services.KnownSinkKinds.Console,
                Alias: "default",
                Vendor: "MMP",
                Version: HeraldVersion.Version,
                MinLevel: builder.ConsoleMinLevel),
            new JsonLogRouteConfig(Services.KnownSinkKinds.Console, context.DefaultRoutePredicate));
    }

    public IEnumerable<Dictionary<string, object?>> BuildFanOutEntries(QuickLogBuilder builder)
    {
        if (!builder.IncludeConsole)
            yield break;

        yield return new Dictionary<string, object?>
        {
            ["kind"] = Services.KnownSinkKinds.Console,
            ["minLevel"] = builder.ConsoleMinLevel,
        };
    }
}

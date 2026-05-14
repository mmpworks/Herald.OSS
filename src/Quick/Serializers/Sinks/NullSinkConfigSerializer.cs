#nullable enable

using System.Collections.Generic;
using MMP.Herald.Configuration.Json;

namespace MMP.Herald.Quick.Serializers.Sinks;

/// <summary>
/// Config serializer for the built-in null sink. Emits a sink + default
/// route when the builder opts in via <c>WithNullSink</c>. The null sink
/// discards every event so the pipeline can be exercised without any
/// downstream I/O.
/// </summary>
internal sealed class NullSinkConfigSerializer : ISinkConfigSerializer
{
    public string SinkKind => Services.KnownSinkKinds.Null;

    public IEnumerable<(JsonLogSinkConfig Sink, JsonLogRouteConfig Route)> BuildSinkRoutes(
        QuickLogBuilder builder, SinkSerializerContext context)
    {
        if (!builder.IncludeNullSink)
            yield break;

        yield return (
            new JsonLogSinkConfig(
                Services.KnownSinkKinds.Null,
                Services.KnownSinkKinds.Null,
                Alias: "default",
                Vendor: "MMP",
                Version: HeraldVersion.Version,
                MinLevel: builder.NullSinkMinLevel),
            new JsonLogRouteConfig(Services.KnownSinkKinds.Null, context.DefaultRoutePredicate));
    }

    public IEnumerable<Dictionary<string, object?>> BuildFanOutEntries(QuickLogBuilder builder)
    {
        if (!builder.IncludeNullSink)
            yield break;

        yield return new Dictionary<string, object?>
        {
            ["kind"] = Services.KnownSinkKinds.Null,
            ["minLevel"] = builder.NullSinkMinLevel,
        };
    }
}

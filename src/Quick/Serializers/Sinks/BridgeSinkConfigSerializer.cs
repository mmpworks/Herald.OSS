#nullable enable

using System.Collections.Generic;
using MMP.Herald.Configuration.Json;
using MMP.Herald.Predicates;

namespace MMP.Herald.Quick.Serializers.Sinks;

/// <summary>
/// Config serializer for pipeline bridge sinks. Emits one entry per bridge
/// with a pass-through route predicate; the bridge itself decides which
/// events to forward.
/// </summary>
internal sealed class BridgeSinkConfigSerializer : ISinkConfigSerializer
{
    public string SinkKind => Services.KnownSinkKinds.PipelineBridge;

    public IEnumerable<(JsonLogSinkConfig Sink, JsonLogRouteConfig Route)> BuildSinkRoutes(
        QuickLogBuilder builder, SinkSerializerContext context)
    {
        for (var i = 0; i < builder.Bridges.Items.Count; i++)
        {
            // Bridges are registered as providers with an indexed kind
            // (pipeline_bridge_0, pipeline_bridge_1, ...) so the sink
            // factory can find each one by Kind during pipeline build.
            // The runtime sink definition has to use the same indexed
            // string for both Name and Kind — otherwise the factory
            // can't match the kind to a provider and replaces the
            // bridge with a no-op stand-in, silently dropping every
            // event that should have routed through it.
            //
            // The JSON-loaded restore path (RestoreBuilderFromConfig)
            // doesn't try to recognise this indexed kind — bridges are
            // re-added by the host via WithBridge(capture) after restore.
            // The misleading on-disk "kind" string is harmless.
            var sinkKind = QuickLogBuilder.ComputeBridgeSinkKind(i);
            yield return (
                new JsonLogSinkConfig(sinkKind, sinkKind),
                new JsonLogRouteConfig(sinkKind, new PredicateSpec.True()));
        }
    }

    public IEnumerable<Dictionary<string, object?>> BuildFanOutEntries(QuickLogBuilder builder)
    {
        for (var i = 0; i < builder.Bridges.Items.Count; i++)
        {
            yield return new Dictionary<string, object?>
            {
                ["kind"] = Services.KnownSinkKinds.PipelineBridge,
                ["index"] = i,
            };
        }
    }
}

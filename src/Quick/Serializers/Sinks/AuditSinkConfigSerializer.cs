#nullable enable

using System.Collections.Generic;
using MMP.Herald.Configuration.Json;
using MMP.Herald.Predicates;

namespace MMP.Herald.Quick.Serializers.Sinks;

/// <summary>
/// Config serializer for audit sinks. Emits one entry per registered audit
/// sink, routed by the audit context key so only audit-tagged events flow
/// through them.
/// </summary>
internal sealed class AuditSinkConfigSerializer : ISinkConfigSerializer
{
    public string SinkKind => "audit";

    public IEnumerable<(JsonLogSinkConfig Sink, JsonLogRouteConfig Route)> BuildSinkRoutes(
        QuickLogBuilder builder, SinkSerializerContext context)
    {
        for (var i = 0; i < builder.AuditSinks.Items.Count; i++)
        {
            var sinkKind = $"audit_{i}";
            yield return (
                new JsonLogSinkConfig(sinkKind, sinkKind),
                new JsonLogRouteConfig(sinkKind,
                    new PredicateSpec.ContextValueEquals(Services.LogContextKeys.Audit, "true")));
        }
    }

    public IEnumerable<Dictionary<string, object?>> BuildFanOutEntries(QuickLogBuilder builder)
    {
        for (var i = 0; i < builder.AuditSinks.Items.Count; i++)
        {
            yield return new Dictionary<string, object?>
            {
                ["kind"] = "audit",
                ["index"] = i,
            };
        }
    }
}

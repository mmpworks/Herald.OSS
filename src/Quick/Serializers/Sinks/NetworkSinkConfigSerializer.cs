#nullable enable

using System.Collections.Generic;
using MMP.Herald.Configuration.Json;

namespace MMP.Herald.Quick.Serializers.Sinks;

/// <summary>
/// Config serializer for network / integration sinks (http_json, tcp_json_line,
/// elasticsearch, slack, generic webhook, otlp_json, otlp_protobuf). All share
/// the same JSON emission shape, only the kind and transport fields differ.
///
/// When the builder has multiple network sinks of the same kind, sink names
/// are suffixed with an index so route assignment stays unambiguous.
/// </summary>
internal sealed class NetworkSinkConfigSerializer : ISinkConfigSerializer
{
    // Registry discriminator — individual kinds are emitted per entry.
    public string SinkKind => "network";

    public IEnumerable<(JsonLogSinkConfig Sink, JsonLogRouteConfig Route)> BuildSinkRoutes(
        QuickLogBuilder builder, SinkSerializerContext context)
    {
        var networkSinks = builder.NetworkSinksView;
        if (networkSinks.Count == 0)
            yield break;

        // Count kinds once so the suffix decision is O(n) rather than O(n^2).
        var kindCounts = new Dictionary<string, int>();
        foreach (var ns in networkSinks)
            kindCounts[ns.Kind] = kindCounts.TryGetValue(ns.Kind, out var c) ? c + 1 : 1;

        for (var i = 0; i < networkSinks.Count; i++)
        {
            var ns = networkSinks[i];
            var sinkName = kindCounts[ns.Kind] > 1 ? $"{ns.Kind}_{i}" : ns.Kind;

            // Headers ride in JsonLogSinkConfig.Properties under the
            // "headers" key so the open-bag plumbing carries them to the
            // downstream sink package without leaking HTTP-specific shape
            // into the typed JsonLogSinkConfig fields. Values are plain
            // strings; ${ENV_VAR} placeholders are resolved by
            // LoggingJsonSerializer.Deserialize at config-load time.
            IReadOnlyDictionary<string, object?>? properties = null;
            if (ns.Headers is { Count: > 0 })
            {
                // ObjectDictionaryJsonConverter only accepts
                // IReadOnlyDictionary<string, object?> for nested dicts.
                // Project the headers dict (string→string) into the
                // shape the converter recognises so nested headers
                // serialise correctly.
                var headersBag = new Dictionary<string, object?>(System.StringComparer.Ordinal);
                foreach (var (name, value) in ns.Headers) headersBag[name] = value;
                properties = new Dictionary<string, object?>(System.StringComparer.Ordinal)
                {
                    ["headers"] = headersBag,
                };
            }

            yield return (
                new JsonLogSinkConfig(sinkName, ns.Kind,
                    Uri: ns.Uri, Host: ns.Host, Port: ns.Port,
                    Vendor: "MMP", Version: HeraldVersion.Version,
                    MinLevel: ns.MinLevel,
                    Properties: properties),
                new JsonLogRouteConfig(sinkName, context.DefaultRoutePredicate));
        }
    }

    public IEnumerable<Dictionary<string, object?>> BuildFanOutEntries(QuickLogBuilder builder)
    {
        foreach (var ns in builder.NetworkSinksView)
        {
            var entry = new Dictionary<string, object?> { ["kind"] = ns.Kind };
            if (ns.Uri is not null) entry["uri"] = ns.Uri;
            if (ns.Host is not null) entry["host"] = ns.Host;
            if (ns.Port is not null) entry["port"] = ns.Port;
            if (ns.MinLevel is not null) entry["minLevel"] = ns.MinLevel;
            if (ns.Headers is { Count: > 0 })
            {
                // Project to object-valued dict so ObjectDictionaryJsonConverter
                // recognises the nested shape (same reason as the properties
                // bag above).
                var headersBag = new Dictionary<string, object?>(System.StringComparer.Ordinal);
                foreach (var (name, value) in ns.Headers) headersBag[name] = value;
                entry["headers"] = headersBag;
            }
            yield return entry;
        }
    }
}

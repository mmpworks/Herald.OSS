#nullable enable

using System.Collections.Generic;
using MMP.Herald.Configuration.Json;
using MMP.Herald.Predicates;

namespace MMP.Herald.Quick.Serializers;

/// <summary>
/// Shared context for sink serializers so individual sinks do not need to
/// know about channels or other sinks. The builder fills this in once per
/// build pass.
/// </summary>
public sealed record SinkSerializerContext(PredicateSpec DefaultRoutePredicate);

/// <summary>
/// Emits JSON sink and route entries (and matching fanOut step descriptors)
/// for a single sink kind. One implementation per sink kind (console, text_file,
/// http_json, tcp_json_line, etc.). Consolidates the knowledge that previously
/// lived in both <c>BuildSinksAndRoutes</c> and the <c>"fanOut"</c> case of
/// <c>BuildStepConfig</c>.
/// </summary>
public interface ISinkConfigSerializer
{
    /// <summary>
    /// The sink kind this serializer handles. Matches the <c>kind</c> field
    /// used in JSON config output (e.g. <c>"console"</c>, <c>"text_file"</c>).
    /// Used mainly for logging and duplicate-registration detection.
    /// </summary>
    string SinkKind { get; }

    /// <summary>
    /// Emit zero or more (sink, route) pairs based on the builder's state.
    /// An empty sequence means "this sink kind is not configured on this
    /// builder," which is the normal case for unused sinks.
    /// </summary>
    IEnumerable<(JsonLogSinkConfig Sink, JsonLogRouteConfig Route)> BuildSinkRoutes(
        QuickLogBuilder builder, SinkSerializerContext context);

    /// <summary>
    /// Emit zero or more fanOut step descriptor dictionaries. The fanOut step's
    /// config enumerates every live sink in a compact form for dashboard display.
    /// </summary>
    IEnumerable<Dictionary<string, object?>> BuildFanOutEntries(QuickLogBuilder builder);
}

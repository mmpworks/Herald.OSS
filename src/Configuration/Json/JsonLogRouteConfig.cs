#nullable enable

using MMP.Herald.Predicates;

namespace MMP.Herald.Configuration.Json;
/// <summary>
/// JSON-facing route configuration.
/// </summary>
public sealed record JsonLogRouteConfig(
    string SinkName,
    PredicateSpec Predicate,
    bool Enabled = true);
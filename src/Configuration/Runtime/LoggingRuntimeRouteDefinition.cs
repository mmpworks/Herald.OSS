#nullable enable

using MMP.Herald.Predicates;

namespace MMP.Herald.Configuration.Runtime;
/// <summary>
/// Runtime route definition using the core predicate DSL.
/// </summary>
public sealed record LoggingRuntimeRouteDefinition(
    string SinkName,
    PredicateSpec Predicate);
#nullable enable

using MMP.Herald.Levels;

namespace MMP.Herald.Routing;

/// <summary>
/// Optional introspection hook for sink chain nodes that can describe the
/// minimum log level they will admit. Walking the chain at pipeline compose
/// time lets the top-level filter raise its floor so events that no sink
/// would accept are never allocated.
/// </summary>
/// <remarks>
/// <para>
/// Implementations return <c>null</c> when the floor is unknown or depends
/// on more than level (for example, a predicate that inspects category or
/// context). The aggregator treats <c>null</c> as "accepts any level", so
/// a single unknown sink keeps the pipeline floor at the policy minimum.
/// Aggregating nodes (composites, routers) take the minimum over children
/// so the chain reports the lowest level any sink would accept.
/// </para>
///
/// <para>
/// <b>Reach by design is narrow.</b> Only sinks that carry their own
/// per-sink minimum-level filter have a meaningful answer — typically
/// <see cref="MMP.Herald.Filters.FilteringLogger"/> wrapping a downstream
/// sink, or sink providers configured with an explicit <c>MinLevel</c>.
/// Leaf writers such as the console renderer and the no-op sink accept
/// whatever the upstream filter already admitted; they have no
/// per-leaf floor to report. Not implementing the interface (or
/// implementing it and returning <c>null</c>) is the honest answer for
/// those sinks and is exactly what the aggregator expects. The
/// optimisation pays off in configurations that explicitly set sink
/// <c>MinLevel</c> values above the pipeline default; it is a
/// belt-and-braces move, not a pervasive one.
/// </para>
/// </remarks>
public interface ISinkChainLevelIntrospection
{
    /// <summary>
    /// Report the minimum level this sink or sink chain will admit, or
    /// <c>null</c> when the floor is unknown. The registry is used for
    /// rank comparisons in aggregating nodes.
    /// </summary>
    LogLevel? GetMinimumLevel(ILogLevelRegistry registry);
}

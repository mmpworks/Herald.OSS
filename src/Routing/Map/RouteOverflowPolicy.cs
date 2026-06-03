#nullable enable

namespace MMP.Herald.Routing.Map;

/// <summary>
/// What an open-key <see cref="MappedKernelSink"/> does with an event whose
/// routing key would create a sink beyond the configured cardinality cap.
///
/// <para>
/// Open-key routing auto-creates a downstream sink per distinct key. Distinct
/// keys are unbounded in general (a per-correlation-id key is effectively
/// one-per-request), so an un-capped router is an unbounded-sink / unbounded
/// file-handle foot-gun. The cap makes cardinality ownership explicit; this
/// enum makes the <i>overflow behaviour</i> an explicit, observable choice
/// rather than a silent spill.
/// </para>
///
/// <para>
/// There is deliberately no "unbounded" member. A caller who wants unbounded
/// routing must say so by setting the cap high — the type does not offer a
/// way to forget the cap exists.
/// </para>
/// </summary>
public enum RouteOverflowPolicy
{
    /// <summary>
    /// Drop the overflow event. Counted and surfaced via the router's overflow
    /// diagnostic so the drop is observable, never silent. Preferred when
    /// confidentiality matters more than completeness: a dropped event cannot
    /// leak one tenant's data into another tenant's file.
    /// </summary>
    Drop = 0,

    /// <summary>
    /// Route the overflow event to the default sink. Counted and surfaced via
    /// the overflow diagnostic.
    ///
    /// <para>
    /// <b>Confidentiality caveat — stated in the open.</b> Overflow events from
    /// different keys co-mingle in the default sink. Under a per-tenant
    /// framing, that is cross-key bleed triggered by cardinality. Choose this
    /// policy only when the default sink's audience is allowed to see every
    /// overflow key's events. When in doubt, prefer <see cref="Drop"/>.
    /// </para>
    /// </summary>
    RouteToDefault = 1,
}

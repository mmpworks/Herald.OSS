#nullable enable

namespace MMP.Herald.Configuration;

/// <summary>
/// Lifecycle state of a pipeline component, sibling-additive to the
/// existing <c>Disabled</c> bit that operator-config has carried since v1.
///
/// <para>
/// <see cref="Active"/> is the default; existing components keep reporting
/// Active without any code change (the interface default returns this
/// value). <see cref="Unsupported"/> is the post-boot state a component
/// transitions into when its declared <c>RequiredCapabilities</c> are no
/// longer satisfied by the current effective cap-set (e.g., a check-in
/// response narrowed the cap-set, an Enterprise to Pro downgrade left a
/// multi-tenant decorator stranded). An <c>Unsupported</c> component is
/// loaded but inactive: it stays in the process, is visible in diagnostics,
/// emits no events, and transitions back to <see cref="Active"/> if the
/// required caps are re-granted — no restart required.
/// </para>
///
/// <para>
/// Per ADR-220, the orchestrator that drives transitions
/// (<c>ComponentLifecycleCoordinator</c>) lives in the licensing engine,
/// not in Herald.OSS. The OSS surface ships the state value and the
/// metadata property; the coordinator that walks the registry on a
/// capability change is licensing-engine scope.
/// </para>
/// </summary>
public enum ComponentLifecycleState
{
    /// <summary>Component is running and dispatched to normally.</summary>
    Active = 0,

    /// <summary>
    /// Component declared <c>RequiredCapabilities</c> are not satisfied by
    /// the current effective cap-set. The component is loaded but wired
    /// out of the active pipeline; it transitions back to <see cref="Active"/>
    /// if the caps are re-granted, without requiring a process restart.
    /// </summary>
    Unsupported = 1,
}

#nullable enable

using System;
using System.Collections.Generic;
using MMP.Herald.Pipeline;

namespace MMP.Herald;

/// <summary>
/// Runtime query API for paid features that want to gate themselves at
/// runtime against the running edition's effective capability set.
///
/// <para>
/// Sibling to <see cref="HeraldEditionGate"/>. Both ship; both are
/// supported. The capability gate is the recommended path for new code:
/// a component declares which named capabilities it needs (e.g.
/// <c>"multi-tenant"</c>, <c>"compliance-overlay"</c>) and the gate
/// checks each one against <see cref="HeraldVersion.CurrentCapabilities"/>
/// (or, for tenant-scoped checks, against
/// <see cref="HeraldVersion.CapabilityResolver"/>). The edition gate
/// remains in place for existing code that gates by tier rank
/// (<c>HeraldEdition.Pro</c>, <c>HeraldEdition.Enterprise</c>) — the
/// migration target is "every paid component declares caps eventually";
/// the deprecation window is 12 months, removal targeted at Herald.OSS 2.0.
/// </para>
///
/// <para>
/// Two enforcement styles, matching <see cref="HeraldEditionGate"/>:
/// </para>
/// <list type="bullet">
///   <item>
///     <c>Require</c> / <c>RequireFor</c> / <c>RequireOn</c> — used at
///     code-path gates where reaching the gate without the capability
///     is a coding mistake. Throws
///     <see cref="HeraldCapabilityRequirementException"/>.
///   </item>
///   <item>
///     <c>AllowsFeature</c> / <c>AllowsFeatureFor</c> — used for
///     operator-config gates where a higher plan unlocks a feature but a
///     lower plan should not crash. Returns a bool.
///   </item>
/// </list>
///
/// <para>
/// Every check fires <see cref="OnCapabilityChecked"/> with the cap name,
/// the feature/component context, the optional tenant id, and the
/// granted/denied outcome. Subscribers include license-counting telemetry,
/// audit-chain emit, anomaly detection, and Dashboard live-introspection.
/// </para>
/// </summary>
public static class HeraldCapabilityGate
{
    /// <summary>
    /// Throws <see cref="HeraldCapabilityRequirementException"/> when the
    /// current process-global capability set does not contain
    /// <paramref name="capability"/>.
    /// </summary>
    public static void Require(string capability, string featureName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(capability);
        ArgumentException.ThrowIfNullOrWhiteSpace(featureName);

        EvaluateAndThrow(
            currentCaps: HeraldVersion.CurrentCapabilities,
            capability: capability,
            featureName: featureName,
            tenantId: null);
    }

    /// <summary>
    /// Tenant-scoped <c>Require</c>. Consults
    /// <see cref="HeraldVersion.CapabilityResolver"/> with
    /// <paramref name="tenantId"/>; a <see langword="null"/> tenant id
    /// falls back to the process-global capability set, preserving
    /// behaviour for single-tenant deployments.
    /// </summary>
    public static void RequireFor(string? tenantId, string capability, string featureName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(capability);
        ArgumentException.ThrowIfNullOrWhiteSpace(featureName);

        EvaluateAndThrow(
            currentCaps: ResolveCaps(tenantId),
            capability: capability,
            featureName: featureName,
            tenantId: tenantId);
    }

    /// <summary>
    /// Per-component capability check. Iterates
    /// <see cref="IComponentMetadata.RequiredCapabilities"/> in declaration
    /// order and throws on the FIRST missing capability (fail-fast,
    /// deterministic). The exception message is byte-identical across
    /// runs so operators can grep for it without ambiguity. When all
    /// declared capabilities are present, the method returns normally.
    /// </summary>
    /// <param name="component">The component to gate. Must not be null.</param>
    /// <param name="tenantId">
    /// Optional tenant slug. When non-null, the resolver is consulted for
    /// that tenant's effective cap-set. When null, the process-global
    /// cap-set is used (preserves single-tenant default).
    /// </param>
    /// <remarks>
    /// This is the per-component analogue of <see cref="Require(string, string)"/>:
    /// it walks the component's declared <c>RequiredCapabilities</c> and
    /// asks the same question of each cap. The component's
    /// <see cref="IComponentMetadata.ComponentName"/> is reported as the
    /// feature on any thrown exception.
    /// </remarks>
    public static void RequireOn(IComponentMetadata component, string? tenantId = null)
    {
        ArgumentNullException.ThrowIfNull(component);

        var required = component.RequiredCapabilities;
        if (required.Count == 0) return;

        var currentCaps = ResolveCaps(tenantId);
        var featureName = component.ComponentName;

        // Cognitive complexity note: single linear walk, single throw on
        // first miss. Echo amendment #3 (fail-fast on first missing cap
        // in declaration order) is the contract here — DO NOT switch to
        // batched/aggregated semantics without ratification, because the
        // exception-message stability is the operator-paste-into-ticket
        // contract.
        foreach (var requirement in required)
        {
            var granted = currentCaps.Contains(requirement.Name);
            FireChecked(requirement.Name, featureName, tenantId, granted);
            if (!granted)
            {
                throw new HeraldCapabilityRequirementException(
                    requirement.Name, featureName, tenantId);
            }
        }
    }

    /// <summary>
    /// Non-throwing variant. Returns <see langword="true"/> when the
    /// current process-global capability set contains
    /// <paramref name="capability"/>.
    /// </summary>
    public static bool AllowsFeature(string capability)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(capability);
        return Evaluate(HeraldVersion.CurrentCapabilities, capability, tenantId: null);
    }

    /// <summary>
    /// Tenant-scoped <c>AllowsFeature</c>. Consults
    /// <see cref="HeraldVersion.CapabilityResolver"/> with
    /// <paramref name="tenantId"/>; a <see langword="null"/> tenant id
    /// falls back to the process-global capability set.
    /// </summary>
    public static bool AllowsFeatureFor(string? tenantId, string capability)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(capability);
        return Evaluate(ResolveCaps(tenantId), capability, tenantId);
    }

    /// <summary>
    /// Observation event. Fires once per gate evaluation — success AND
    /// failure paths both raise the event. Subscribers MUST NOT throw;
    /// any subscriber exception is swallowed so a single buggy listener
    /// cannot corrupt gate state or take down a paid-binary registration.
    ///
    /// <para>
    /// Subscriber identities the design anticipates: Azure license-counting
    /// telemetry, FFIEC audit-trail emit, anomaly detection, Dashboard
    /// live-introspection, Echo test instrumentation. Mirrors the
    /// observation-seam pattern Rosanne B-5 (OnTenantRegistration) ships.
    /// </para>
    /// </summary>
    public static event Action<CapabilityGateEvaluation>? OnCapabilityChecked;

    // Internal overload for unit tests: drives the gate against any
    // capability set without touching the process-global HeraldVersion.
    // The public methods delegate to the same Evaluate/Fire helpers so
    // production callers and tests exercise the identical logic path.
    internal static void RequireOn(
        IReadOnlySet<string> currentCaps,
        string capability,
        string featureName)
    {
        ArgumentNullException.ThrowIfNull(currentCaps);
        ArgumentException.ThrowIfNullOrWhiteSpace(capability);
        ArgumentException.ThrowIfNullOrWhiteSpace(featureName);

        EvaluateAndThrow(currentCaps, capability, featureName, tenantId: null);
    }

    internal static void ResetForTesting()
    {
        OnCapabilityChecked = null;
    }

    private static IReadOnlySet<string> ResolveCaps(string? tenantId)
    {
        // Default resolver returns the process-global cap-set for every
        // tenant id (including null). A multi-tenant host swaps in a
        // delegate that consults its per-tenant claim store. The delegate
        // is held on HeraldVersion so a single source of truth applies
        // both to gate evaluation and to any external diagnostics.
        var resolver = HeraldVersion.CapabilityResolver;
        return resolver(tenantId);
    }

    private static bool Evaluate(IReadOnlySet<string> currentCaps, string capability, string? tenantId)
    {
        var granted = currentCaps.Contains(capability);
        // AllowsFeature is the "operator-config" entry: caller has no
        // featureName context to share. Use the capability name as the
        // feature label so the event payload is still attributable.
        FireChecked(capability, capability, tenantId, granted);
        return granted;
    }

    private static void EvaluateAndThrow(
        IReadOnlySet<string> currentCaps,
        string capability,
        string featureName,
        string? tenantId)
    {
        var granted = currentCaps.Contains(capability);
        FireChecked(capability, featureName, tenantId, granted);
        if (!granted)
        {
            throw new HeraldCapabilityRequirementException(capability, featureName, tenantId);
        }
    }

    private static void FireChecked(string capability, string featureName, string? tenantId, bool granted)
    {
        // Snapshot the delegate so a concurrent subscribe / unsubscribe
        // cannot null-deref or skip late-added handlers mid-fire. C#
        // event-add is atomic on the field, so this is safe under
        // contention.
        var handler = OnCapabilityChecked;
        if (handler is null) return;

        var evaluation = new CapabilityGateEvaluation(
            Capability: capability,
            FeatureName: featureName,
            TenantId: tenantId,
            Granted: granted,
            At: DateTimeOffset.UtcNow);

        // Walk each invocation target individually so one throwing
        // subscriber cannot prevent the others from receiving the
        // notification. Swallow per-subscriber exceptions — the contract
        // says a buggy telemetry listener must not corrupt gate state
        // (T-CAP-27). The compiler-generated multi-cast delegate's
        // GetInvocationList() returns the snapshot in subscription order.
        foreach (var target in handler.GetInvocationList())
        {
            try
            {
                ((Action<CapabilityGateEvaluation>)target)(evaluation);
            }
            catch
            {
                // Intentionally swallowed. A future enhancement could
                // route to a failure sink; today the contract is "do not
                // crash the gate." Tests T-CAP-27 / T-CAP-48 pin the
                // observed behaviour.
            }
        }
    }
}

/// <summary>
/// Payload for <see cref="HeraldCapabilityGate.OnCapabilityChecked"/>.
/// One value type per gate evaluation; fields are inert and safe to log
/// or forward to an external telemetry pipeline.
/// </summary>
/// <param name="Capability">The capability checked.</param>
/// <param name="FeatureName">The feature or component the gate protected.</param>
/// <param name="TenantId">
/// Tenant slug supplied to a tenant-scoped overload, or <see langword="null"/>
/// for process-global checks.
/// </param>
/// <param name="Granted">
/// <see langword="true"/> when the capability was present in the effective
/// cap-set; <see langword="false"/> when missing (an exception was thrown
/// if the entry-point was a <c>Require*</c> method).
/// </param>
/// <param name="At">The moment the evaluation completed.</param>
public sealed record CapabilityGateEvaluation(
    string Capability,
    string FeatureName,
    string? TenantId,
    bool Granted,
    DateTimeOffset At);

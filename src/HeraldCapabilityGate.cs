#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace MMP.Herald;

/// <summary>
/// Capability-aware registration gate — the rev 2 sibling to
/// <see cref="HeraldEditionGate"/>. Components declare their capability
/// requirements via <c>RequiredCapabilities</c> on
/// <see cref="Pipeline.IComponentMetadata"/> /
/// <see cref="Configuration.KnownSink"/> /
/// <see cref="Configuration.PipelineStep"/>; the gate asks the
/// set-membership question against the running effective cap-set
/// (<see cref="HeraldVersion.CurrentCapabilities"/> via
/// <see cref="HeraldVersion.CapabilityResolver"/>).
///
/// <para>
/// <b>Migration posture (C2, rev 2 / rev 3).</b> Gates that previously
/// asked "is the running edition at or above the required edition?" now
/// ask "does the running license grant this capability?" The unified
/// evaluation rule lives in the call sites that already evaluate
/// <c>MinimumEdition</c> — see Section 4.2 of
/// <c>docs/_wip/capability-composition-plan-v1.md</c> for the pseudocode.
/// Capability path wins when declared (non-empty
/// <c>RequiredCapabilities</c>); legacy edition path is the fallback.
/// Both work for the 12-month deprecation window.
/// </para>
///
/// <para>
/// <b>Fail-fast contract (Echo amendment #3).</b> When a component declares
/// N caps and K are missing, the FIRST missing cap in declaration order
/// throws — not lexicographic, not most-missing, not batched. The exception
/// message is byte-identical across runs so an operator's grep stays stable.
/// </para>
///
/// <para>
/// <b>Per-tenant overlay (Rosanne S-1).</b>
/// <see cref="RequireFor"/> consults <see cref="HeraldVersion.CapabilityResolver"/>
/// with the supplied tenant id; the default resolver returns the
/// process-global cap-set so single-tenant deployments behave identically
/// to a no-tenant model. A multi-tenant host swaps the resolver at startup
/// to consult its per-tenant claim store.
/// </para>
///
/// <para>
/// <b>Observation (Rosanne S-2).</b> Every evaluation fires
/// <see cref="OnCapabilityChecked"/> (both success and failure paths).
/// Subscribers are observation-only — a throwing handler is isolated and
/// does NOT affect the gate's success/failure decision.
/// </para>
/// </summary>
public static class HeraldCapabilityGate
{
    /// <summary>
    /// Fires after every <see cref="Require"/> / <see cref="RequireFor"/> /
    /// <see cref="AllowsFeature"/> / <see cref="AllowsFeatureFor"/> /
    /// <see cref="Probe"/> call, carrying the cap, feature, optional tenant
    /// id, and the granted/denied verdict.
    ///
    /// <para>
    /// <b>Isolation contract:</b> a subscriber that throws does NOT cause
    /// the gate to misbehave. The throw is swallowed; the gate proceeds
    /// with its success/failure decision unaffected. Subsequent gate calls
    /// continue to fire the event for other subscribers. This prevents one
    /// buggy telemetry consumer from taking down every paid-binary
    /// registration (Rosanne S-2 isolation requirement).
    /// </para>
    ///
    /// <para>
    /// Reference-type assignment is atomic in C# so concurrent
    /// subscribe/unsubscribe during evaluation is race-safe. Subscribers
    /// see either the old delegate set or the new one — never a torn
    /// reference.
    /// </para>
    /// </summary>
    public static event Action<CapabilityGateEvaluation>? OnCapabilityChecked;

    /// <summary>
    /// Throws <see cref="HeraldCapabilityRequirementException"/> when the
    /// process-global effective cap-set does not contain
    /// <paramref name="capability"/>. The 95% call shape — single-tenant
    /// deployments use this overload.
    /// </summary>
    public static void Require(string capability, string featureName)
    {
        RequireFor(tenantId: null, capability, featureName);
    }

    /// <summary>
    /// Per-tenant overload. Consults
    /// <see cref="HeraldVersion.CapabilityResolver"/> with
    /// <paramref name="tenantId"/>. When <paramref name="tenantId"/> is null,
    /// the default resolver returns the process-global cap-set —
    /// equivalent to <see cref="Require"/>. Rosanne S-1.
    /// </summary>
    public static void RequireFor(string? tenantId, string capability, string featureName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(capability);
        ArgumentException.ThrowIfNullOrWhiteSpace(featureName);

        var effective = ResolveCaps(tenantId);
        var granted = effective.Contains(capability);
        FireObservation(capability, featureName, tenantId, granted);

        if (!granted)
        {
            throw new HeraldCapabilityRequirementException(capability, featureName, tenantId);
        }
    }

    /// <summary>
    /// Internal evaluation overload used by tests to drive the gate
    /// against any cap-set without touching
    /// <see cref="HeraldVersion.CurrentCapabilities"/> /
    /// <see cref="HeraldVersion.CapabilityResolver"/>. The public methods
    /// route through <see cref="ResolveCaps"/> so production callers
    /// exercise the resolver path; tests use this overload to assert on
    /// the throw-or-not decision deterministically.
    /// </summary>
    internal static void RequireOn(
        IReadOnlySet<string> currentCaps,
        string capability,
        string featureName)
    {
        ArgumentNullException.ThrowIfNull(currentCaps);
        ArgumentException.ThrowIfNullOrWhiteSpace(capability);
        ArgumentException.ThrowIfNullOrWhiteSpace(featureName);

        var granted = currentCaps.Contains(capability);
        FireObservation(capability, featureName, tenantId: null, granted);

        if (!granted)
        {
            throw new HeraldCapabilityRequirementException(capability, featureName);
        }
    }

    /// <summary>
    /// Non-throwing query for soft-fallback paths. Returns <c>true</c>
    /// when <paramref name="capability"/> is present in the process-global
    /// effective cap-set. Mirrors
    /// <see cref="HeraldEditionGate.AllowsFeature(HeraldEdition)"/>'s shape.
    /// </summary>
    public static bool AllowsFeature(string capability)
        => AllowsFeatureFor(tenantId: null, capability);

    /// <summary>
    /// Per-tenant non-throwing query. See <see cref="RequireFor"/> for the
    /// resolver-consultation rule.
    /// </summary>
    public static bool AllowsFeatureFor(string? tenantId, string capability)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(capability);

        var effective = ResolveCaps(tenantId);
        var granted = effective.Contains(capability);
        FireObservation(capability, featureName: capability, tenantId, granted);
        return granted;
    }

    /// <summary>
    /// Lookup-only probe that returns the granted/denied verdict WITHOUT
    /// firing <see cref="OnCapabilityChecked"/>. Use from observation
    /// surfaces (Dashboard render queries, audit-trail rebuild walks)
    /// that should not appear as a gate evaluation to other subscribers.
    /// </summary>
    /// <remarks>
    /// The plan flags this as a future SDK-only extension at
    /// <see cref="Pipeline.CapabilityRequirement"/> (per-cap lookup-only
    /// sentinel). Ships the call shape now so consumers do not invent a
    /// parallel API later.
    /// </remarks>
    public static bool Probe(string? tenantId, string capability)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(capability);

        var effective = ResolveCaps(tenantId);
        return effective.Contains(capability);
    }

    // ── Capability name shape validator (Rosanne #15-style hygiene check) ──

    /// <summary>
    /// Validates that a capability name follows the documented shape:
    /// lowercase letters / digits / hyphens, optionally prefixed by a
    /// vendor namespace (<c>&lt;vendor&gt;.&lt;cap&gt;</c>). MMPWorks-canonical
    /// caps use the bare hyphenated form (<c>multi-tenant</c>); third-party
    /// vendors should namespace (<c>acme.advanced-redaction</c>).
    ///
    /// <para>
    /// Returns <c>true</c> for well-formed names; <c>false</c> otherwise.
    /// Diagnostic-level only — no throw; the caller decides whether to log,
    /// reject, or accept. The plan's RC4 mitigation is warn-and-skip; this
    /// method is the substring that drives the warning.
    /// </para>
    ///
    /// <para>
    /// The catalog-membership check (is this cap in
    /// <c>EditionCapabilityPresets.Catalog</c>?) lives in
    /// <c>MMP.Licensing.Contracts</c> — Herald.OSS has no catalog
    /// dependency, so this validator is shape-only.
    /// </para>
    /// </summary>
    public static bool ValidateCapabilityNameShape(string capability)
    {
        if (string.IsNullOrWhiteSpace(capability)) return false;
        return CapabilityNameRegex.IsMatch(capability);
    }

    // Named-capture group per CODING_INSTRUCTIONS.md regex policy. Shape:
    //   <vendor>.<segment>  OR  <segment>
    // <segment> := lowercase letter, optionally followed by lowercase letters / digits / hyphens,
    //              ending in a letter or digit (no trailing hyphen).
    // <vendor>  := same shape; presence is optional.
    private static readonly Regex CapabilityNameRegex = new(
        @"^(?<full>(?<vendor>[a-z][a-z0-9-]*[a-z0-9]\.)?(?<segment>[a-z][a-z0-9-]*[a-z0-9]|[a-z]))$",
        RegexOptions.Compiled);

    // ── Helpers ────────────────────────────────────────────────────────

    private static IReadOnlySet<string> ResolveCaps(string? tenantId)
    {
        // CapabilityResolver default is `_ => CurrentCapabilities` so the
        // null-tenant call collapses to the process-global path, preserving
        // documented v1 single-tenant behavior. A multi-tenant wrapper
        // overrides the resolver to consult its per-tenant claim store.
        var resolver = HeraldVersion.CapabilityResolver;
        return resolver?.Invoke(tenantId) ?? ImmutableHashSet<string>.Empty;
    }

    private static void FireObservation(
        string capability,
        string featureName,
        string? tenantId,
        bool granted)
    {
        // Snapshot the delegate before invoking so a concurrent unsubscribe
        // does not throw inside the iteration. Each subscriber runs inside
        // its own try/catch so a thrown handler does NOT cascade to the
        // caller — Rosanne S-2 isolation contract.
        var handlers = OnCapabilityChecked;
        if (handlers is null) return;

        var payload = new CapabilityGateEvaluation(
            capability,
            featureName,
            tenantId,
            granted,
            DateTimeOffset.UtcNow);

        foreach (var handler in handlers.GetInvocationList())
        {
            try
            {
                ((Action<CapabilityGateEvaluation>)handler)(payload);
            }
            catch
            {
                // Observation-only — a throwing handler must not corrupt the
                // gate's success/failure decision. Swallow + continue so other
                // subscribers still get the event. Plan ratification: the
                // gate is the load-bearing path; telemetry is best-effort.
            }
        }
    }
}

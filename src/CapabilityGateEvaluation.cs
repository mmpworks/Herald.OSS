#nullable enable

using System;

namespace MMP.Herald;

/// <summary>
/// Payload fired by <see cref="HeraldCapabilityGate.OnCapabilityChecked"/>
/// on every gate evaluation — success AND failure paths. Rosanne S-2
/// observation seam.
///
/// <para>
/// Five named downstream consumers identified at amendment time
/// (capability-composition-plan-v1 Section 4.1.1):
/// </para>
///
/// <list type="number">
///   <item><b>Azure license-counting telemetry</b> — feeds usage-based billing readiness.</item>
///   <item><b>FFIEC audit-trail</b> — "show me every advanced-strategies check on this server" is a real audit question.</item>
///   <item><b>Anomaly detection</b> — a Pro customer suddenly hitting <c>multi-tenant</c> checks they should not is a license-shape probe.</item>
///   <item><b>Nancy Dashboard.Paid live-introspection</b> — saves polling on the license panel.</item>
///   <item><b>Echo test instrumentation</b> — event subscription replaces try/catch in cap-evaluation tests.</item>
/// </list>
///
/// <para>
/// Subscribers are observation-only — throwing in a handler must NOT
/// affect the gate's success/failure decision. See
/// <see cref="HeraldCapabilityGate.OnCapabilityChecked"/> for the isolation
/// contract.
/// </para>
/// </summary>
/// <param name="Capability">The capability name the gate checked.</param>
/// <param name="FeatureName">The feature whose registration drove the check.</param>
/// <param name="TenantId">
/// Tenant id when the call came through <see cref="HeraldCapabilityGate.RequireFor"/>;
/// null for process-global checks (the 95% case).
/// </param>
/// <param name="Granted">
/// True when the cap was present in the effective cap-set, false when missing.
/// </param>
/// <param name="At">UTC timestamp of the evaluation.</param>
public sealed record CapabilityGateEvaluation(
    string Capability,
    string FeatureName,
    string? TenantId,
    bool Granted,
    DateTimeOffset At);

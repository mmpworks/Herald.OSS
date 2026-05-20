#nullable enable

using System;

namespace MMP.Herald;

/// <summary>
/// Thrown by <see cref="HeraldCapabilityGate"/> when a required capability
/// is not present in the current effective cap-set.
///
/// <para>
/// Sibling to <see cref="HeraldEditionRequirementException"/>: the two
/// types report two different concerns. The edition exception fires when
/// a component declares only a <c>MinimumEdition</c> floor and the running
/// edition is below it. The capability exception fires when a component
/// declares <c>RequiredCapabilities</c> and one of the named caps is
/// missing from the current cap-set. An operator reading the exception
/// type should never have to guess which remediation path applies:
/// edition mismatch is "install the higher-tier module"; capability
/// mismatch is "have your license token re-issued with the missing cap"
/// (or upgrade to a preset that includes it).
/// </para>
///
/// <para>
/// When the gate was invoked through the tenant-scoped
/// <see cref="HeraldCapabilityGate.RequireFor(string?, string, string)"/>
/// overload, <see cref="TenantId"/> carries the tenant slug so an operator
/// message can name the tenant that lacked the cap. For process-global
/// checks <see cref="TenantId"/> is <see langword="null"/>.
/// </para>
/// </summary>
public sealed class HeraldCapabilityRequirementException : Exception
{
    /// <summary>Name of the capability whose gate was crossed.</summary>
    public string Capability { get; }

    /// <summary>Feature or component the gate was protecting.</summary>
    public string Feature { get; }

    /// <summary>
    /// Tenant slug supplied to the per-tenant gate overload, or
    /// <see langword="null"/> for process-global checks.
    /// </summary>
    public string? TenantId { get; }

    /// <summary>Constructs an exception for a process-global capability check.</summary>
    public HeraldCapabilityRequirementException(string capability, string feature)
        : this(capability, feature, tenantId: null)
    {
    }

    /// <summary>Constructs an exception that names the offending tenant.</summary>
    public HeraldCapabilityRequirementException(string capability, string feature, string? tenantId)
        : base(BuildMessage(capability, feature, tenantId))
    {
        Capability = capability;
        Feature = feature;
        TenantId = tenantId;
    }

    private static string BuildMessage(string capability, string feature, string? tenantId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(capability);
        ArgumentException.ThrowIfNullOrWhiteSpace(feature);

        // Operator-actionable shape: name the feature, name the missing
        // capability, suggest the additive remediation path. The "upgrade
        // to a plan that includes" phrase is the operator's grep target —
        // see test matrix T-CAP-4. Deterministic content only; no
        // timestamps, no formatting that varies across builds.
        var tenantSuffix = tenantId is null
            ? ""
            : $" (tenant: {tenantId})";
        return $"{feature} requires the '{capability}' capability{tenantSuffix}. " +
               $"Upgrade to a plan that includes '{capability}' or have your license token " +
               $"re-issued with the missing capability.";
    }
}

#nullable enable

using System;

namespace MMP.Herald;

/// <summary>
/// Thrown by <see cref="HeraldCapabilityGate"/> when a component declares
/// <c>RequiredCapabilities</c> and the running effective cap-set does NOT
/// contain the named capability. Sibling to
/// <see cref="HeraldEditionRequirementException"/> — the two report distinct
/// operator-remediation paths:
///
/// <list type="bullet">
///   <item>
///     <see cref="HeraldEditionRequirementException"/> — "upgrade to a higher
///     edition OR migrate the component to declare RequiredCapabilities."
///   </item>
///   <item>
///     <see cref="HeraldCapabilityRequirementException"/> — "add the missing
///     cap to the customer's license token (PATCH /licenses/{id}/capabilities)
///     OR ship a token with a preset that already includes the cap."
///   </item>
///   <item>
///     <see cref="HeraldLicenseRevokedException"/> — "license has been
///     server-flagged Revoked; contact sales to restore." Distinct branch.
///   </item>
/// </list>
///
/// <para>
/// Server-side handlers that previously caught
/// <see cref="HeraldEditionRequirementException"/> in the widened catch at
/// <c>Modules/Server/Program.cs:473-474</c> should catch this exception
/// alongside it so the operator sees a clean 402 ProblemDetails with the
/// missing-cap name rather than a 500 from an unhandled exception.
/// </para>
/// </summary>
public sealed class HeraldCapabilityRequirementException : Exception
{
    /// <summary>Name of the missing capability (e.g. <c>multi-tenant</c>).</summary>
    public string Capability { get; }

    /// <summary>Name of the feature whose registration was gated.</summary>
    public string Feature { get; }

    /// <summary>
    /// Optional tenant identifier when the failure came from
    /// <see cref="HeraldCapabilityGate.RequireFor(string?, string, string)"/>.
    /// Null for process-global checks.
    /// </summary>
    public string? TenantId { get; }

    public HeraldCapabilityRequirementException(string capability, string feature, string? tenantId = null)
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

        var tenantSuffix = string.IsNullOrEmpty(tenantId)
            ? string.Empty
            : $" (tenant: {tenantId})";

        // Reproducibility contract (Echo amendment #3): the message contains
        // no nondeterministic content (no timestamps, no GUIDs) so an
        // operator's two runs produce byte-identical strings they can grep
        // and paste into a ticket without noise. Upgrade-hint phrasing
        // matches the rev 3 spec (Section 4.5) for operator-facing remediation.
        return $"{feature} requires capability '{capability}'{tenantSuffix}. " +
               $"Add '{capability}' to the customer's license token via " +
               $"PATCH /licenses/{{id}}/capabilities, or upgrade to a plan that " +
               $"includes '{capability}'.";
    }
}

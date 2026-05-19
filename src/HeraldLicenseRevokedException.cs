#nullable enable

using System;

namespace MMP.Herald;

/// <summary>
/// Thrown when the licensing engine has confirmed (via a SSoT check-in
/// response or a server-pushed Revoked notification) that the running
/// license has been Revoked. Distinct from
/// <see cref="HeraldCapabilityRequirementException"/> and
/// <see cref="HeraldEditionRequirementException"/> because the operator
/// remediation path is fundamentally different:
///
/// <list type="bullet">
///   <item>
///     <b>Expired</b> (good faith) — customer's renewal slipped, payment
///     late, AR cycle running long. Demo progression applies (rev 3 G2
///     state machine): days 1-30 weekly nag, days 31-60 daily nag, day 61+
///     per-tenant shutoff. <see cref="HeraldCapabilityRequirementException"/>
///     is the post-shutoff exception class (the per-tenant cap-set is
///     replaced with an empty set).
///   </item>
///   <item>
///     <b>Revoked</b> (this exception) — customer in dispute / fraud /
///     intentional kill. Immediate hard-fail; no Demo progression; sales
///     contact required. The exception type is the operator's signal that
///     no self-serve remediation exists.
///   </item>
/// </list>
///
/// <para>
/// This type ships in 0.7.0 alongside the gate primitives. The raising side
/// (license-state machine, check-in client) lands in 2.1.0 of
/// MMP.Licensing per the Stage B G-5 lifecycle wave. The type is in OSS
/// (rather than MMP.Licensing) so the Server's widened catch at
/// <c>Modules/Server/Program.cs:473-474</c> can name all three exception
/// classes without taking a forward dependency on the licensing engine.
/// </para>
/// </summary>
public sealed class HeraldLicenseRevokedException : Exception
{
    /// <summary>
    /// Optional reason code from the license server (e.g. "dispute",
    /// "terms-of-service", "fraud-investigation"). Null when the client
    /// received a Revoked flag with no reason payload.
    /// </summary>
    public string? ReasonCode { get; }

    /// <summary>
    /// Optional license id from the server, useful for support correspondence.
    /// Null when the client cannot resolve it (early-boot, offline).
    /// </summary>
    public string? LicenseId { get; }

    public HeraldLicenseRevokedException(string? reasonCode = null, string? licenseId = null)
        : base(BuildMessage(reasonCode, licenseId))
    {
        ReasonCode = reasonCode;
        LicenseId = licenseId;
    }

    public HeraldLicenseRevokedException(string message, string? reasonCode = null, string? licenseId = null)
        : base(message)
    {
        ReasonCode = reasonCode;
        LicenseId = licenseId;
    }

    private static string BuildMessage(string? reasonCode, string? licenseId)
    {
        var reasonSuffix = string.IsNullOrEmpty(reasonCode)
            ? string.Empty
            : $" Reason: {reasonCode}.";
        var licenseSuffix = string.IsNullOrEmpty(licenseId)
            ? string.Empty
            : $" License: {licenseId}.";

        return "The Herald license has been Revoked by the license server." +
               reasonSuffix + licenseSuffix +
               " Contact sales@mmpworks.com to restore. " +
               "No self-serve remediation is available; the license must be re-issued.";
    }
}

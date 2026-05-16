#nullable enable

using System;
using System.Text.RegularExpressions;

namespace MMP.Herald.Quick;

/// <summary>
/// Validates and normalizes tenant identifiers used by <see cref="HeraldRegistry"/>.
///
/// <para>
/// Every registry operation is tenant-scoped. Callers that do not supply a
/// tenant land on <see cref="Default"/>, so a single-tenant deployment is the
/// N=1 case of the multi-tenant model — no separate code path, no opt-in
/// shim. Herald.OSS accepts any non-empty tenant name by default; the
/// <see cref="TenantAdmissionPolicy"/> hook lets a commercial wrapper layer
/// license-aware admission control on top without modifying OSS code.
/// </para>
/// </summary>
public static class HeraldTenant
{
    /// <summary>
    /// Tenant used when a caller does not specify one. All existing
    /// single-tenant code paths resolve here.
    /// </summary>
    public const string Default = "default";

    /// <summary>
    /// Replaceable admission policy invoked from
    /// <see cref="HeraldRegistryInstance"/>'s <c>Register</c> and
    /// <c>TryRegister</c> paths after the tenant id has been normalised,
    /// before the registration is published. OSS ships a no-op default that
    /// admits every valid tenant. Commercial wrappers (Pro / Enterprise)
    /// replace this with a license-aware policy that throws when the current
    /// build is not licensed for non-default tenants.
    ///
    /// <para>
    /// Process-wide static state. Set once at application startup before any
    /// pipeline registration runs. Reference-type assignment is atomic in C#,
    /// so concurrent reads during registration see either the old delegate
    /// or the new one — never a torn reference. The static-property shape is
    /// the deliberate seam: an Enterprise plug-in swaps the policy without
    /// touching OSS source, and an OSS adopter who wants the same gate locally
    /// assigns their own delegate at startup.
    /// </para>
    /// </summary>
    public static Action<string> TenantAdmissionPolicy { get; set; } = NoOpAdmission;

    // Default policy: admit every normalised tenant id. OSS imposes no
    // edition gate; the caller has already passed the regex validation in
    // Normalize, so there is nothing further to check here.
    private static void NoOpAdmission(string _) { }

    // Tenant IDs must be filesystem-safe and URL-safe because they appear in
    // per-tenant config file paths and in API route segments. Named capture
    // group per the repo's regex policy.
    private static readonly Regex ValidTenantRegex =
        new(@"^(?<id>[a-zA-Z0-9_\-]{1,64})$", RegexOptions.Compiled);

    /// <summary>
    /// Validate a tenant ID and return its canonical (lowercase) form.
    /// Throws <see cref="ArgumentException"/> when the input is null, empty,
    /// or contains characters outside the allowed set.
    /// </summary>
    public static string Normalize(string tenant)
    {
        if (string.IsNullOrWhiteSpace(tenant))
            throw new ArgumentException("Tenant id is required.", nameof(tenant));

        if (!ValidTenantRegex.IsMatch(tenant))
            throw new ArgumentException(
                $"Tenant id '{tenant}' is invalid. Allowed: letters, digits, underscore, hyphen; 1-64 chars.",
                nameof(tenant));

        return tenant.ToLowerInvariant();
    }

    /// <summary>
    /// True when <paramref name="tenant"/> is the default tenant. Case-insensitive.
    /// </summary>
    public static bool IsDefault(string tenant) =>
        string.Equals(tenant, Default, StringComparison.OrdinalIgnoreCase);
}

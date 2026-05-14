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
/// shim. Enterprise editions unlock additional tenants; Community and Pro
/// builds reject non-default tenants at registration time.
/// </para>
/// </summary>
public static class HeraldTenant
{
    /// <summary>
    /// Tenant used when a caller does not specify one. All existing
    /// single-tenant code paths resolve here.
    /// </summary>
    public const string Default = "default";

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

    /// <summary>
    /// Tenant validation hook retained for API parity; Herald.OSS is a single
    /// distribution and accepts any non-empty tenant name unchanged.
    /// </summary>
    public static void EnsureAllowedForCurrentEdition(string tenant)
    {
    }
}

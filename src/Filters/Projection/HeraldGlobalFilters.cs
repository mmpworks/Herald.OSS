#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace MMP.Herald.Filters.Projection;

/// <summary>
/// Process-wide projection-filter overlay. Two layers, by precedence:
///
/// <list type="bullet">
///   <item><b>Global rules</b> (applies to every tenant) — registered
///         via <see cref="AddGlobalRule"/>.</item>
///   <item><b>Per-tenant rules</b> (override global for the same
///         property name in that tenant) — registered via
///         <see cref="AddTenantRule"/>.</item>
/// </list>
///
/// <para>At pipeline build time, <see cref="GetEffectiveRulesFor"/>
/// resolves the rules for a target tenant: tenant rules override
/// global rules per property name. The pipeline's own
/// <c>WithProjectionFilter</c> calls compose on top — pipeline rules
/// override tenant rules for the same property name.</para>
///
/// <para>Resolution order (most specific wins per property name):
/// <c>Pipeline → Tenant → Global → no rule</c>.</para>
///
/// <para>Edition: registering a per-tenant rule requires the
/// Enterprise edition (multi-tenant is gated). Global rules work on
/// all editions because they're not tenant-aware. The check happens
/// at registration time, not at evaluation time, so the hot path
/// pays no edition-gate cost.</para>
///
/// <para>Thread safety: both registries are <see cref="ConcurrentDictionary{TKey,TValue}"/>;
/// reads are lock-free; writes are atomic per-key. The list of rules
/// for one (tenant, property) pair is replaced wholesale by
/// <see cref="AddTenantRule"/> / <see cref="AddGlobalRule"/> —
/// callers can re-register to mutate.</para>
/// </summary>
public static class HeraldGlobalFilters
{
    // Global rules: property name -> rule. One rule per property at
    // each layer; the most recent registration replaces.
    private static readonly ConcurrentDictionary<string, ProjectionFilterRule> _global =
        new(StringComparer.Ordinal);

    // Per-tenant rules: tenant -> (property name -> rule).
    private static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, ProjectionFilterRule>> _byTenant =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Register a global projection-filter rule. The rule applies to
    /// every tenant unless a tenant-specific rule for the same
    /// property name overrides it. Re-registering with the same
    /// property name replaces the prior rule.
    /// </summary>
    public static void AddGlobalRule(string propertyName, string rule)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        ArgumentException.ThrowIfNullOrWhiteSpace(rule);
        var parsed = ProjectionFilterRule.Parse(propertyName, rule);
        _global[propertyName] = parsed;
    }

    /// <summary>
    /// Register a per-tenant projection-filter rule. Overrides the
    /// global rule (if any) for the same property name when the
    /// pipeline runs under <paramref name="tenant"/>. Re-registering
    /// with the same property name replaces the prior tenant rule.
    /// </summary>
    public static void AddTenantRule(string tenant, string propertyName, string rule)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenant);
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        ArgumentException.ThrowIfNullOrWhiteSpace(rule);

        // Multi-tenant is Enterprise. Registering tenant-specific
        // rules on Community/Pro builds throws at registration time.
        HeraldEditionGate.Require(HeraldEdition.Enterprise, "AddTenantRule");

        var parsed = ProjectionFilterRule.Parse(propertyName, rule);
        var bucket = _byTenant.GetOrAdd(tenant, _ => new(StringComparer.Ordinal));
        bucket[propertyName] = parsed;
    }

    /// <summary>
    /// Remove a global rule by property name. Idempotent — no-op if
    /// no rule was registered. Returns <c>true</c> if a rule was
    /// removed.
    /// </summary>
    public static bool RemoveGlobalRule(string propertyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        return _global.TryRemove(propertyName, out _);
    }

    /// <summary>
    /// Remove a per-tenant rule by property name. Idempotent —
    /// no-op if no rule was registered for that (tenant, property).
    /// </summary>
    public static bool RemoveTenantRule(string tenant, string propertyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenant);
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        return _byTenant.TryGetValue(tenant, out var bucket) &&
               bucket.TryRemove(propertyName, out _);
    }

    /// <summary>
    /// Resolve the effective overlay rules for a target tenant.
    /// Tenant rules take precedence over global rules per property
    /// name; the result is a snapshot at call time. Pass <c>null</c>
    /// or <see cref="HeraldTenant.Default"/> to get global-only.
    /// </summary>
    public static IReadOnlyList<ProjectionFilterRule> GetEffectiveRulesFor(string? tenant)
    {
        // Build a property-name -> rule map, starting with globals
        // and overlaying tenant rules.
        var resolved = new Dictionary<string, ProjectionFilterRule>(StringComparer.Ordinal);
        foreach (var kv in _global) resolved[kv.Key] = kv.Value;

        if (!string.IsNullOrEmpty(tenant) && _byTenant.TryGetValue(tenant, out var bucket))
        {
            foreach (var kv in bucket) resolved[kv.Key] = kv.Value;
        }

        if (resolved.Count == 0) return Array.Empty<ProjectionFilterRule>();
        var list = new List<ProjectionFilterRule>(resolved.Count);
        foreach (var v in resolved.Values) list.Add(v);
        return list;
    }

    /// <summary>
    /// Reset all overlay state. Intended for tests and host shutdown
    /// — production hosts don't typically clear the registry.
    /// </summary>
    public static void Clear()
    {
        _global.Clear();
        _byTenant.Clear();
    }
}

#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MMP.Herald.Quick;

/// <summary>
/// Instance-scoped pipeline registry. Holds the same nested
/// <see cref="ConcurrentDictionary{TKey,TValue}"/> map of
/// <c>(tenant → name → registration)</c> that previously lived as a
/// <c>public static class</c>; the static <see cref="HeraldRegistry"/>
/// now forwards every method to <see cref="HeraldHost.Default"/>'s
/// instance of this class.
///
/// <para>
/// Tests and multi-tenant scenarios that need an isolated pipeline
/// registry construct their own <see cref="HeraldHost"/> and consume
/// <c>host.Pipelines</c> directly. Two hosts have independent maps —
/// registering a pipeline on one is invisible to the other. The
/// process-wide static facade still backs the
/// <see cref="HeraldHost.Default"/> instance so every caller compiles
/// unchanged.
/// </para>
///
/// <para>
/// Thread-safe. Reads go through the inner concurrent dictionary
/// without touching the upsert lock. Register / TryRegister / Remove
/// serialise on a single per-instance lock so the
/// "remove prior + publish new" sequence stays atomic. Registration is
/// a startup-class activity, so the lock has no measurable cost.
/// </para>
/// </summary>
public sealed class HeraldRegistryInstance
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, HeraldRegistration>> _byTenant =
        new(StringComparer.OrdinalIgnoreCase);

    // Per-instance — two HeraldHost objects do not share this lock.
    // Process-wide locking would re-create the cross-host coupling the
    // host-instance refactor was meant to remove.
    private readonly object _registerLock = new();

    private static readonly TimeSpan PriorDisposeTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Subscribe with <c>+=</c> to observe failures encountered while
    /// disposing a registration evicted by <c>Register</c> when an entry
    /// already existed at <c>(tenant, name)</c>. Handler receives
    /// <c>(tenant, name, exception)</c>. Default no subscribers — failures
    /// are silent to preserve the previous "fire-and-forget dispose"
    /// surface.
    /// </summary>
    public event Action<string, string, Exception>? OnPriorDisposalFailed;

    /// <summary>
    /// Subscribe with <c>+=</c> to observe every successful pipeline
    /// registration on this instance. Handler receives <c>(tenant, name)</c>
    /// where <c>tenant</c> is the normalised (lowercase) tenant id.
    /// Fires after the dictionary publish so a subscriber that looks the
    /// registration up will find it.
    ///
    /// <para>
    /// Architectural seam. Herald.OSS does not subscribe. A downstream
    /// host that needs an audit trail of registrations, a license counter,
    /// or a tenant-creation observer subscribes here without modifying
    /// OSS source. <c>Register</c> fires the event on every call (upsert
    /// included); <c>TryRegister</c> fires only when <c>TryAdd</c> succeeds
    /// (a name-collision failure does NOT fire).
    /// </para>
    ///
    /// <para>
    /// A throwing subscriber propagates out of <c>Register</c> /
    /// <c>TryRegister</c> after the publish — the dictionary state is
    /// consistent but the caller sees the exception. Subscribers that
    /// cannot afford to break registration must wrap their handler body
    /// in try/catch.
    /// </para>
    /// </summary>
    public event Action<string, string>? OnTenantRegistration;

    private ConcurrentDictionary<string, HeraldRegistration> GetOrAddTenantMap(string tenant) =>
        _byTenant.GetOrAdd(tenant, _ => new ConcurrentDictionary<string, HeraldRegistration>(StringComparer.OrdinalIgnoreCase));

    // Single-arg Register overloads resolve through HeraldTenantScope.Current
    // so registration is symmetric with lookup. Outside a scope, Current
    // returns HeraldTenant.Default, so single-tenant callers behave unchanged.
    // Code running inside Push("studio-a") that calls Register(name, ...)
    // lands in studio-a's bucket, exactly where a scoped Get would look for it.
    public void Register(QuickLogBuilder builder, QuickLogResult result, string? configPath = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var name = builder.RegistryName
            ?? throw new InvalidOperationException("Builder has no registry name. Use QuickLogBuilder.Create(\"name\") to set one.");
        Register(HeraldTenantScope.Current, name, builder, result, configPath);
    }

    public void Register(string name, QuickLogBuilder builder, QuickLogResult result, string? configPath = null) =>
        Register(HeraldTenantScope.Current, name, builder, result, configPath);

    public void Register(string tenant, string name, QuickLogBuilder builder, QuickLogResult result, string? configPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(result);

        var normalized = HeraldTenant.Normalize(tenant);

        // Admission policy hook. OSS default is a no-op; a commercial wrapper
        // replaces TenantAdmissionPolicy at startup with a throwing policy
        // when the build is not licensed for non-default tenants. Throwing
        // here aborts the registration before the dictionary publish.
        HeraldTenant.TenantAdmissionPolicy(normalized);

        var newEntry = new HeraldRegistration(name, builder, result, configPath);
        var map = GetOrAddTenantMap(normalized);

        HeraldRegistration? prior;
        lock (_registerLock)
        {
            map.TryRemove(name, out prior);
            map[name] = newEntry;
        }

        // Notify observers after publish so a subscriber looking the
        // registration up immediately finds the new entry. Fires for
        // every successful Register call including upserts.
        OnTenantRegistration?.Invoke(normalized, name);

        DisposePriorEntry(normalized, name, prior);
    }

    public bool TryRegister(string name, QuickLogBuilder builder, QuickLogResult result, string? configPath = null) =>
        TryRegister(HeraldTenantScope.Current, name, builder, result, configPath);

    public bool TryRegister(string tenant, string name, QuickLogBuilder builder, QuickLogResult result, string? configPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(result);

        var normalized = HeraldTenant.Normalize(tenant);

        // Admission policy fires before any publish work. A throwing policy
        // propagates out of TryRegister; the bool return is for name-collision
        // detection only, not for authorisation rejection.
        HeraldTenant.TenantAdmissionPolicy(normalized);

        var newEntry = new HeraldRegistration(name, builder, result, configPath);
        var map = GetOrAddTenantMap(normalized);

        if (!map.TryAdd(name, newEntry)) return false;

        // Only fire when TryAdd actually publishes — a name-collision
        // failure is observable through the false return value, not
        // through OnTenantRegistration.
        OnTenantRegistration?.Invoke(normalized, name);
        return true;
    }

    // Single-arg lookup overloads resolve through HeraldTenantScope.Current so
    // a scoped caller cannot accidentally read the default tenant's bucket
    // when they meant to read their own. Outside a scope, Current returns
    // HeraldTenant.Default, so single-tenant callers behave unchanged.
    // This is the leak-prevention contract: a scoped Get either returns the
    // scoped tenant's registration or null — it never silently falls through
    // to a different tenant.
    public HeraldRegistration? Get(string name) => Get(HeraldTenantScope.Current, name);

    public HeraldRegistration? Get(string tenant, string name)
    {
        var normalized = HeraldTenant.Normalize(tenant);
        return _byTenant.TryGetValue(normalized, out var map) && map.TryGetValue(name, out var entry)
            ? entry
            : null;
    }

    public HeraldRegistration Require(string name) => Require(HeraldTenantScope.Current, name);

    public HeraldRegistration Require(string tenant, string name) =>
        Get(tenant, name) ?? throw new InvalidOperationException(
            $"Herald pipeline '{name}' is not registered under tenant '{tenant}'. Available: {string.Join(", ", GetNames(tenant))}");

    public bool Contains(string name) => Contains(HeraldTenantScope.Current, name);

    public bool Contains(string tenant, string name)
    {
        var normalized = HeraldTenant.Normalize(tenant);
        return _byTenant.TryGetValue(normalized, out var map) && map.ContainsKey(name);
    }

    public IReadOnlyList<string> GetNames() => GetNames(HeraldTenantScope.Current);

    public IReadOnlyList<string> GetNames(string tenant)
    {
        var normalized = HeraldTenant.Normalize(tenant);
        return _byTenant.TryGetValue(normalized, out var map) ? map.Keys.ToList() : Array.Empty<string>();
    }

    public IReadOnlyList<HeraldRegistration> GetAll() => GetAll(HeraldTenantScope.Current);

    public IReadOnlyList<HeraldRegistration> GetAll(string tenant)
    {
        var normalized = HeraldTenant.Normalize(tenant);
        return _byTenant.TryGetValue(normalized, out var map) ? map.Values.ToList() : Array.Empty<HeraldRegistration>();
    }

    public IReadOnlyList<string> GetTenants() =>
        _byTenant.Where(kv => !kv.Value.IsEmpty).Select(kv => kv.Key).ToList();

    /// <summary>
    /// Total registrations across every tenant. Use
    /// <see cref="GetAll()"/> for the default-tenant count, or
    /// <c>GetAll(tenant).Count</c> for a specific tenant.
    /// </summary>
    public int Count
    {
        get
        {
            var total = 0;
            foreach (var map in _byTenant.Values)
            {
                total += map.Count;
            }
            return total;
        }
    }

    public Task<bool> RemoveAsync(string name) => RemoveAsync(HeraldTenantScope.Current, name);

    public async Task<bool> RemoveAsync(string tenant, string name)
    {
        var normalized = HeraldTenant.Normalize(tenant);
        if (!_byTenant.TryGetValue(normalized, out var map)) return false;
        if (!map.TryRemove(name, out var entry)) return false;

        await entry.DisposeAsync().ConfigureAwait(false);
        return true;
    }

    public bool Remove(string name) => Remove(HeraldTenantScope.Current, name);

    public bool Remove(string tenant, string name)
    {
        var normalized = HeraldTenant.Normalize(tenant);
        if (!_byTenant.TryGetValue(normalized, out var map)) return false;
        if (!map.TryRemove(name, out var entry)) return false;

        entry.DisposeAsync().AsTask().GetAwaiter().GetResult();
        return true;
    }

    public async Task ClearAsync()
    {
        foreach (var tenant in _byTenant.Keys.ToList())
        {
            var names = GetNames(tenant);
            foreach (var name in names)
                await RemoveAsync(tenant, name).ConfigureAwait(false);
        }
    }

    // Background drain of an evicted registration. Failures route through
    // OnPriorDisposalFailed (no subscribers by default — silent) so a host
    // that wants visibility can wire a handler. Mirrors the shape of
    // HotReloadableLoggingBootstrap.OldResourceJanitor — matching patterns
    // means an operator who learns one learns both.
    private void DisposePriorEntry(string tenant, string name, HeraldRegistration? prior)
    {
        if (prior is null) return;
        Task.Run(async () =>
        {
            try
            {
                var disposeTask = prior.DisposeAsync().AsTask();
                var completed = await Task.WhenAny(disposeTask, Task.Delay(PriorDisposeTimeout))
                    .ConfigureAwait(false);

                if (completed != disposeTask)
                {
                    OnPriorDisposalFailed?.Invoke(tenant, name,
                        new TimeoutException(
                            $"Prior HeraldRegistration DisposeAsync did not complete within {PriorDisposeTimeout.TotalSeconds} seconds; abandoning."));
                    return;
                }

                // Surface any captured exception.
                await disposeTask.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                OnPriorDisposalFailed?.Invoke(tenant, name, ex);
            }
        });
    }
}

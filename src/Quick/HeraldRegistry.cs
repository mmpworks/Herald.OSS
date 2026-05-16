#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MMP.Herald.Addons.ManagementApi;

namespace MMP.Herald.Quick;

/// <summary>
/// Global registry of named Herald pipeline instances.
///
/// Use this when your application has multiple pipelines (e.g., a game server
/// that slots in a combat logger, an economy logger, and a network logger
/// independently). Each pipeline can be created, retrieved, and removed by name.
///
/// <para>
/// <b>Hosting model.</b> The static surface forwards every call to
/// <see cref="HeraldHost.Default"/>'s <see cref="HeraldRegistryInstance"/>.
/// Tests and multi-tenant hosts that want isolation construct their own
/// <see cref="HeraldHost"/> and consume <c>host.Pipelines</c> directly.
/// Existing single-host callers keep working unchanged through this
/// facade.
/// </para>
///
/// <para>
/// Multi-tenancy is modeled as the default shape: every entry belongs to a
/// tenant, and single-tenant callers fall through to
/// <see cref="HeraldTenant.Default"/>. The back-compat overloads (no tenant
/// argument) forward to the default tenant so existing code keeps working.
/// Enterprise builds unlock additional tenants; other editions throw on
/// non-default registration.
/// </para>
///
/// Thread-safe. All operations are safe to call from any thread.
///
/// Usage:
///   // Single-tenant (default tenant, unchanged behaviour)
///   var builder = QuickLogBuilder.Create("combat")
///       .WithFileSink("logs/combat.log");
///   var result = builder.Build();
///   HeraldRegistry.Register(builder, result);
///
///   // Retrieve from anywhere
///   var combat = HeraldRegistry.Get("combat");
///
///   // Multi-tenant (Enterprise)
///   HeraldRegistry.Register("studio-a", builderA, resultA);
///   HeraldRegistry.Register("studio-b", builderB, resultB);
///   var fromStudioA = HeraldRegistry.Get("studio-a", "combat");
/// </summary>
public static class HeraldRegistry
{
    /// <summary>
    /// Subscribe with <c>+=</c> to observe failures encountered while
    /// disposing a registration evicted by
    /// <see cref="Register(string, string, QuickLogBuilder, QuickLogResult, string?)"/>
    /// when an entry already existed at <c>(tenant, name)</c>. Handler
    /// receives <c>(tenant, name, exception)</c>. Default no subscribers —
    /// failures are silent to preserve the previous "fire-and-forget
    /// dispose" surface. Hosts that want visibility (typically Server)
    /// add a handler at startup; tests that need isolation
    /// <c>+=</c> in setup and <c>-=</c> in teardown.
    ///
    /// <para>
    /// Modeled as a <c>static event</c> so two parallel writers cannot
    /// stomp each other through a property setter — every writer must
    /// explicitly subscribe and explicitly unsubscribe. The CLR's
    /// generated add/remove accessors use Interlocked.CompareExchange
    /// internally so concurrent subscriptions are safe.
    /// </para>
    ///
    /// <para>
    /// Forwards to <c>HeraldHost.Default.Pipelines.OnPriorDisposalFailed</c>
    /// — a custom-host scenario subscribes to its own host's event
    /// directly and is unaffected by the default-host event.
    /// </para>
    /// </summary>
    public static event Action<string, string, Exception> OnPriorDisposalFailed
    {
        add => HeraldHost.Default.Pipelines.OnPriorDisposalFailed += value;
        remove => HeraldHost.Default.Pipelines.OnPriorDisposalFailed -= value;
    }

    /// <summary>
    /// Subscribe with <c>+=</c> to observe every successful pipeline
    /// registration on the default host. Handler receives
    /// <c>(tenant, name)</c> where <c>tenant</c> is the normalised
    /// (lowercase) tenant id. See
    /// <see cref="HeraldRegistryInstance.OnTenantRegistration"/> for the
    /// full contract — this property is a forwarder to
    /// <see cref="HeraldHost.Default"/>'s registry. A custom-host scenario
    /// subscribes to its own host's event directly.
    /// </summary>
    public static event Action<string, string> OnTenantRegistration
    {
        add => HeraldHost.Default.Pipelines.OnTenantRegistration += value;
        remove => HeraldHost.Default.Pipelines.OnTenantRegistration -= value;
    }

    /// <summary>
    /// Subscribe with <c>+=</c> to observe a <c>Get</c> / <c>Require</c>
    /// miss on the default host. Handler receives
    /// <c>(tenant, name)</c>. See
    /// <see cref="HeraldRegistryInstance.OnTenantLookupMissed"/> for the
    /// full contract.
    /// </summary>
    public static event Action<string, string> OnTenantLookupMissed
    {
        add => HeraldHost.Default.Pipelines.OnTenantLookupMissed += value;
        remove => HeraldHost.Default.Pipelines.OnTenantLookupMissed -= value;
    }

    #region Back-compat (default tenant)

    /// <summary>
    /// Register a pipeline using the name from QuickLogBuilder.Create("name"),
    /// into the default tenant.
    /// </summary>
    public static void Register(QuickLogBuilder builder, QuickLogResult result, string? configPath = null) =>
        HeraldHost.Default.Pipelines.Register(builder, result, configPath);

    /// <summary>
    /// Register a pipeline with an explicit name in the default tenant.
    /// </summary>
    public static void Register(string name, QuickLogBuilder builder, QuickLogResult result, string? configPath = null) =>
        HeraldHost.Default.Pipelines.Register(name, builder, result, configPath);

    /// <summary>Get a pipeline by name from the default tenant.</summary>
    public static HeraldRegistration? Get(string name) => HeraldHost.Default.Pipelines.Get(name);

    /// <summary>Get a pipeline by name from the default tenant, throwing when missing.</summary>
    public static HeraldRegistration Require(string name) => HeraldHost.Default.Pipelines.Require(name);

    /// <summary>Check whether the default tenant contains a pipeline with the given name.</summary>
    public static bool Contains(string name) => HeraldHost.Default.Pipelines.Contains(name);

    /// <summary>Return all pipeline names in the default tenant.</summary>
    public static IReadOnlyList<string> GetNames() => HeraldHost.Default.Pipelines.GetNames();

    /// <summary>Return all pipelines in the default tenant.</summary>
    public static IReadOnlyList<HeraldRegistration> GetAll() => HeraldHost.Default.Pipelines.GetAll();

    /// <summary>
    /// Total registrations across every tenant. Use <see cref="GetAll()"/>
    /// for the default-tenant count, or <c>GetAll(tenant).Count</c> for a
    /// specific tenant.
    /// </summary>
    public static int Count => HeraldHost.Default.Pipelines.Count;

    /// <summary>Remove (and dispose) a pipeline from the default tenant.</summary>
    public static Task<bool> RemoveAsync(string name) => HeraldHost.Default.Pipelines.RemoveAsync(name);

    /// <summary>Remove (and dispose) a pipeline from the default tenant synchronously.</summary>
    public static bool Remove(string name) => HeraldHost.Default.Pipelines.Remove(name);

    #endregion

    #region Tenant-aware API

    /// <summary>
    /// Register a pipeline with an explicit name in the given tenant.
    /// The tenant-aware variant always requires an explicit <paramref name="name"/>
    /// because a two-arg form would collide with the back-compat
    /// <c>Register(string name, ...)</c> overload that targets the default
    /// tenant.
    ///
    /// <para>
    /// Upsert semantics. When a registration already exists at
    /// <c>(tenant, name)</c> the prior <see cref="HeraldRegistration"/> is
    /// disposed before the new one publishes — closing a previously silent
    /// leak where double-registration left the old pipeline's sinks, async
    /// queue, and WAL handle owned by no one. The dispose is best-effort:
    /// a stuck shutdown does not fail the new registration.
    /// </para>
    /// </summary>
    public static void Register(string tenant, string name, QuickLogBuilder builder, QuickLogResult result, string? configPath = null) =>
        HeraldHost.Default.Pipelines.Register(tenant, name, builder, result, configPath);

    /// <summary>
    /// Returns true and registers when no entry exists at <c>(tenant, name)</c>,
    /// returns false otherwise without disposing or replacing the existing
    /// entry. Useful for callers that want to detect a name collision without
    /// the upsert behaviour of <see cref="Register(string, string, QuickLogBuilder, QuickLogResult, string?)"/>.
    /// </summary>
    public static bool TryRegister(string tenant, string name, QuickLogBuilder builder, QuickLogResult result, string? configPath = null) =>
        HeraldHost.Default.Pipelines.TryRegister(tenant, name, builder, result, configPath);

    /// <summary>
    /// Try to register in the default tenant. Returns true on success, false
    /// when a registration already exists. See
    /// <see cref="TryRegister(string, string, QuickLogBuilder, QuickLogResult, string?)"/>.
    /// </summary>
    public static bool TryRegister(string name, QuickLogBuilder builder, QuickLogResult result, string? configPath = null) =>
        HeraldHost.Default.Pipelines.TryRegister(name, builder, result, configPath);

    /// <summary>Get a registered pipeline by tenant and name. Returns null if not found.</summary>
    public static HeraldRegistration? Get(string tenant, string name) => HeraldHost.Default.Pipelines.Get(tenant, name);

    /// <summary>Get a registered pipeline by tenant and name. Throws if not found.</summary>
    public static HeraldRegistration Require(string tenant, string name) => HeraldHost.Default.Pipelines.Require(tenant, name);

    /// <summary>Check whether the tenant contains a pipeline with the given name.</summary>
    public static bool Contains(string tenant, string name) => HeraldHost.Default.Pipelines.Contains(tenant, name);

    /// <summary>Get all pipeline names in the given tenant.</summary>
    public static IReadOnlyList<string> GetNames(string tenant) => HeraldHost.Default.Pipelines.GetNames(tenant);

    /// <summary>Get all pipelines in the given tenant.</summary>
    public static IReadOnlyList<HeraldRegistration> GetAll(string tenant) => HeraldHost.Default.Pipelines.GetAll(tenant);

    /// <summary>
    /// Return the names of all tenants that have at least one registration.
    /// Useful for admin tooling under Enterprise.
    /// </summary>
    public static IReadOnlyList<string> GetTenants() => HeraldHost.Default.Pipelines.GetTenants();

    /// <summary>
    /// Remove (and dispose) a pipeline from the given tenant.
    /// </summary>
    public static Task<bool> RemoveAsync(string tenant, string name) => HeraldHost.Default.Pipelines.RemoveAsync(tenant, name);

    /// <summary>
    /// Synchronous counterpart to <see cref="RemoveAsync(string, string)"/>.
    /// </summary>
    public static bool Remove(string tenant, string name) => HeraldHost.Default.Pipelines.Remove(tenant, name);

    /// <summary>
    /// Remove all registered pipelines across every tenant and dispose their resources.
    /// </summary>
    public static Task ClearAsync() => HeraldHost.Default.Pipelines.ClearAsync();

    #endregion
}

/// <summary>
/// A named Herald pipeline registration.
/// Holds the builder (for reconfiguration) and the result (for logging).
/// </summary>
public sealed class HeraldRegistration : IAsyncDisposable
{
    public HeraldRegistration(string name, QuickLogBuilder builder, QuickLogResult result, string? configPath = null)
    {
        Name = name;
        Builder = builder;
        Result = result;
        ConfigPath = configPath;
    }

    /// <summary>The registered name (e.g. "combat", "network", "economy").</summary>
    public string Name { get; }

    /// <summary>The builder, retained for runtime reconfiguration via RebuildFrom().</summary>
    public QuickLogBuilder Builder { get; }

    /// <summary>
    /// The built pipeline result (logger, level registry, pipeline accessor).
    /// Settable for rebuild-with-downtime scenarios where the entire result
    /// is replaced (old pipeline orphaned, new pipeline takes over).
    /// </summary>
    public QuickLogResult Result { get; internal set; }

    /// <summary>
    /// Path to the JSON config file for persistent pipelines.
    /// When set, the pipeline's config is saved to this file on commit.
    /// Null for non-persistent (in-memory only) pipelines.
    /// </summary>
    public string? ConfigPath { get; }

    private HeraldManagementApi? _api;

    /// <summary>
    /// Cached management API for this registration. Shares transaction state
    /// across all callers. Created lazily on first access.
    ///
    /// <para>
    /// Two concurrent first-access readers would each run <c>??=</c> and
    /// each publish their own <see cref="HeraldManagementApi"/>; whoever
    /// wrote last would leave the other's instance stranded with any
    /// in-flight transaction state. The <see cref="Interlocked.CompareExchange"/>
    /// loses one copy to the GC but guarantees every caller from that
    /// point forward sees the same instance. <see cref="Volatile.Read"/>
    /// pairs with the matching write in the CAS so readers on a second
    /// thread cannot observe a torn or stale reference.
    /// </para>
    ///
    /// <para>
    /// CAS instead of Volatile-only here because the API instance carries
    /// active transaction state — letting two clients use different
    /// instances would split that state across copies. Compare with
    /// <see cref="MMP.Herald.Events.LogEvent"/>'s lazy property index,
    /// which uses Volatile-only because the index is functionally pure
    /// (two racing builds produce equal dictionaries; the loser is
    /// harmlessly GC'd). Same engine, different invariants, different
    /// primitive — both are deliberate.
    /// </para>
    /// </summary>
    public HeraldManagementApi Api
    {
        get
        {
            var existing = Volatile.Read(ref _api);
            if (existing is not null) return existing;

            var created = HeraldManagementApi.FromRegistration(this);
            // Publish only if no other thread beat us to it. The returned
            // value is whatever was there *before* the CAS — when the field
            // was already populated, that's the winning instance.
            var prior = Interlocked.CompareExchange(ref _api, created, null);
            return prior ?? created;
        }
    }

    /// <summary>Force the cached API to be recreated on next access (e.g. after Result swap).</summary>
    internal void InvalidateApi() => Volatile.Write(ref _api, null);

    // Test-only hook so the dispose-prior failure path can be exercised
    // without making QuickLogResult non-sealed or threading a synthetic
    // IAsyncDisposable through the production API. Production callers
    // never set this; the field stays null.
    internal IAsyncDisposable? DisposeProbeForTests { get; set; }

    public async ValueTask DisposeAsync()
    {
        // Probe runs first. A throwing probe propagates and the production
        // Result.DisposeAsync never runs — exactly the shape S4 needs to
        // route through OnPriorDisposalFailed.
        if (DisposeProbeForTests is { } probe)
        {
            await probe.DisposeAsync().ConfigureAwait(false);
        }
        await Result.DisposeAsync().ConfigureAwait(false);
    }
}

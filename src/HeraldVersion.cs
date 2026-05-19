#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Reflection;
using System.Threading;

namespace MMP.Herald;

/// <summary>
/// Surface version + edition self-report for the running Herald process.
/// <para>
/// Edition defaults to <see cref="HeraldEdition.Community"/>. Paid Herald
/// modules call <see cref="SetEdition(HeraldEdition)"/> from their
/// initializer to advertise that they are present. The OSS kernel never
/// gates behaviour on this value; consumers who need to know whether a
/// feature is available should check the feature directly.
/// </para>
///
/// <para>
/// Rev 2 capability-composition (Stage B): <see cref="CurrentCapabilities"/>
/// adds the capability-set parallel to <see cref="CurrentEdition"/>. The
/// licensing engine writes both after a successful token verify; the new
/// <see cref="HeraldCapabilityGate"/> reads via
/// <see cref="CapabilityResolver"/> so a multi-tenant host can swap the
/// resolver for a per-tenant claim store without touching the gate.
/// </para>
/// </summary>
public static class HeraldVersion
{
    private static readonly Assembly HeraldAssembly = typeof(HeraldVersion).Assembly;

    private static HeraldEdition _currentEdition = HeraldEdition.Community;
    private static IReadOnlySet<string> _currentCapabilities = ImmutableHashSet<string>.Empty;
    private static Func<string?, IReadOnlySet<string>> _capabilityResolver = DefaultCapabilityResolver;

    /// <summary>
    /// Reports which edition the running Herald process is operating as.
    /// Defaults to <see cref="HeraldEdition.Community"/>; paid module
    /// initializers may call <see cref="SetEdition(HeraldEdition)"/> to
    /// advertise their presence.
    /// </summary>
    public static HeraldEdition CurrentEdition => _currentEdition;

    /// <summary>
    /// Convenience getter; equivalent to <see cref="CurrentEdition"/>'s
    /// <see cref="HeraldEdition.Name"/>. Provided for legacy display-string
    /// callers (Server console banner, Lean process banner). Prefer
    /// <see cref="CurrentEdition"/> for new code so the typed identity
    /// surface is visible at the call site.
    /// </summary>
    public static string Edition => _currentEdition.Name;

    /// <summary>
    /// Install hook for paid Herald modules to advertise their tier. First
    /// call wins; subsequent calls are no-ops (NOT exceptions — keeps tests
    /// deterministic under parallel fixture loading).
    /// </summary>
    /// <remarks>
    /// Intended for use by Herald paid-module initializers; calling this
    /// from application code is unsupported.
    /// </remarks>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public static void SetEdition(HeraldEdition edition)
    {
        if (edition is null) throw new ArgumentNullException(nameof(edition));
        Interlocked.CompareExchange(ref _currentEdition, edition, HeraldEdition.Community);
    }

    /// <summary>
    /// Process-global effective capability set the running license advertises.
    /// Empty by default (Community deployments — no token, no caps); the
    /// licensing engine writes this after a successful token verify via
    /// <see cref="SetCurrentCapabilities(IReadOnlySet{string})"/>.
    ///
    /// <para>
    /// <b>Read path.</b> Consumers should read through
    /// <see cref="CapabilityResolver"/> (or
    /// <see cref="HeraldCapabilityGate.Require"/> /
    /// <see cref="HeraldCapabilityGate.RequireFor"/>) rather than this
    /// property directly. Direct reads bypass the per-tenant resolver hook
    /// that a multi-tenant host installs.
    /// </para>
    /// </summary>
    public static IReadOnlySet<string> CurrentCapabilities => _currentCapabilities;

    /// <summary>
    /// Replaceable per-tenant capability resolver — Rosanne S-1 seam.
    /// Defaults to <see cref="DefaultCapabilityResolver"/> which ignores
    /// the tenant id and returns <see cref="CurrentCapabilities"/>. A
    /// multi-tenant host (commercial wrapper) replaces this delegate at
    /// startup with a tenant-store lookup; the gate primitives consult
    /// the delegate transparently.
    ///
    /// <para>
    /// Reference-type assignment is atomic in C#; concurrent reads during
    /// a swap see either the old delegate or the new one — never a torn
    /// reference. The static-property shape mirrors
    /// <see cref="Quick.HeraldTenant.TenantAdmissionPolicy"/> (the
    /// established B-1 precedent).
    /// </para>
    ///
    /// <para>
    /// <b>No subtraction (ADR-210).</b> The resolver may RETURN a smaller
    /// per-tenant set, but the returned set must be sourced from the
    /// customer's contract — not synthesised by subtracting from the
    /// process-global set. The "Enterprise minus X" use case is served by
    /// the additive Pro+caps pattern, not by a subtractive resolver.
    /// </para>
    /// </summary>
    public static Func<string?, IReadOnlySet<string>> CapabilityResolver
    {
        get => _capabilityResolver;
        set => _capabilityResolver = value ?? DefaultCapabilityResolver;
    }

    /// <summary>
    /// Install hook for the licensing engine to report the current
    /// effective capability set. First call wins per
    /// CompareExchange-against-default semantics (matches
    /// <see cref="SetEdition(HeraldEdition)"/>); the engine re-publishes
    /// via <see cref="ReplaceCurrentCapabilities(IReadOnlySet{string})"/>
    /// when the cap-set changes (renewal, PATCH, expiry-state change).
    /// </summary>
    /// <remarks>
    /// Intended for use by the licensing engine; calling this from
    /// application code is unsupported.
    /// </remarks>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public static void SetCurrentCapabilities(IReadOnlySet<string> capabilities)
    {
        if (capabilities is null) throw new ArgumentNullException(nameof(capabilities));
        Interlocked.CompareExchange(ref _currentCapabilities, capabilities, ImmutableHashSet<string>.Empty);
    }

    /// <summary>
    /// Engine-side replacement hook used when the effective cap-set
    /// changes mid-run (renewal, PATCH amendment, expiry-state transition,
    /// downgrade). Unconditional swap — the engine is the source of truth.
    /// Lifecycle events (G-5) ride this path.
    /// </summary>
    /// <remarks>
    /// Intended for use by the licensing engine; calling this from
    /// application code is unsupported.
    /// </remarks>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public static void ReplaceCurrentCapabilities(IReadOnlySet<string> capabilities)
    {
        if (capabilities is null) throw new ArgumentNullException(nameof(capabilities));
        Interlocked.Exchange(ref _currentCapabilities, capabilities);
    }

    private static IReadOnlySet<string> DefaultCapabilityResolver(string? tenantId)
        => _currentCapabilities;

    internal static void ResetForTesting()
    {
        Interlocked.Exchange(ref _currentEdition, HeraldEdition.Community);
        Interlocked.Exchange(ref _currentCapabilities, ImmutableHashSet<string>.Empty);
        _capabilityResolver = DefaultCapabilityResolver;
    }

    /// <summary>SemVer version string (e.g. "0.1.0").</summary>
    public static string Version { get; } = ReadVersion();

    /// <summary>True when the Herald assembly was built in Debug configuration.</summary>
    public static bool IsDebug { get; } = DetectDebug();

    /// <summary>"Debug" or "Release".</summary>
    public static string BuildConfiguration => IsDebug ? "Debug" : "Release";

    /// <summary>Combined display string, e.g. "0.1.0 (Release)".</summary>
    public static string FullVersion => $"{Version} ({BuildConfiguration})";

    private static string ReadVersion()
    {
        var informational = HeraldAssembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        if (!string.IsNullOrEmpty(informational))
        {
            var plusIndex = informational.IndexOf('+');
            return plusIndex >= 0 ? informational[..plusIndex] : informational;
        }

        var assemblyVersion = HeraldAssembly.GetName().Version;
        return assemblyVersion is not null
            ? $"{assemblyVersion.Major}.{assemblyVersion.Minor}.{assemblyVersion.Build}"
            : "0.0.0";
    }

    private static bool DetectDebug()
    {
        var debuggable = HeraldAssembly.GetCustomAttribute<System.Diagnostics.DebuggableAttribute>();
        return debuggable?.IsJITOptimizerDisabled ?? false;
    }
}

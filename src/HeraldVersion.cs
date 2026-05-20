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
/// </summary>
public static class HeraldVersion
{
    private static readonly Assembly HeraldAssembly = typeof(HeraldVersion).Assembly;

    private static HeraldEdition _currentEdition = HeraldEdition.Community;

    // Process-global capability set. Empty by default — Community deployments
    // (no token) see EmptyCaps and every cap check fails closed. A paid
    // verifier writes the effective set via SetCurrentCapabilities once at
    // boot; the license state machine may later swap the set via
    // ReplaceCurrentCapabilities when contract amendments land.
    private static readonly ImmutableHashSet<string> EmptyCaps =
        ImmutableHashSet<string>.Empty.WithComparer(StringComparer.Ordinal);

    private static IReadOnlySet<string> _currentCapabilities = EmptyCaps;

    // Per-tenant resolver. Defaults to "(_) => CurrentCapabilities" so
    // single-tenant deployments behave identically to a v1 plan that had
    // no tenant parameter. A multi-tenant host swaps in a delegate that
    // consults its per-tenant claim store.
    private static Func<string?, IReadOnlySet<string>> _capabilityResolver = DefaultResolver;

    private static IReadOnlySet<string> DefaultResolver(string? _) => _currentCapabilities;

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
    /// The capabilities the running process currently has access to. Empty
    /// by default; Community deployments stay empty and every capability
    /// gate fails closed. A paid verifier seeds this set via
    /// <see cref="SetCurrentCapabilities(IReadOnlySet{string})"/> after a
    /// successful token verify. Returns a snapshot reference safe to
    /// enumerate without re-reading the field.
    /// </summary>
    public static IReadOnlySet<string> CurrentCapabilities => _currentCapabilities;

    /// <summary>
    /// Per-tenant capability resolver. The gate calls this with the tenant
    /// id supplied to <c>RequireFor</c> / <c>AllowsFeatureFor</c>; a
    /// <see langword="null"/> tenant id returns the process-global cap-set
    /// (the documented default). Multi-tenant hosts assign a delegate that
    /// consults a per-tenant claim store. Replaceable at runtime; a
    /// <see langword="null"/> assignment restores the default resolver.
    /// </summary>
    public static Func<string?, IReadOnlySet<string>> CapabilityResolver
    {
        get => _capabilityResolver;
        set => _capabilityResolver = value ?? DefaultResolver;
    }

    /// <summary>
    /// Install hook for the paid verifier to advertise the process's
    /// effective capability set. First-write-wins via
    /// <see cref="Interlocked.CompareExchange{T}(ref T, T, T)"/>:
    /// subsequent calls are no-ops (matches <see cref="SetEdition"/>
    /// semantics — keeps tests deterministic under parallel fixture
    /// loading). For replace-rather-than-install semantics use
    /// <see cref="ReplaceCurrentCapabilities(IReadOnlySet{string})"/>.
    /// </summary>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public static void SetCurrentCapabilities(IReadOnlySet<string> capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        Interlocked.CompareExchange(ref _currentCapabilities, capabilities, EmptyCaps);
    }

    /// <summary>
    /// Replaces the current capability set unconditionally. The licensing
    /// state machine calls this when a check-in response narrows or widens
    /// the cap-set (G-5 hook): post-boot cap-set mutation is legitimate
    /// because the SSoT contract says the license server is authoritative
    /// for the current effective caps. Distinct from
    /// <see cref="SetCurrentCapabilities(IReadOnlySet{string})"/> so the
    /// first-write-wins install hook stays unconditional and the
    /// replace-on-change path is explicit at the call site.
    /// </summary>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public static void ReplaceCurrentCapabilities(IReadOnlySet<string> capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        Interlocked.Exchange(ref _currentCapabilities, capabilities);
    }

    internal static void ResetForTesting()
    {
        Interlocked.Exchange(ref _currentEdition, HeraldEdition.Community);
        Interlocked.Exchange(ref _currentCapabilities, EmptyCaps);
        _capabilityResolver = DefaultResolver;
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

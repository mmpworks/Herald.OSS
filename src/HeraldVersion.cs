#nullable enable

using System;
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

    internal static void ResetForTesting()
    {
        Interlocked.Exchange(ref _currentEdition, HeraldEdition.Community);
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

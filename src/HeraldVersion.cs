#nullable enable

using System.Reflection;

namespace MMP.Herald;

/// <summary>
/// Exposes Herald assembly version, build configuration, and edition at runtime.
/// Version follows SemVer (major.minor.patch).
/// Edition is set at compile time via the HeraldEdition MSBuild property.
/// </summary>
public static class HeraldVersion
{
    private static readonly Assembly HeraldAssembly = typeof(HeraldVersion).Assembly;

    /// <summary>SemVer version string (e.g. "0.1.0").</summary>
    public static string Version { get; } = ReadVersion();

    /// <summary>True when the Herald assembly was built in Debug configuration.</summary>
    public static bool IsDebug { get; } = DetectDebug();

    /// <summary>"Debug" or "Release".</summary>
    public static string BuildConfiguration => IsDebug ? "Debug" : "Release";

    /// <summary>The current edition as a typed record with rank for comparison.</summary>
    public static HeraldEdition CurrentEdition { get; } =
#if HERALD_COMMUNITY
        HeraldEdition.Community;
#elif HERALD_PRO
        HeraldEdition.Pro;
#else
        HeraldEdition.Enterprise;
#endif

    /// <summary>"Community", "Pro", or "Enterprise".</summary>
    public static string Edition => CurrentEdition.Name;

    /// <summary>Combined display string, e.g. "0.1.0 Community" or "0.1.0 Pro".</summary>
    public static string FullVersion => $"{Version} {Edition}";

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

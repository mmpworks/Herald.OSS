#nullable enable

using System.Reflection;

namespace MMP.Herald;

/// <summary>
/// Exposes Herald.OSS assembly version and build configuration at runtime.
/// Version follows SemVer (major.minor.patch).
///
/// <para>
/// Herald.OSS is the Apache 2.0 upstream distribution. The legacy edition
/// concept (Community / Pro / Enterprise) lives in Herald.Core downstream;
/// in Herald.OSS there's a single edition by virtue of being the open
/// distribution.
/// </para>
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

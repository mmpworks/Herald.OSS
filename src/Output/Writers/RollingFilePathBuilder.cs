#nullable enable

using System;
using System.Globalization;
using MMP.Herald.Configuration.Runtime;

namespace MMP.Herald.Output.Writers;

/// <summary>
/// Pure path construction for rolling log files.
/// Builds file names from base path + period timestamp + size sequence.
/// Stateless - all methods are static.
/// </summary>
internal static class RollingFilePathBuilder
{
    public static string Build(
        string basePath,
        DateTimeOffset periodStart,
        LoggingRuntimeFileRollingPolicy policy,
        int sequence) =>
        Build(basePath, periodStart, policy.Interval, sequence,
            policy.FileNameSuffix, policy.Locale);

    public static string Build(
        string basePath,
        DateTimeOffset periodStart,
        LogFileRollingInterval interval,
        int sequence,
        string? fileNameSuffix = null,
        string? locale = null) {
        var (directory, nameWithoutExt, extension) = SplitVirtualPath(basePath);

        var culture = ResolveCulture(locale);

        string datePart;
        if (!string.IsNullOrWhiteSpace(fileNameSuffix))
        {
            datePart = periodStart.ToString(fileNameSuffix, culture);
        }
        else
        {
            datePart = interval switch
            {
                _ when interval == LogFileRollingInterval.Hourly =>
                    periodStart.ToString("_yyyy-MM-dd_HH", CultureInfo.InvariantCulture),
                _ when interval == LogFileRollingInterval.Daily =>
                    periodStart.ToString("_yyyy-MM-dd", CultureInfo.InvariantCulture),
                _ when interval == LogFileRollingInterval.Custom =>
                    periodStart.ToString("_yyyy-MM-dd_HH-mm", CultureInfo.InvariantCulture),
                _ => string.Empty
            };
        }

        var sequencePart = sequence > 1 ? $"_{sequence}" : string.Empty;
        var fileName = $"{nameWithoutExt}{datePart}{sequencePart}{extension}";

        return string.IsNullOrEmpty(directory) ? fileName : $"{directory}/{fileName}";
    }

    public static (string Directory, string NameWithoutExt, string Extension) SplitVirtualPath(
        string path) {
        var lastSlash = path.LastIndexOf('/');
        var fileName = lastSlash >= 0 ? path[(lastSlash + 1)..] : path;
        var directory = lastSlash >= 0 ? path[..lastSlash] : string.Empty;

        var dotIndex = fileName.LastIndexOf('.');
        var nameWithoutExt = dotIndex >= 0 ? fileName[..dotIndex] : fileName;
        var extension = dotIndex >= 0 ? fileName[dotIndex..] : string.Empty;

        return (directory, nameWithoutExt, extension);
    }

    private static IFormatProvider ResolveCulture(string? locale) {
        if (string.IsNullOrWhiteSpace(locale)) return CultureInfo.InvariantCulture;

        try { return CultureInfo.GetCultureInfo(locale); }
        catch (CultureNotFoundException) { return CultureInfo.InvariantCulture; }
    }
}

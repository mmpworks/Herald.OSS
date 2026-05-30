#nullable enable

namespace MMP.Herald.Services;

/// <summary>
/// Well-known log level key strings for use with WithMinimumLevel() and other
/// string-based level APIs. These match the Key property of KnownLogLevels.
///
/// Usage:
///   builder.WithMinimumLevel(LogLevelKeys.Debug)
///   builder.WithFileSink("logs/game.log", minLevel: LogLevelKeys.Warning)
/// </summary>
public static class LogLevelKeys
{
    public const string Verbose = "verbose";
    public const string Debug = "debug";
    public const string Information = "information";
    public const string Notice = "notice";
    public const string Success = "success";
    public const string Warning = "warning";
    public const string Error = "error";
    public const string Fatal = "fatal";
    public const string Security = "security";
    public const string Metric = "metric";
}

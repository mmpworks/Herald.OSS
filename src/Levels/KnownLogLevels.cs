#nullable enable

namespace MMP.Herald.Levels;
/// <summary>
/// Common reusable level definitions.
/// These are values only. Their rank is assigned by the registry.
/// </summary>
public static class KnownLogLevels
{
    public static LogLevel Trace { get; } = new("trace", "Trace");
    public static LogLevel Debug { get; } = new("debug", "Debug");
    public static LogLevel Info { get; } = new("info", "Info");
    public static LogLevel Warn { get; } = new("warn", "Warn");
    public static LogLevel Error { get; } = new("error", "Error");

    public static LogLevel Notice { get; } = new("notice", "Notice");
    public static LogLevel Success { get; } = new("success", "Success");
    public static LogLevel Critical { get; } = new("critical", "Critical");
    public static LogLevel Security { get; } = new("security", "Security");
    public static LogLevel Metric { get; } = new("metric", "Metric");
}
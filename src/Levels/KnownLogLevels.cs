#nullable enable

namespace MMP.Herald.Levels;
/// <summary>
/// Common reusable level definitions.
/// These are values only. Their rank is assigned by the registry.
/// </summary>
public static class KnownLogLevels
{
    public static LogLevel Verbose { get; } = new("verbose", "Verbose");
    public static LogLevel Debug { get; } = new("debug", "Debug");
    public static LogLevel Information { get; } = new("information", "Information");
    public static LogLevel Warning { get; } = new("warning", "Warning");
    public static LogLevel Error { get; } = new("error", "Error");

    public static LogLevel Notice { get; } = new("notice", "Notice");
    public static LogLevel Success { get; } = new("success", "Success");
    public static LogLevel Fatal { get; } = new("fatal", "Fatal");
    public static LogLevel Security { get; } = new("security", "Security");
    public static LogLevel Metric { get; } = new("metric", "Metric");
}
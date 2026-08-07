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

    // Short-name aliases (0.13.0, additive). Each returns the SAME LogLevel
    // instance as its Serilog-vocabulary canonical above — identical key,
    // identical wire behavior, identical registry identity. They exist so
    // code written against the short vocabulary (Trace/Info/Warn/Critical)
    // compiles unchanged; they are aliases, not additional levels.

    /// <summary>Alias for <see cref="Verbose"/> (same instance, key "verbose").</summary>
    public static LogLevel Trace => Verbose;

    /// <summary>Alias for <see cref="Information"/> (same instance, key "information").</summary>
    public static LogLevel Info => Information;

    /// <summary>Alias for <see cref="Warning"/> (same instance, key "warning").</summary>
    public static LogLevel Warn => Warning;

    /// <summary>Alias for <see cref="Fatal"/> (same instance, key "fatal").</summary>
    public static LogLevel Critical => Fatal;
}
#nullable enable

namespace MMP.Herald.Configuration.Runtime;

/// <summary>
/// Controls how often a rolling file sink rotates to a new file.
/// Using a record instead of an enum keeps the value open for extension without recompilation.
/// </summary>
public sealed record LogFileRollingInterval(string Value)
{
    public static LogFileRollingInterval None { get; } = new("none");
    public static LogFileRollingInterval Hourly { get; } = new("hourly");
    public static LogFileRollingInterval Daily { get; } = new("daily");

    /// <summary>
    /// Custom interval defined by StartMinute and CaptureDurationMinutes in the rolling policy.
    /// Rolls at fixed wall-clock boundaries aligned to the hour (e.g., every 15 minutes starting
    /// at minute 0 produces windows :00-:15, :15-:30, :30-:45, :45-:00).
    /// </summary>
    public static LogFileRollingInterval Custom { get; } = new("custom");

    public override string ToString() => Value;
}

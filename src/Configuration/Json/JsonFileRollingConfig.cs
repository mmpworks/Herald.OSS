#nullable enable

namespace MMP.Herald.Configuration.Json;

/// <summary>
/// JSON-facing rolling policy for file sinks.
/// Interval controls time-based rolling; MaxBytes triggers size-based rolling within a period.
/// </summary>
/// <param name="Interval">"none", "hourly", "daily", or "custom".</param>
/// <param name="MaxBytes">Size limit per file. A new file is created before the current one would exceed this.</param>
/// <param name="MaxRetainedFiles">Maximum rolled files kept per time-based rotation. Oldest are pruned.</param>
/// <param name="LogQueueSize">Maximum total log files across all periods. Oldest files removed when exceeded.
/// Good for capping total disk usage across sessions.</param>
/// <param name="StartMinute">For "custom" interval: minute within the hour (0-59) where the first window starts.</param>
/// <param name="CaptureDurationMinutes">For "custom" interval: length of each capture window in minutes.</param>
/// <param name="FileNameSuffix">Optional .NET date/time format string for the time-based portion of rolling file names.
/// When provided, replaces the built-in date suffix. The format is applied to the period start time.
/// Examples: "_yyyy_MM_dd", "_yyyyMMdd_HHmm", "_dd-MMM-yyyy".
/// Standard .NET date/time format specifiers are supported (yyyy, MM, dd, HH, mm, ss, etc.).</param>
/// <param name="Locale">Optional locale/culture name for date formatting in file names (e.g., "en-US", "de-DE", "ja-JP").
/// Defaults to invariant culture when null.</param>
/// <param name="RetentionDays">Delete rolled files older than this many days. Null means no age-based pruning.
/// Works alongside MaxRetainedFiles and TotalSizeCapBytes - whichever is most restrictive wins.</param>
/// <param name="TotalSizeCapBytes">Maximum total size (bytes) of all rolled log files combined.
/// When exceeded, oldest files are deleted regardless of MaxRetainedFiles or RetentionDays.
/// Accepts raw byte count. Use helper parsing for human-readable values (e.g., "1GB" -> 1073741824).
/// Null or 0 means no total size cap.</param>
public sealed record JsonFileRollingConfig(
    string Interval = Services.JsonConfigProperties.IntervalDaily,
    long? MaxBytes = null,
    int? MaxRetainedFiles = null,
    int? LogQueueSize = null,
    int StartMinute = 0,
    int CaptureDurationMinutes = 60,
    string? FileNameSuffix = null,
    string? Locale = null,
    int? RetentionDays = null,
    long? TotalSizeCapBytes = null);

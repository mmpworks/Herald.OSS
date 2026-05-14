#nullable enable

namespace MMP.Herald.Configuration.Runtime;

/// <summary>
/// Runtime file rolling policy normalized from transport config.
/// A non-null instance means rolling is enabled for the sink.
/// </summary>
/// <param name="Interval">Time-based rolling interval (None, Hourly, Daily, Custom).</param>
/// <param name="MaxBytes">Size limit per file. A new file is created before the current one exceeds this.</param>
/// <param name="MaxRetainedFiles">Maximum number of rolled files to keep. Oldest are pruned when exceeded.</param>
/// <param name="LogQueueSize">Maximum total log files across all periods. Oldest files are removed when exceeded.
/// Useful as a session-scoped cap (e.g., "keep only the last 20 log files total").</param>
/// <param name="StartMinute">For Custom interval: the minute within the hour where the first window begins (0-59).</param>
/// <param name="CaptureDurationMinutes">For Custom interval: the length of each capture window in minutes.</param>
/// <param name="FileNameSuffix">Optional .NET date/time format string for the time-based portion of rolling file names.
/// When provided, replaces the built-in date suffix. Applied to the period start time using the configured culture.</param>
/// <param name="Locale">Optional locale/culture name for date formatting (e.g., "en-US", "de-DE"). Null uses invariant culture.</param>
/// <param name="RetentionDays">Delete rolled files older than this many days. Null means no age-based pruning.</param>
/// <param name="TotalSizeCapBytes">Maximum total size (bytes) of all rolled log files combined. Null or 0 means no cap.</param>
public sealed record LoggingRuntimeFileRollingPolicy(
    LogFileRollingInterval Interval,
    long? MaxBytes = null,
    int? MaxRetainedFiles = null,
    int? LogQueueSize = null,
    int StartMinute = 0,
    int CaptureDurationMinutes = 60,
    string? FileNameSuffix = null,
    string? Locale = null,
    int? RetentionDays = null,
    long? TotalSizeCapBytes = null);

#nullable enable

using MMP.Herald.Services;

namespace MMP.Herald.Configuration;

/// <summary>
/// Immutable policy for file sink rolling behavior. Fluent builder pattern
/// reduces the 10-parameter WithFileSink() overload to a single object.
///
/// Usage:
///   var policy = FileSinkPolicy.Create()
///       .Daily()
///       .WithMaxBytes(10_000_000)
///       .WithMaxRetainedFiles(30)
///       .WithMinLevel(LogLevelKeys.Warn);
///
///   builder.WithFileSink("logs/game.log", policy);
/// </summary>
public sealed record FileSinkPolicy(
    string Interval = Services.JsonConfigProperties.IntervalNone,
    long? MaxBytes = null,
    int? LogQueueSize = null,
    int? MaxRetainedFiles = null,
    int StartMinute = 0,
    int CaptureDurationMinutes = 60,
    string? FileNameSuffix = null,
    string? Locale = null,
    string? MinLevel = null,
    int? RetentionDays = null,
    long? TotalSizeCapBytes = null)
{
    public static FileSinkPolicy Create() => new();

    // -- Rolling interval presets --

    public FileSinkPolicy Daily() => this with { Interval = JsonConfigProperties.IntervalDaily };
    public FileSinkPolicy Hourly() => this with { Interval = JsonConfigProperties.IntervalHourly };
    public FileSinkPolicy Custom(int startMinute, int durationMinutes) =>
        this with { Interval = JsonConfigProperties.IntervalCustom, StartMinute = startMinute, CaptureDurationMinutes = durationMinutes };
    public FileSinkPolicy NoRolling() => this with { Interval = JsonConfigProperties.IntervalNone };

    // -- Size limits --

    public FileSinkPolicy WithMaxBytes(long maxBytes) => this with { MaxBytes = maxBytes };
    public FileSinkPolicy WithLogQueueSize(int size) => this with { LogQueueSize = size };
    public FileSinkPolicy WithMaxRetainedFiles(int count) => this with { MaxRetainedFiles = count };

    // -- Formatting --

    public FileSinkPolicy WithFileNameSuffix(string suffix) => this with { FileNameSuffix = suffix };
    public FileSinkPolicy WithLocale(string locale) => this with { Locale = locale };

    // -- Retention --

    public FileSinkPolicy WithRetentionDays(int days) => this with { RetentionDays = days };
    public FileSinkPolicy WithTotalSizeCap(long bytes) => this with { TotalSizeCapBytes = bytes };

    // -- Level filter --

    public FileSinkPolicy WithMinLevel(string level) => this with { MinLevel = level };
}

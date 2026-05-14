#nullable enable

using System.Text.Json.Serialization;

namespace MMP.Herald.Configuration;

/// <summary>
/// Strongly-typed configuration shape for the file-based sinks
/// (<c>text_file</c>, <c>json_file</c>, <c>protobuf_file</c>). Field
/// names match the v2 mmpform bindings shipped by Herald.Sinks.File so
/// the dashboard's POSTed JSON deserialises straight into this record
/// without per-key <c>TryGetProperty</c> reads.
/// </summary>
/// <remarks>
/// Every field is nullable. Missing keys come through as <c>null</c>,
/// which the apply-config path treats as "not provided — fall back to
/// the sink's own default." That keeps a partial-update POST working
/// the same way the dictionary-keyed path used to.
/// </remarks>
public sealed record FileSinkConfig
{
    /// <summary>Directory path (relative or absolute) for the log files.</summary>
    [JsonPropertyName("logDirectory")]
    public string? LogDirectory { get; init; }

    /// <summary>Base file name without extension. Required for the file path.</summary>
    [JsonPropertyName("logFileTemplate")]
    public string? LogFileTemplate { get; init; }

    /// <summary>File extension without leading dot (e.g. <c>log</c>, <c>ndjson</c>).</summary>
    [JsonPropertyName("logExtension")]
    public string? LogExtension { get; init; }

    /// <summary>Output-format hint surfaced by text_file (text vs csv).</summary>
    [JsonPropertyName("logOutputType")]
    public string? LogOutputType { get; init; }

    /// <summary>Minimum level for the sink. <c>null</c> inherits the pipeline minimum.</summary>
    [JsonPropertyName("minLevel")]
    public string? MinLevel { get; init; }

    /// <summary>Master toggle for time- and size-based file rolling.</summary>
    [JsonPropertyName("rollingLogsEnabled")]
    public bool RollingLogsEnabled { get; init; }

    /// <summary>Time-based rolling interval (<c>hourly</c>, <c>daily</c>, <c>custom</c>).</summary>
    [JsonPropertyName("rollingInterval")]
    public string? RollingInterval { get; init; }

    /// <summary>Size-based roll trigger as a human-readable string (<c>10MB</c>, <c>1GB</c>).</summary>
    [JsonPropertyName("maxFileSize")]
    public string? MaxFileSize { get; init; }

    /// <summary>How many rolled files to retain per period before pruning the oldest.</summary>
    [JsonPropertyName("maxRetainedFiles")]
    public int? MaxRetainedFiles { get; init; }

    /// <summary>.NET date format string injected into rolled file names.</summary>
    [JsonPropertyName("fileNamePattern")]
    public string? FileNamePattern { get; init; }

    /// <summary>Total-size cap across all rolled files (<c>1GB</c>, <c>500MB</c>).</summary>
    [JsonPropertyName("totalSizeCap")]
    public string? TotalSizeCap { get; init; }

    /// <summary>Drop rolled files older than this many days. <c>null</c> disables age pruning.</summary>
    [JsonPropertyName("retentionDays")]
    public int? RetentionDays { get; init; }
}

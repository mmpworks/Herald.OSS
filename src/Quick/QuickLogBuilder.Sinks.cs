#nullable enable

using MMP.Herald.Configuration;
using MMP.Herald.Configuration.Json;
using MMP.Herald.Output.Rich;

namespace MMP.Herald.Quick;

// Default sink trio (console / null / file) plus the four pipeline-level
// loopback knobs. Extracted from QuickLogBuilder.With.cs
// (principal-review queue #19) along Rosanne's seam map. Grouping the two
// seams in a single file preserves the author's intent: the loopback
// setters configure shared destinations the file sink consumes, and
// splitting them apart would obscure that relationship.
public sealed partial class QuickLogBuilder
{
    // -- Default sink trio --

    /// <summary>
    /// Add a standard console sink for regular log output.
    /// Channel events are excluded from this sink.
    /// </summary>
    public QuickLogBuilder WithConsoleSink(IRenderedLogOutputWriter? writer = null, string? minLevel = null) {
        _includeConsole = true;
        _consoleWriter = writer;
        _consoleMinLevel = minLevel;
        return this;
    }

    /// <summary>
    /// Add a sink that silently drops every event. Intended for benchmarks
    /// that measure pipeline cost in isolation and for configurations that
    /// want the pipeline present without downstream I/O.
    /// </summary>
    public QuickLogBuilder WithNullSink(string? minLevel = null) {
        _includeNullSink = true;
        _nullSinkMinLevel = minLevel;
        return this;
    }

    // -- Loopback knobs --

    // ── Loopback knobs (pipeline-level) ──────────────────────────
    // These four setters configure the loopback channels every sink
    // in the pipeline shares. Per-sink opt-in (RunState + TeeLiveTo*)
    // happens elsewhere; these set the shared destinations.

    /// <summary>
    /// Set the pipeline's loopback URL. The URL leg of every sink in
    /// run-state Test (or Live with TeeLiveToUrl on) POSTs each event
    /// to this URL as one NDJSON line. Supports two placeholders that
    /// the router factory substitutes once at wrap time:
    /// <c>{pipelineName}</c> → this pipeline's registry name,
    /// <c>{sinkName}</c>     → the per-sink Name field.
    /// Pass <c>null</c> or empty to disable the URL leg pipeline-wide.
    /// </summary>
    public QuickLogBuilder WithTestLoopbackUrl(string? url) {
        _testLoopbackUrl = string.IsNullOrWhiteSpace(url) ? null : url;
        return this;
    }

    /// <summary>
    /// Set the pipeline's loopback log directory. Sinks teeing to file
    /// write <c>{logDir}/{TestOutFileSuffix}-{SinkName}.{ndjson|log}</c>,
    /// rolling every <see cref="WithLoopbackEntriesPerFile"/> events.
    /// Pass <c>null</c> or empty to disable the file leg pipeline-wide.
    /// </summary>
    public QuickLogBuilder WithTestLoopbackLogDir(string? logDir) {
        _testLoopbackLogDir = string.IsNullOrWhiteSpace(logDir) ? null : logDir;
        return this;
    }

    /// <summary>
    /// Set the rotation cap for loopback files. The writer rolls to a
    /// new file each time it crosses this count. Defaults to 1000.
    /// Values &lt;= 0 are clamped to 1000 so a misconfigured input
    /// cannot turn the rolling logic into a tight loop.
    /// </summary>
    public QuickLogBuilder WithLoopbackEntriesPerFile(int entriesPerFile) {
        _loopbackEntriesPerFile = entriesPerFile > 0 ? entriesPerFile : 1000;
        return this;
    }

    /// <summary>
    /// Choose the loopback file format. <c>true</c> (default) writes
    /// NDJSON — one structured event per line, machine-parseable, the
    /// same shape the URL leg uses. <c>false</c> writes plain text
    /// rendered through the message field. The file extension follows
    /// the choice (<c>.ndjson</c> vs <c>.log</c>) so a directory listing
    /// tells the operator the format at a glance.
    /// </summary>
    public QuickLogBuilder WithLoopbackUseNdjson(bool useNdjson) {
        _loopbackUseNdjson = useNdjson;
        return this;
    }

    /// <summary>Add a file sink. Kind is inferred from extension (.ndjson/.jsonl → json_file, else text_file).</summary>
    public QuickLogBuilder WithFileSink(string path, string? minLevel = null) {
        _logFilePath = path;
        _logFileMinLevel = minLevel;
        _logFileKind = InferFileKind(path);
        return this;
    }

    /// <summary>Add a file sink with explicit kind (json_file, text_file, protobuf_file).</summary>
    public QuickLogBuilder WithFileSink(string path, string kind, string? minLevel = null) {
        _logFilePath = path;
        _logFileMinLevel = minLevel;
        _logFileKind = kind;
        return this;
    }

    /// <summary>Add a file sink with rolling file support.</summary>
    public QuickLogBuilder WithFileSink(
        string path,
        string interval,
        long? maxBytes = null,
        int? logQueueSize = null,
        int? maxRetainedFiles = null,
        int startMinute = 0,
        int captureDurationMinutes = 60,
        string? fileNameSuffix = null,
        string? locale = null,
        string? minLevel = null,
        int? retentionDays = null,
        long? totalSizeCapBytes = null) {
        // Preserve explicitly set kind if path hasn't changed
        if (_logFilePath != path)
            _logFileKind = InferFileKind(path);
        _logFilePath = path;
        _logFileMinLevel = minLevel;
        _logFileRolling = new JsonFileRollingConfig(
            Interval: interval,
            MaxBytes: maxBytes,
            MaxRetainedFiles: maxRetainedFiles,
            LogQueueSize: logQueueSize,
            StartMinute: startMinute,
            CaptureDurationMinutes: captureDurationMinutes,
            FileNameSuffix: fileNameSuffix,
            Locale: locale,
            RetentionDays: retentionDays,
            TotalSizeCapBytes: totalSizeCapBytes);
        return this;
    }

    private static string InferFileKind(string path) {
        if (path.EndsWith(".ndjson", System.StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".jsonl", System.StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".json", System.StringComparison.OrdinalIgnoreCase))
            return Services.KnownSinkKinds.JsonFile;
        if (path.EndsWith(".pb", System.StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".proto", System.StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".bin", System.StringComparison.OrdinalIgnoreCase))
            return Services.KnownSinkKinds.ProtobufFile;
        return Services.KnownSinkKinds.TextFile;
    }

    /// <summary>Add a file sink with a fluent rolling policy.</summary>
    public QuickLogBuilder WithFileSink(string path, FileSinkPolicy policy) {
        return WithFileSink(path,
            interval: policy.Interval,
            maxBytes: policy.MaxBytes,
            logQueueSize: policy.LogQueueSize,
            maxRetainedFiles: policy.MaxRetainedFiles,
            startMinute: policy.StartMinute,
            captureDurationMinutes: policy.CaptureDurationMinutes,
            fileNameSuffix: policy.FileNameSuffix,
            locale: policy.Locale,
            minLevel: policy.MinLevel,
            retentionDays: policy.RetentionDays,
            totalSizeCapBytes: policy.TotalSizeCapBytes);
    }
}

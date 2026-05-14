#nullable enable

using System;
using System.Collections.Generic;
using MMP.Herald.Pipeline;

namespace MMP.Herald.Configuration;

/// <summary>
/// Registry of known sink types with display metadata and edition requirements.
/// Analogous to <see cref="PipelineStep"/> but for sinks.
/// The dashboard queries this to render the Available Sinks panel with
/// correct display names, descriptions, help text, and edition badges.
/// </summary>
public sealed class KnownSink
{
    public string Kind { get; }
    public string DisplayName { get; }
    public string Description { get; }
    public string Help { get; }
    public VendorInfo Vendor { get; }
    public IReadOnlyList<Routing.SinkConfigField> ConfigurationSchema { get; }

    private KnownSink(string kind, string displayName, string description, string help = "",
        VendorInfo? vendor = null,
        IReadOnlyList<Routing.SinkConfigField>? schema = null)
    {
        Kind = kind;
        DisplayName = string.IsNullOrEmpty(displayName) ? kind : displayName;
        Description = description;
        Help = help;
        Vendor = vendor ?? VendorInfo.MMP;
        ConfigurationSchema = schema ?? [];
    }

    // ── Shared file sink schema ────────────────────────────────────

    /// <summary>
    /// Standard config fields for any file-based sink: <c>logDirectory</c>,
    /// <c>logFileTemplate</c>, <c>logExtension</c>, <c>minLevel</c>,
    /// <c>rollingLogsEnabled</c>, and the rolling group fields.
    /// Names match the v2 mmpform bindings shipped by Herald.Sinks.File so
    /// the JSON the dashboard POSTs round-trips through the management
    /// API without a translation layer.
    /// </summary>
    private static Routing.SinkConfigField[] FileSchemaFields(string defaultExtension, params string[] validExtensions) =>
    [
        // ── File identity ──
        Routing.SinkConfigField.String("logDirectory", "logs", "Directory",
            "Directory where log files are written. Relative paths are resolved from the application's working directory. The directory is created automatically if it doesn't exist."),
        Routing.SinkConfigField.String("logFileTemplate", null, "File name",
            "Base name for the log file without extension. When rolling is enabled, date tokens are appended automatically (e.g. app-server-20260413).", required: true),
        new Routing.SinkConfigField("logExtension", "choice", defaultExtension,
            "File extension. Determines the output format hint in the file name.",
            Options: validExtensions.Length > 0 ? validExtensions : [defaultExtension],
            Help: "The extension is appended to the template name: {logDirectory}/{logFileTemplate}.{logExtension}."),
        Routing.SinkConfigField.String("minLevel", null, "Minimum level",
            "Only events at or above this level are written to this file. Leave empty to inherit the pipeline's global minimum level."),

        // ── Rolling policy ──
        Routing.SinkConfigField.Bool("rollingLogsEnabled", true, "Enable file rolling",
            "File rolling automatically creates new log files based on time intervals or file size limits. " +
            "Old files are retained up to a configurable count and total size, then pruned automatically. " +
            "Disable to write to a single file indefinitely — useful for short-lived processes, test runs, or systems that handle log rotation externally (e.g. logrotate)."),
        new Routing.SinkConfigField("_rollingInfo", "info", null,
            "Naming convention",
            Help: "Rolled files are named: {logFileTemplate}-{fileNamePattern}.{logExtension}\n" +
                  "The pattern is a .NET date format string. Common tokens: yyyy (year), MM (month), dd (day), HH (hour 00-23), mm (minute).\n" +
                  "Examples with logFileTemplate='game-server', logExtension='ndjson':\n" +
                  "  Daily:  game-server-20260413.ndjson\n" +
                  "  Hourly: game-server-2026041314.ndjson\n" +
                  "  Custom: game-server-20260413_1430.ndjson\n" +
                  "Mid-period size rolls append an index: game-server-20260413.1.ndjson, game-server-20260413.2.ndjson.",
            Group: "rolling"),
        new Routing.SinkConfigField("rollingInterval", "choice", "daily",
            "Rolling interval",
            Options: ["hourly", "daily", "custom"],
            Help: "Time-based trigger that creates a new log file when the interval elapses. 'hourly' rolls at the top of each hour. 'daily' rolls at midnight. 'custom' uses startMinute and captureDuration for game-server-style sessions (e.g. match-based logging). Works alongside max file size — whichever triggers first causes the roll.",
            Group: "rolling", Row: "rolling-top", Span: 1),
        new Routing.SinkConfigField("maxFileSize", "string", "100MB",
            "Max file size",
            Help: "Size-based trigger that rolls the log file when it reaches this size. Accepts human-readable values: 10MB, 1GB, 500KB. Set to 0 for no size limit. When both time-based and size-based rolling are configured, whichever fires first wins. Size-rolled files get an index suffix (e.g. app-20260413.1.ndjson, app-20260413.2.ndjson).",
            Group: "rolling", Row: "rolling-top", Span: 1),
        new Routing.SinkConfigField("fileNamePattern", "string", null,
            "Name pattern",
            Help: "Uses .NET date format strings. Leave blank to use the default for the selected interval. See naming examples above.",
            Group: "rolling", Row: "rolling-top", Span: 2),
        new Routing.SinkConfigField("maxRetainedFiles", "int", 31,
            "Max retained files",
            Help: "Maximum number of rolled log files to keep per rolling period. When a new file is created and the count exceeds this limit, the oldest file in that period is deleted. With daily rolling and maxRetainedFiles=31, you keep approximately one month of history. Set to 0 to retain all files (use totalSizeCap as a safety net instead).",
            Group: "rolling", Row: "rolling-bottom", Span: 1),
        new Routing.SinkConfigField("totalSizeCap", "string", "0",
            "Total size cap",
            Help: "Maximum total size of all archived log files combined, across all rolling periods. When the total exceeds this value, the oldest archives are deleted regardless of maxRetainedFiles. Accepts human-readable values: 1GB, 500MB, 10GB. Set to 0 for no cap. Acts as a disk-space safety net - even if maxRetainedFiles allows many files, totalSizeCap prevents disk exhaustion.",
            Group: "rolling", Row: "rolling-bottom", Span: 1),

        // ── Retention policy ──
        new Routing.SinkConfigField("retentionDays", "int", null,
            "Retention days",
            Help: "Delete rolled log files older than this many days. Works alongside maxRetainedFiles and totalSizeCap - whichever is most restrictive wins. " +
                  "Set to 30 for one month of history, 90 for a quarter, 365 for a year. Leave empty for no age-based pruning. " +
                  "Age is determined by file last-write time, not file name. Inspired by Datadog (15-day default), Grafana Loki (retention_period), and Elasticsearch ILM delete phase.",
            Group: "rolling", Row: "rolling-retention", Span: 2),
    ];

    // ── Built-in sinks ──────────────────────────────────────────────

    public static KnownSink Console { get; } = new(Services.KnownSinkKinds.Console,
        "Console", "Writes formatted output to stdout",
        "Writes rendered log events to standard output using the configured output template and level styles. Supports ANSI color codes. Use for development, debugging, and Docker container logging.",
        schema:
        [
            Routing.SinkConfigField.String("minLevel", null, "Minimum level",
                "Only events at or above this level are written to the console. Leave empty to inherit the pipeline's global minimum level. Overrides are useful when you want verbose file logging but quieter console output."),
        ]);

    public static KnownSink JsonFile { get; } = new(Services.KnownSinkKinds.JsonFile,
        "JSON File", "Writes NDJSON log files to disk",
        "Writes each event as a single JSON line (NDJSON) to a file. Primary sink for production logging. Each instance manages its own file path, rolling, and retention.",
        schema: FileSchemaFields("ndjson", "ndjson", "jsonl", "json"));

    public static KnownSink TextFile { get; } = new(Services.KnownSinkKinds.TextFile,
        "Text File", "Writes plain-text log files to disk",
        "Writes human-readable plain-text log lines. Each instance manages its own file path, rolling, and retention independently.",
        schema: FileSchemaFields("log", "log", "txt"));

    public static KnownSink Channel { get; } = new(Services.KnownSinkKinds.Channel,
        "Channel", "Named channel for domain-specific output",
        "Named output streams for domain-specific logging. Channels can have independent output templates, writers, and signal handlers.",
        schema:
        [
            Routing.SinkConfigField.String("channelName", null, "Channel name",
                "Unique identifier for this channel. Events are routed to channels based on their category or explicit channel targeting. Example: Combat, Economy, Network."),
        ]);

    public static KnownSink Bridge { get; } = new(Services.KnownSinkKinds.PipelineBridge,
        "Bridge", "Pipeline bridge for external systems",
        "Forwards log events to an external ILogger implementation. Used for live log capture (Dashboard), cross-system integration, and custom event processing.",
        schema:
        [
            Routing.SinkConfigField.String("bridgeType", null, "Bridge type",
                "The type of bridge: LiveLogCapture for dashboard streaming, or a custom ILogger implementation for cross-system integration."),
        ]);

    // ── Network sinks ───────────────────────────────────────────────

    public static KnownSink HttpJson { get; } = new(Services.KnownSinkKinds.HttpJson,
        "HTTP JSON", "Posts log events as JSON to an HTTP endpoint",
        "Batches log events and POSTs them as JSON arrays to a configurable HTTP endpoint. Supports endpoint URL, batch size, flush interval, and HTTP headers for auth.",
        schema:
        [
            Routing.SinkConfigField.String("endpoint", null, "Endpoint URL",
                "The HTTP URL to POST batched log events to. Must accept JSON arrays. Include the full URL with protocol (https://logs.example.com/ingest).", required: true),
            Routing.SinkConfigField.Int("batchSize", 100, "Batch size",
                "Number of events to collect before sending an HTTP POST. Larger batches reduce HTTP overhead but increase latency. The batch also flushes on the flush interval."),
            Routing.SinkConfigField.Int("flushIntervalMs", 5000, "Flush interval (ms)",
                "Maximum time to wait before sending an incomplete batch. Ensures events reach the endpoint within this window even under low throughput."),
            Routing.SinkConfigField.String("minLevel", null, "Minimum level",
                "Only events at or above this level are sent. Leave empty to inherit the pipeline's global minimum level."),
        ]);

    public static KnownSink TcpJsonLine { get; } = new(Services.KnownSinkKinds.TcpJsonLine,
        "TCP JSON Line", "Streams JSON log lines over a TCP connection",
        "Persistent TCP connection streaming newline-delimited JSON. Lower overhead than HTTP for high-volume scenarios. Use for Logstash, Fluentd, or similar.",
        schema:
        [
            Routing.SinkConfigField.String("host", null, "Host",
                "The hostname or IP address of the TCP endpoint. Example: logstash.internal or 10.0.1.50.", required: true),
            Routing.SinkConfigField.Int("port", 5000, "Port",
                "TCP port to connect to. Common ports: 5000 (Logstash), 24224 (Fluentd).", required: true),
            Routing.SinkConfigField.String("minLevel", null, "Minimum level",
                "Only events at or above this level are streamed. Leave empty to inherit the pipeline's global minimum level."),
        ]);

    public static KnownSink Elasticsearch { get; } = new(Services.KnownSinkKinds.Elasticsearch,
        "Elasticsearch", "Indexes log events into Elasticsearch",
        "Indexes events directly into Elasticsearch via the Bulk API. Batched for efficiency. Configure cluster URL, index pattern, and auth.",
        schema:
        [
            Routing.SinkConfigField.String("clusterUrl", null, "Cluster URL",
                "Elasticsearch cluster endpoint. Example: https://elasticsearch.internal:9200. Include protocol and port.", required: true),
            Routing.SinkConfigField.String("indexPattern", "herald-{0:yyyy.MM.dd}", "Index pattern",
                "Index name with optional date pattern for daily rolling. {0} is replaced with the event timestamp. Example: herald-{0:yyyy.MM.dd} creates indices like herald-2026.04.13."),
            Routing.SinkConfigField.String("minLevel", null, "Minimum level",
                "Only events at or above this level are indexed. Leave empty to inherit the pipeline's global minimum level."),
        ]);

    public static KnownSink SlackWebhook { get; } = new(Services.KnownSinkKinds.SlackWebhook,
        "Slack Webhook", "Posts log events to a Slack channel via webhook",
        "Posts events to Slack via incoming webhook. Best used with filtering for errors and alerts only. Color-coded severity attachments.",
        schema:
        [
            Routing.SinkConfigField.String("webhookUrl", null, "Webhook URL",
                "Slack incoming webhook URL. Create one at api.slack.com/incoming-webhooks. Events are formatted as message attachments with color-coded severity.", required: true),
            Routing.SinkConfigField.String("minLevel", "error", "Minimum level",
                "Only events at or above this level are posted to Slack. Defaults to 'error' — you probably don't want every debug message in Slack."),
        ]);

    public static KnownSink GenericWebhook { get; } = new(Services.KnownSinkKinds.GenericWebhook,
        "Generic Webhook", "Posts log events to any HTTP webhook endpoint with rules engine",
        "POSTs events as JSON to any webhook endpoint. Includes a rules engine for pattern-based triggering " +
        "inspired by PagerDuty Event Rules, Opsgenie Alert Policies, and Grafana Notification Policies. " +
        "Rules support level thresholds, category matching, message regex, property filters, and per-rule cooldowns.",
        schema:
        [
            Routing.SinkConfigField.String("url", null, "Webhook URL",
                "The URL to POST events to. Each event is sent individually (not batched) for real-time delivery.", required: true),
            Routing.SinkConfigField.String("minLevel", "warn", "Minimum level",
                "Only events at or above this level trigger a webhook call. Defaults to 'warn' to avoid flooding the endpoint."),

            // ── Rules engine ──
            Routing.SinkConfigField.Bool("enableRules", false, "Enable rules engine",
                "When enabled, events are evaluated against rules before sending. " +
                "Only events matching at least one rule (with cooldown enforcement) are delivered. " +
                "Without rules, all events above minLevel are sent."),
            new Routing.SinkConfigField("_rulesInfo", "info", null,
                "Rules engine",
                Help: "Rules are evaluated in order. Each rule has conditions (all must match - AND logic) " +
                      "and an optional cooldown to prevent flooding.\n\n" +
                      "Condition types:\n" +
                      "  - minLevel: event level >= threshold (like PagerDuty severity routing)\n" +
                      "  - categoryEquals/categoryContains: match event category (like Opsgenie tags)\n" +
                      "  - messageContains/messageMatches: text search or regex (like Elasticsearch watcher)\n" +
                      "  - propertyExists/propertyEquals: structured property filter (like Datadog facets)\n\n" +
                      "Example rules:\n" +
                      "  1. 'Critical Alerts' - minLevel=critical, cooldown=300s\n" +
                      "  2. 'Payment Errors' - categoryEquals=Payment + minLevel=error, cooldown=60s\n" +
                      "  3. 'OOM Detection' - messageMatches=OutOfMemory, cooldown=600s",
                Group: "rules"),
            Routing.SinkConfigField.Int("defaultCooldownSeconds", 60, "Default cooldown (seconds)",
                "Default minimum seconds between consecutive webhook calls for the same rule. " +
                "Prevents flooding. Per-rule cooldowns override this. Set to 0 for no cooldown. " +
                "Similar to PagerDuty's dedup_key suppression window.",
                group: "rules"),
        ]);

    // ── OTLP sinks ──────────────────────────────────────────────────

    public static KnownSink OtlpJson { get; } = new(Services.KnownSinkKinds.OtlpJson,
        "OTLP JSON", "Exports logs via OpenTelemetry JSON protocol",
        "Exports events using OTLP in JSON encoding over HTTP. Standard way to send logs to OpenTelemetry-compatible backends.",
        schema:
        [
            Routing.SinkConfigField.String("endpoint", null, "Collector endpoint",
                "OTLP collector HTTP endpoint. Example: http://otel-collector:4318/v1/logs. Must support OTLP/HTTP with JSON encoding.", required: true),
            Routing.SinkConfigField.String("minLevel", null, "Minimum level",
                "Only events at or above this level are exported. Leave empty to inherit the pipeline's global minimum level."),
        ]);

    public static KnownSink OtlpProtobuf { get; } = new(Services.KnownSinkKinds.OtlpProtobuf,
        "OTLP Protobuf", "Exports logs via OpenTelemetry Protobuf protocol",
        "Exports events using OTLP in binary Protobuf encoding. More efficient than JSON — smaller payloads and faster serialization.",
        schema:
        [
            Routing.SinkConfigField.String("endpoint", null, "Collector endpoint",
                "OTLP collector endpoint. For gRPC: http://otel-collector:4317. For HTTP: http://otel-collector:4318/v1/logs. Must support Protobuf encoding.", required: true),
            Routing.SinkConfigField.String("minLevel", null, "Minimum level",
                "Only events at or above this level are exported. Leave empty to inherit the pipeline's global minimum level."),
        ]);

    public static KnownSink ProtobufFile { get; } = new(Services.KnownSinkKinds.ProtobufFile,
        "Protobuf File", "Writes logs as Protobuf-encoded files to disk",
        "Binary Protobuf format to local files. 3-5x smaller than JSON. Each instance manages its own file path, rolling, and retention.",
        schema: FileSchemaFields("pb", "pb", "proto", "bin"));

    // ── Registry ────────────────────────────────────────────────────

    private static readonly Dictionary<string, KnownSink> _byKind = new(StringComparer.OrdinalIgnoreCase)
    {
        [Services.KnownSinkKinds.Console] = Console,
        [Services.KnownSinkKinds.JsonFile] = JsonFile,
        [Services.KnownSinkKinds.TextFile] = TextFile,
        [Services.KnownSinkKinds.Channel] = Channel,
        [Services.KnownSinkKinds.PipelineBridge] = Bridge,
        [Services.KnownSinkKinds.HttpJson] = HttpJson,
        [Services.KnownSinkKinds.TcpJsonLine] = TcpJsonLine,
        [Services.KnownSinkKinds.Elasticsearch] = Elasticsearch,
        [Services.KnownSinkKinds.SlackWebhook] = SlackWebhook,
        [Services.KnownSinkKinds.GenericWebhook] = GenericWebhook,
        [Services.KnownSinkKinds.OtlpJson] = OtlpJson,
        [Services.KnownSinkKinds.OtlpProtobuf] = OtlpProtobuf,
        [Services.KnownSinkKinds.ProtobufFile] = ProtobufFile,
    };

    public static KnownSink? FromKind(string kind) =>
        _byKind.TryGetValue(kind, out var sink) ? sink : null;

    public static IEnumerable<string> AllKinds => _byKind.Keys;

    /// <summary>
    /// Register a custom sink type for plugin providers.
    /// </summary>
    public static KnownSink Register(string kind, string displayName = "", string description = "",
        string help = "", VendorInfo? vendor = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);

        if (_byKind.TryGetValue(kind, out var existing))
        {
            if (!string.IsNullOrEmpty(displayName) || !string.IsNullOrEmpty(description) ||
                !string.IsNullOrEmpty(help) || vendor is not null)
            {
                var updated = new KnownSink(kind,
                    !string.IsNullOrEmpty(displayName) ? displayName : existing.DisplayName,
                    !string.IsNullOrEmpty(description) ? description : existing.Description,
                    !string.IsNullOrEmpty(help) ? help : existing.Help,
                    vendor ?? existing.Vendor);
                _byKind[kind] = updated;
                return updated;
            }
            return existing;
        }

        var sink = new KnownSink(kind, displayName, description, help,
            vendor ?? VendorInfo.ThirdParty);
        _byKind[kind] = sink;
        return sink;
    }
}

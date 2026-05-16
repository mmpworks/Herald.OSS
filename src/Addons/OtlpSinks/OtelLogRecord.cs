#nullable enable

using System;
using System.Collections.Generic;
using MMP.Herald.Events;
using MMP.Herald.Levels;
using MMP.Herald.Templating;

namespace MMP.Herald.Addons.OtlpSinks;

/// <summary>
/// Maps Herald's LogEvent to the OpenTelemetry Logs data model (LogRecord).
/// This is the alignment layer that lets Herald events flow into any OTel-compatible
/// collector, dashboard, or analysis tool with correct semantic meaning.
///
/// OTel LogRecord fields (https://opentelemetry.io/docs/specs/otel/logs/data-model/):
///   - Timestamp          → LogEvent.TimeUtc
///   - SeverityNumber     → Mapped from LogLevel rank
///   - SeverityText       → LogLevel.DisplayName
///   - Body               → LogEvent.Message (rendered)
///   - Attributes         → LogEvent.Properties (flattened)
///   - Resource           → service.name, service.version, etc. from Context
///   - TraceId            → Context["traceId"]
///   - SpanId             → Context["spanId"]
///   - TraceFlags         → 0 or 1
///   - InstrumentationScope → "MMP.Herald"
///
/// Usage:
///   var record = OtelLogRecord.FromLogEvent(logEvent, levelRegistry);
///   // record is now a plain object that serializes to the OTel JSON schema
///
/// Or use the extension method:
///   var record = logEvent.ToOtelLogRecord(levelRegistry);
/// </summary>
public sealed class OtelLogRecord
{
    // OTel Severity Number mapping (https://opentelemetry.io/docs/specs/otel/logs/data-model/#field-severitynumber)
    // OTel defines 24 severity levels. Herald maps to the standard ones:
    //   TRACE=1, DEBUG=5, INFO=9, WARN=13, ERROR=17, FATAL=21
    private static readonly Dictionary<string, int> SeverityMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["trace"] = 1,    // TRACE
        ["debug"] = 5,    // DEBUG
        ["info"] = 9,     // INFO
        ["notice"] = 10,  // INFO2
        ["warn"] = 13,    // WARN
        ["error"] = 17,   // ERROR
        ["critical"] = 21, // FATAL
        ["fatal"] = 21,   // FATAL
    };

    /// <summary>Nanoseconds since Unix epoch (OTel uses nanos, not ticks).</summary>
    public long TimeUnixNano { get; init; }

    /// <summary>OTel severity number (1-24).</summary>
    public int SeverityNumber { get; init; }

    /// <summary>OTel severity text (e.g., "INFO", "ERROR").</summary>
    public string SeverityText { get; init; } = "";

    /// <summary>The rendered log message.</summary>
    public string Body { get; init; } = "";

    /// <summary>Structured attributes (from Properties + selected Context).</summary>
    public Dictionary<string, object?> Attributes { get; init; } = new();

    /// <summary>Resource attributes (service identity).</summary>
    public Dictionary<string, object?> Resource { get; init; } = new();

    /// <summary>W3C trace ID (32 hex chars), or null.</summary>
    public string? TraceId { get; init; }

    /// <summary>W3C span ID (16 hex chars), or null.</summary>
    public string? SpanId { get; init; }

    /// <summary>Instrumentation scope name.</summary>
    public string InstrumentationScope { get; init; } = "MMP.Herald";

    /// <summary>
    /// Map a Herald LogEvent to an OTel LogRecord.
    /// </summary>
    public static OtelLogRecord FromLogEvent(LogEvent logEvent, ILogLevelRegistry? levelRegistry = null)
    {
        ArgumentNullException.ThrowIfNull(logEvent);

        var severityNumber = SeverityMap.TryGetValue(logEvent.Level.Key, out var sn) ? sn : 9;
        var unixNano = (logEvent.TimeUtc.ToUnixTimeMilliseconds()) * 1_000_000;

        // Attributes: flattened properties + category + messageTemplate + eventId
        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var prop in logEvent.Properties)
            attributes[prop.Name] = prop.ResolvedValue;

        attributes["log.category"] = logEvent.Category.Value;
        attributes["log.message_template"] = logEvent.MessageTemplate;

        if (logEvent.EventId is not null)
        {
            attributes["log.event_id"] = logEvent.EventId.Id;
            if (logEvent.EventId.Name is not null)
                attributes["log.event_name"] = logEvent.EventId.Name;
        }

        // OpenTelemetry exception semantic conventions
        // (https://opentelemetry.io/docs/specs/otel/exceptions/).
        // Source: LogEvent.Context["exception"], set by Error(Exception, ...)
        // overloads and the HeraldLoggerProvider MEL adapter. Three
        // attributes attach to the LogRecord so downstream backends
        // (Sentry, Honeycomb, OpenObserve, Traceway, Datadog) group Issues
        // by exception.type + stacktrace hash. AggregateException and
        // InnerException chains ride inside ToString() — that's the
        // canonical .NET format every backend already parses. PII redaction
        // is upstream; the encoder does not re-filter.
        if (logEvent.Context.TryGetValue(Services.LogContextKeys.Exception, out var exObj)
            && exObj is Exception ex)
        {
            attributes["exception.type"] = ex.GetType().FullName ?? ex.GetType().Name;
            if (!string.IsNullOrEmpty(ex.Message))
                attributes["exception.message"] = ex.Message;
            attributes["exception.stacktrace"] = ex.ToString();
        }

        // Resource: service identity fields from context
        var resource = new Dictionary<string, object?>(StringComparer.Ordinal);
        var resourceKeys = new[] { "service.name", "service.version", "service.region", "service.environment" };
        foreach (var key in resourceKeys)
        {
            if (logEvent.Context.TryGetValue(key, out var val) && val is not null)
                resource[key] = val;
        }

        // Non-resource context as attributes (machine, process, caller info).
        // Skip the "exception" key — already emitted as semconv attributes
        // above, and the raw Exception object is not a valid OTLP KeyValue.
        foreach (var (key, val) in logEvent.Context)
        {
            if (key.StartsWith("service.", StringComparison.Ordinal)) continue;
            if (key.StartsWith("_", StringComparison.Ordinal)) continue;
            if (key == Services.LogContextKeys.Exception) continue;
            if (val is null) continue;
            attributes[key] = val;
        }

        return new OtelLogRecord
        {
            TimeUnixNano = unixNano,
            SeverityNumber = severityNumber,
            SeverityText = logEvent.Level.DisplayName.ToUpperInvariant(),
            Body = logEvent.Message,
            Attributes = attributes,
            Resource = resource,
            TraceId = logEvent.Context.TryGetValue(Services.LogContextKeys.TraceId, out var tid)
                ? tid?.ToString() : null,
            SpanId = logEvent.Context.TryGetValue(Services.LogContextKeys.SpanId, out var sid)
                ? sid?.ToString() : null
        };
    }
}

/// <summary>Extension method for fluent conversion.</summary>
public static class OtelLogRecordExtensions
{
    public static OtelLogRecord ToOtelLogRecord(this LogEvent logEvent, ILogLevelRegistry? levelRegistry = null)
        => OtelLogRecord.FromLogEvent(logEvent, levelRegistry);
}

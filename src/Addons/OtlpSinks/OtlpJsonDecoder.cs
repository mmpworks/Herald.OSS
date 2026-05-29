#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using MMP.Herald.Events;
using MMP.Herald.Levels;
using MMP.Herald.Services;
using MMP.Herald.Templating;

namespace MMP.Herald.Addons.OtlpSinks;

/// <summary>
/// Decodes OTLP/HTTP JSON log payloads (ExportLogsServiceRequest) into
/// Herald <see cref="LogEvent"/> instances. Complements
/// <see cref="OtelLogRecord"/>, which encodes in the other direction.
///
/// <para>
/// Handles the full <c>resourceLogs[] / scopeLogs[] / logRecords[]</c> tree.
/// Resource attributes flow into every resulting event's <c>Context</c>;
/// scope name lands as a context key too. Log-record attributes become
/// Herald properties. Severity numbers map back to the common log level keys
/// (trace / debug / info / warn / error / fatal). A record with no resolvable
/// severity falls back to the pipeline's current minimum level (or info when no
/// floor is set).
/// </para>
///
/// <para>
/// The decoder is tolerant: unknown fields are ignored, not rejected. Malformed
/// JSON throws <see cref="JsonException"/>; callers (the Server ingest
/// endpoint) surface that as a 400.
/// </para>
/// </summary>
public static class OtlpJsonDecoder
{
    // Reverse of OtelLogRecord.SeverityMap. 24 canonical values in OTLP; we
    // map each decade to the Herald key with the right semantic weight.
    private static readonly Dictionary<int, string> SeverityFromNumber = new()
    {
        [1] = "trace", [2] = "trace", [3] = "trace", [4] = "trace",
        [5] = "debug", [6] = "debug", [7] = "debug", [8] = "debug",
        [9] = "info", [10] = "info", [11] = "info", [12] = "info",
        [13] = "warn", [14] = "warn", [15] = "warn", [16] = "warn",
        [17] = "error", [18] = "error", [19] = "error", [20] = "error",
        [21] = "fatal", [22] = "fatal", [23] = "fatal", [24] = "fatal",
    };

    /// <summary>
    /// Parse an OTLP/HTTP JSON logs payload and yield the decoded events.
    /// OTLP carries an optional severity, so a record with no resolvable
    /// level falls back to <paramref name="optionalLevelDefault"/> (the
    /// pipeline's current minimum) when supplied, else <c>info</c>.
    /// </summary>
    public static IReadOnlyList<LogEvent> Decode(
        string json, ILogLevelRegistry levelRegistry, LogLevel? optionalLevelDefault = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        ArgumentNullException.ThrowIfNull(levelRegistry);

        using var doc = JsonDocument.Parse(json);
        return Decode(doc.RootElement, levelRegistry, optionalLevelDefault);
    }

    /// <summary>
    /// Parse a pre-opened OTLP JSON root element. Useful when the caller
    /// already owns a <see cref="JsonDocument"/> and does not want to
    /// re-stringify.
    /// </summary>
    public static IReadOnlyList<LogEvent> Decode(
        JsonElement root, ILogLevelRegistry levelRegistry, LogLevel? optionalLevelDefault = null)
    {
        ArgumentNullException.ThrowIfNull(levelRegistry);
        var events = new List<LogEvent>();

        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("resourceLogs", out var resourceLogs) ||
            resourceLogs.ValueKind != JsonValueKind.Array)
        {
            return events;
        }

        foreach (var resourceLog in resourceLogs.EnumerateArray())
        {
            var resourceContext = ReadResourceAttributes(resourceLog);

            if (!resourceLog.TryGetProperty("scopeLogs", out var scopeLogs) ||
                scopeLogs.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var scopeLog in scopeLogs.EnumerateArray())
            {
                var scopeName = TryGetScopeName(scopeLog);

                if (!scopeLog.TryGetProperty("logRecords", out var logRecords) ||
                    logRecords.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var record in logRecords.EnumerateArray())
                {
                    var evt = DecodeRecord(record, scopeName, resourceContext, levelRegistry, optionalLevelDefault);
                    if (evt is not null) events.Add(evt);
                }
            }
        }

        return events;
    }

    private static LogEvent? DecodeRecord(
        JsonElement record,
        string? scopeName,
        IReadOnlyDictionary<string, object?> resourceContext,
        ILogLevelRegistry levelRegistry,
        LogLevel? optionalLevelDefault)
    {
        var timeUtc = ReadTimestamp(record);
        var level = ReadLevel(record, levelRegistry, optionalLevelDefault);
        var body = ReadBodyString(record);
        var properties = ReadAttributes(record);

        // Category defaults to scope name; falls back to a sentinel so downstream
        // sinks do not drop the event. OTLP has no "category" concept; scope is
        // the closest analog.
        var category = new LogCategory(!string.IsNullOrWhiteSpace(scopeName) ? scopeName : "otlp");

        // Trace context lands on Context under the same keys Activity-based
        // enrichers use, so downstream sinks render them identically whether
        // the event originated in-process or came in over OTLP.
        var traceId = ReadHexOrBase64Id(record, "traceId");
        var spanId = ReadHexOrBase64Id(record, "spanId");

        var context = resourceContext;
        var scopeAdded = scopeName is not null;
        var traceAdded = traceId is not null;
        var spanAdded = spanId is not null;
        if (scopeAdded || traceAdded || spanAdded)
        {
            // Only clone the dict when we actually need to add — most records
            // share the resource context instance.
            var enriched = new Dictionary<string, object?>(resourceContext);
            if (scopeAdded) enriched["otlp.scope"] = scopeName;
            if (traceAdded) enriched[LogContextKeys.TraceId] = traceId;
            if (spanAdded) enriched[LogContextKeys.SpanId] = spanId;
            context = enriched;
        }

        return new LogEvent(
            TimeUtc: timeUtc,
            Level: level,
            Category: category,
            MessageTemplate: body,
            Message: body,
            Properties: properties,
            Context: context);
    }

    private static DateTimeOffset ReadTimestamp(JsonElement record)
    {
        if (record.TryGetProperty("timeUnixNano", out var t) &&
            TryParseUnixNano(t, out var fromTime))
        {
            return fromTime;
        }
        if (record.TryGetProperty("observedTimeUnixNano", out var obs) &&
            TryParseUnixNano(obs, out var fromObserved))
        {
            return fromObserved;
        }
        return DateTimeOffset.UtcNow;
    }

    private static bool TryParseUnixNano(JsonElement value, out DateTimeOffset result)
    {
        // OTLP/HTTP JSON frequently stringifies 64-bit integers because JSON
        // numbers cannot precisely represent them. Accept both.
        long nanos = 0;
        var ok = value.ValueKind switch
        {
            JsonValueKind.Number => value.TryGetInt64(out nanos),
            JsonValueKind.String => long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out nanos),
            _ => false
        };
        if (!ok)
        {
            result = default;
            return false;
        }
        // Ticks are 100-nanosecond units; 1 ms = 1,000,000 ns = 10,000 ticks.
        var ticks = nanos / 100;
        result = new DateTimeOffset(DateTime.UnixEpoch.AddTicks(ticks), TimeSpan.Zero);
        return true;
    }

    private static LogLevel ReadLevel(
        JsonElement record, ILogLevelRegistry levelRegistry, LogLevel? optionalLevelDefault)
    {
        string? key = null;

        if (record.TryGetProperty("severityNumber", out var sn) && sn.ValueKind == JsonValueKind.Number &&
            sn.TryGetInt32(out var severity) && SeverityFromNumber.TryGetValue(severity, out var mapped))
        {
            key = mapped;
        }
        else if (record.TryGetProperty("severityText", out var st) && st.ValueKind == JsonValueKind.String)
        {
            key = st.GetString()?.ToLowerInvariant();
        }

        if (key is not null)
        {
            var resolved = levelRegistry.GetByKeyOrNull(key);
            if (resolved is not null) return resolved;
        }

        // OTLP severity is optional. When the record carries no resolvable
        // level, fall back to the pipeline's current minimum level if the
        // caller supplied one; otherwise keep the existing "info" behaviour
        // (e.g. a test or a floorless pipeline).
        return optionalLevelDefault
            ?? levelRegistry.GetByKeyOrNull("info")
            ?? new LogLevel("info", "INF");
    }

    private static string ReadBodyString(JsonElement record)
    {
        if (!record.TryGetProperty("body", out var body)) return string.Empty;
        if (body.ValueKind == JsonValueKind.String) return body.GetString() ?? string.Empty;
        if (body.ValueKind != JsonValueKind.Object) return body.ToString();

        // AnyValue: { "stringValue": "...", "intValue": "..", "boolValue": true, ... }
        if (body.TryGetProperty("stringValue", out var sv) && sv.ValueKind == JsonValueKind.String)
            return sv.GetString() ?? string.Empty;
        if (body.TryGetProperty("intValue", out var iv))
            return iv.ToString();
        if (body.TryGetProperty("doubleValue", out var dv))
            return dv.ToString();
        if (body.TryGetProperty("boolValue", out var bv))
            return bv.GetBoolean() ? "true" : "false";
        return body.ToString();
    }

    private static IReadOnlyList<LogProperty> ReadAttributes(JsonElement record)
    {
        if (!record.TryGetProperty("attributes", out var attrs) || attrs.ValueKind != JsonValueKind.Array)
            return LogEvent.EmptyProperties;

        var list = new List<LogProperty>();
        foreach (var attr in attrs.EnumerateArray())
        {
            if (attr.ValueKind != JsonValueKind.Object) continue;
            if (!attr.TryGetProperty("key", out var keyElement) || keyElement.ValueKind != JsonValueKind.String)
                continue;
            var key = keyElement.GetString();
            if (string.IsNullOrEmpty(key)) continue;

            var value = attr.TryGetProperty("value", out var v) ? ReadAnyValue(v) : null;
            list.Add(new LogProperty(key, value));
        }
        return list;
    }

    private static IReadOnlyDictionary<string, object?> ReadResourceAttributes(JsonElement resourceLog)
    {
        if (!resourceLog.TryGetProperty("resource", out var resource) ||
            resource.ValueKind != JsonValueKind.Object ||
            !resource.TryGetProperty("attributes", out var attrs) ||
            attrs.ValueKind != JsonValueKind.Array)
        {
            return LogEvent.EmptyContext;
        }

        var dict = new Dictionary<string, object?>();
        foreach (var attr in attrs.EnumerateArray())
        {
            if (attr.ValueKind != JsonValueKind.Object) continue;
            if (!attr.TryGetProperty("key", out var keyElement) || keyElement.ValueKind != JsonValueKind.String)
                continue;
            var key = keyElement.GetString();
            if (string.IsNullOrEmpty(key)) continue;
            dict[key] = attr.TryGetProperty("value", out var v) ? ReadAnyValue(v) : null;
        }
        return dict;
    }

    // OTLP/HTTP JSON emits trace/span ids as lowercase hex by default, but
    // older collectors and hand-crafted payloads sometimes emit base64. Accept
    // either — hex is the canonical form on the Herald side, so base64 is
    // decoded and re-hexed to keep the downstream shape uniform.
    private static string? ReadHexOrBase64Id(JsonElement record, string fieldName)
    {
        if (!record.TryGetProperty(fieldName, out var value) || value.ValueKind != JsonValueKind.String)
            return null;

        var raw = value.GetString();
        if (string.IsNullOrEmpty(raw)) return null;

        // Hex fast path: even length, every character in 0-9 a-f A-F.
        if (raw.Length % 2 == 0 && IsHex(raw))
        {
            return raw.ToLowerInvariant();
        }

        // Fall back to base64. A malformed value lands here and returns null
        // rather than throwing — the event still delivers, just without trace
        // context.
        try
        {
            var bytes = Convert.FromBase64String(raw);
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static bool IsHex(string s)
    {
        foreach (var c in s)
        {
            var ok = c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';
            if (!ok) return false;
        }
        return true;
    }

    private static string? TryGetScopeName(JsonElement scopeLog) =>
        scopeLog.TryGetProperty("scope", out var scope) &&
        scope.ValueKind == JsonValueKind.Object &&
        scope.TryGetProperty("name", out var name) &&
        name.ValueKind == JsonValueKind.String
            ? name.GetString()
            : null;

    // Flatten an OTLP AnyValue to a plain .NET object. Does not recurse into
    // ArrayValue / KvlistValue — those become their JSON string representation,
    // which is honest for a log-property surface.
    private static object? ReadAnyValue(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object) return value.ToString();

        if (value.TryGetProperty("stringValue", out var s) && s.ValueKind == JsonValueKind.String)
            return s.GetString();
        if (value.TryGetProperty("intValue", out var i))
            return i.ValueKind == JsonValueKind.Number && i.TryGetInt64(out var iv) ? iv : long.Parse(i.GetString() ?? "0", CultureInfo.InvariantCulture);
        if (value.TryGetProperty("doubleValue", out var d) && d.ValueKind == JsonValueKind.Number)
            return d.GetDouble();
        if (value.TryGetProperty("boolValue", out var b))
            return b.GetBoolean();
        if (value.TryGetProperty("arrayValue", out var a))
            return a.ToString();
        if (value.TryGetProperty("kvlistValue", out var kv))
            return kv.ToString();
        return null;
    }
}

#nullable enable

using System;
using System.Collections.Generic;
using Google.Protobuf;
using MMP.Herald.Events;
using MMP.Herald.Levels;
using MMP.Herald.Services;
using MMP.Herald.Templating;

namespace MMP.Herald.Addons.OtlpSinks;

/// <summary>
/// Decodes OTLP/HTTP protobuf log payloads (<c>ExportLogsServiceRequest</c>)
/// into Herald <see cref="LogEvent"/> instances. Mirrors the JSON decoder in
/// <see cref="OtlpJsonDecoder"/> so the ingest endpoint can accept either
/// content type and hand the same event shape to the pipeline.
///
/// <para>
/// Walks the wire format directly via <see cref="CodedInputStream"/>. No
/// generated message classes — every nested submessage reads its length,
/// then opens a fresh <c>CodedInputStream</c> over the sliced bytes. That
/// matches the pattern the encoder side uses (<c>OtlpProtobufLogSerializer</c>)
/// and keeps the OTLP addon free of generated-type boilerplate.
/// </para>
///
/// <para>
/// Unknown fields and unknown wire types are skipped. A truncated or garbled
/// payload throws <see cref="InvalidProtocolBufferException"/>; callers
/// (the Server ingest endpoint) surface that as a 400.
/// </para>
/// </summary>
public static class OtlpProtobufLogDecoder
{
    // Maps SeverityNumber (1-24) to Herald level keys. Matches OtlpJsonDecoder.
    // OTel wire vocab — not a Herald level key (deliberately not swept by the Serilog rename).
    // These string values are OTel SeverityText spellings that travel through
    // levelRegistry.GetByKeyOrNull(), which resolves old→new via the transitional alias map.
    // When Task 9 removes that alias map, update these values to Serilog vocab at that time.
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
    /// Decode a serialized <c>ExportLogsServiceRequest</c> payload. OTLP
    /// carries an optional severity, so a record with no resolvable level
    /// falls back to <paramref name="optionalLevelDefault"/> (the pipeline's
    /// current minimum) when supplied, else <c>info</c>.
    /// </summary>
    public static IReadOnlyList<LogEvent> Decode(
        byte[] payload, ILogLevelRegistry levelRegistry, LogLevel? optionalLevelDefault = null)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(levelRegistry);

        var events = new List<LogEvent>();
        if (payload.Length == 0) return events;

        var input = new CodedInputStream(payload);

        // ExportLogsServiceRequest: field 1 is repeated ResourceLogs.
        while (!input.IsAtEnd)
        {
            var tag = input.ReadTag();
            if (tag == 0) break;
            if (WireFormat.GetTagFieldNumber(tag) == 1 &&
                WireFormat.GetTagWireType(tag) == WireFormat.WireType.LengthDelimited)
            {
                var sub = input.ReadBytes().CreateCodedInput();
                DecodeResourceLogs(sub, events, levelRegistry, optionalLevelDefault);
            }
            else
            {
                input.SkipLastField();
            }
        }

        return events;
    }

    // ── ResourceLogs ────────────────────────────────────────────────

    private static void DecodeResourceLogs(
        CodedInputStream input,
        List<LogEvent> events,
        ILogLevelRegistry levelRegistry,
        LogLevel? optionalLevelDefault)
    {
        IReadOnlyDictionary<string, object?> resourceContext = LogEvent.EmptyContext;

        while (!input.IsAtEnd)
        {
            var tag = input.ReadTag();
            if (tag == 0) break;

            var field = WireFormat.GetTagFieldNumber(tag);
            var wire = WireFormat.GetTagWireType(tag);

            if (field == 1 && wire == WireFormat.WireType.LengthDelimited)
            {
                resourceContext = DecodeResourceAttributes(input.ReadBytes().CreateCodedInput());
            }
            else if (field == 2 && wire == WireFormat.WireType.LengthDelimited)
            {
                DecodeScopeLogs(input.ReadBytes().CreateCodedInput(), events, resourceContext, levelRegistry, optionalLevelDefault);
            }
            else
            {
                input.SkipLastField();
            }
        }
    }

    private static IReadOnlyDictionary<string, object?> DecodeResourceAttributes(CodedInputStream input)
    {
        Dictionary<string, object?>? attrs = null;

        while (!input.IsAtEnd)
        {
            var tag = input.ReadTag();
            if (tag == 0) break;
            if (WireFormat.GetTagFieldNumber(tag) == 1 &&
                WireFormat.GetTagWireType(tag) == WireFormat.WireType.LengthDelimited)
            {
                var kv = DecodeKeyValue(input.ReadBytes().CreateCodedInput());
                if (kv.HasValue)
                {
                    attrs ??= new Dictionary<string, object?>();
                    attrs[kv.Value.Key] = kv.Value.Value;
                }
            }
            else
            {
                input.SkipLastField();
            }
        }

        return attrs ?? LogEvent.EmptyContext;
    }

    // ── ScopeLogs ───────────────────────────────────────────────────

    private static void DecodeScopeLogs(
        CodedInputStream input,
        List<LogEvent> events,
        IReadOnlyDictionary<string, object?> resourceContext,
        ILogLevelRegistry levelRegistry,
        LogLevel? optionalLevelDefault)
    {
        string? scopeName = null;

        while (!input.IsAtEnd)
        {
            var tag = input.ReadTag();
            if (tag == 0) break;

            var field = WireFormat.GetTagFieldNumber(tag);
            var wire = WireFormat.GetTagWireType(tag);

            if (field == 1 && wire == WireFormat.WireType.LengthDelimited)
            {
                scopeName = DecodeInstrumentationScopeName(input.ReadBytes().CreateCodedInput());
            }
            else if (field == 2 && wire == WireFormat.WireType.LengthDelimited)
            {
                var evt = DecodeLogRecord(
                    input.ReadBytes().CreateCodedInput(),
                    scopeName, resourceContext, levelRegistry, optionalLevelDefault);
                if (evt is not null) events.Add(evt);
            }
            else
            {
                input.SkipLastField();
            }
        }
    }

    private static string? DecodeInstrumentationScopeName(CodedInputStream input)
    {
        string? name = null;
        while (!input.IsAtEnd)
        {
            var tag = input.ReadTag();
            if (tag == 0) break;
            if (WireFormat.GetTagFieldNumber(tag) == 1 &&
                WireFormat.GetTagWireType(tag) == WireFormat.WireType.LengthDelimited)
            {
                name = input.ReadString();
            }
            else
            {
                input.SkipLastField();
            }
        }
        return name;
    }

    // ── LogRecord ───────────────────────────────────────────────────

    // LogRecord field numbers mirror those in OtlpProtobufLogSerializer.WriteLogRecord.
    private static LogEvent? DecodeLogRecord(
        CodedInputStream input,
        string? scopeName,
        IReadOnlyDictionary<string, object?> resourceContext,
        ILogLevelRegistry levelRegistry,
        LogLevel? optionalLevelDefault)
    {
        ulong timeUnixNano = 0;
        ulong observedTimeUnixNano = 0;
        int severityNumber = 0;
        string severityText = string.Empty;
        string body = string.Empty;
        List<LogProperty>? properties = null;
        string? traceIdHex = null;
        string? spanIdHex = null;

        while (!input.IsAtEnd)
        {
            var tag = input.ReadTag();
            if (tag == 0) break;

            var field = WireFormat.GetTagFieldNumber(tag);
            var wire = WireFormat.GetTagWireType(tag);

            switch (field)
            {
                case 1 when wire == WireFormat.WireType.Fixed64:
                    timeUnixNano = input.ReadFixed64();
                    break;

                case 2 when wire == WireFormat.WireType.Varint:
                    severityNumber = input.ReadInt32();
                    break;

                case 3 when wire == WireFormat.WireType.LengthDelimited:
                    severityText = input.ReadString();
                    break;

                case 5 when wire == WireFormat.WireType.LengthDelimited:
                    body = DecodeAnyValueAsString(input.ReadBytes().CreateCodedInput());
                    break;

                case 6 when wire == WireFormat.WireType.LengthDelimited:
                    var kv = DecodeKeyValue(input.ReadBytes().CreateCodedInput());
                    if (kv.HasValue)
                    {
                        properties ??= new List<LogProperty>();
                        properties.Add(new LogProperty(kv.Value.Key, kv.Value.Value));
                    }
                    break;

                case 9 when wire == WireFormat.WireType.LengthDelimited:
                    // trace_id is a 16-byte identifier per the W3C trace-context
                    // spec. Stored as hex in LogContextKeys.TraceId so the
                    // downstream shape matches what ActivityEnricher writes.
                    traceIdHex = Convert.ToHexString(input.ReadBytes().ToByteArray()).ToLowerInvariant();
                    break;

                case 10 when wire == WireFormat.WireType.LengthDelimited:
                    // span_id is an 8-byte identifier per the W3C trace-context spec.
                    spanIdHex = Convert.ToHexString(input.ReadBytes().ToByteArray()).ToLowerInvariant();
                    break;

                case 11 when wire == WireFormat.WireType.Fixed64:
                    observedTimeUnixNano = input.ReadFixed64();
                    break;

                default:
                    input.SkipLastField();
                    break;
            }
        }

        var timeUtc = BuildTimestamp(timeUnixNano, observedTimeUnixNano);
        var level = ResolveLevel(severityNumber, severityText, levelRegistry, optionalLevelDefault);
        var category = new LogCategory(!string.IsNullOrWhiteSpace(scopeName) ? scopeName : "otlp");
        var context = MergeScope(resourceContext, scopeName, traceIdHex, spanIdHex);

        return new LogEvent(
            TimeUtc: timeUtc,
            Level: level,
            Category: category,
            MessageTemplate: body,
            Message: body,
            Properties: (IReadOnlyList<LogProperty>?)properties ?? LogEvent.EmptyProperties,
            Context: context);
    }

    private static DateTimeOffset BuildTimestamp(ulong timeUnixNano, ulong observedTimeUnixNano)
    {
        var nanos = timeUnixNano != 0 ? timeUnixNano : observedTimeUnixNano;
        if (nanos == 0) return DateTimeOffset.UtcNow;

        // Ticks are 100-nanosecond units; 1 ns = 0.01 tick.
        var ticks = (long)(nanos / 100UL);
        return new DateTimeOffset(DateTime.UnixEpoch.AddTicks(ticks), TimeSpan.Zero);
    }

    private static LogLevel ResolveLevel(
        int severityNumber, string severityText, ILogLevelRegistry registry, LogLevel? optionalLevelDefault)
    {
        string? key = null;
        if (severityNumber > 0 && SeverityFromNumber.TryGetValue(severityNumber, out var mapped))
        {
            key = mapped;
        }
        else if (!string.IsNullOrWhiteSpace(severityText))
        {
            key = severityText.ToLowerInvariant();
        }

        if (key is not null)
        {
            var resolved = registry.GetByKeyOrNull(key);
            if (resolved is not null) return resolved;
        }

        // OTLP severity is optional. When the record carries no resolvable
        // level, fall back to the pipeline's current minimum level if the
        // caller supplied one; otherwise keep the existing "information" behaviour
        // (e.g. a test or a floorless pipeline).
        // OTel wire vocab — not a Herald level key (deliberately not swept by the Serilog rename).
        // The alias map resolves "information" to the correct LogLevel; update when Task 9 lands.
        return optionalLevelDefault
            ?? registry.GetByKeyOrNull("information")
            ?? new LogLevel("information", "INF");
    }

    private static IReadOnlyDictionary<string, object?> MergeScope(
        IReadOnlyDictionary<string, object?> resource,
        string? scopeName,
        string? traceIdHex = null,
        string? spanIdHex = null)
    {
        var hasScope = !string.IsNullOrWhiteSpace(scopeName);
        var hasTrace = !string.IsNullOrWhiteSpace(traceIdHex);
        var hasSpan = !string.IsNullOrWhiteSpace(spanIdHex);

        if (!hasScope && !hasTrace && !hasSpan) return resource;

        var enriched = new Dictionary<string, object?>(resource);
        if (hasScope) enriched["otlp.scope"] = scopeName;
        if (hasTrace) enriched[LogContextKeys.TraceId] = traceIdHex;
        if (hasSpan) enriched[LogContextKeys.SpanId] = spanIdHex;
        return enriched;
    }

    // ── KeyValue / AnyValue ─────────────────────────────────────────

    private static (string Key, object? Value)? DecodeKeyValue(CodedInputStream input)
    {
        string? key = null;
        object? value = null;

        while (!input.IsAtEnd)
        {
            var tag = input.ReadTag();
            if (tag == 0) break;

            var field = WireFormat.GetTagFieldNumber(tag);
            var wire = WireFormat.GetTagWireType(tag);

            if (field == 1 && wire == WireFormat.WireType.LengthDelimited)
            {
                key = input.ReadString();
            }
            else if (field == 2 && wire == WireFormat.WireType.LengthDelimited)
            {
                value = DecodeAnyValue(input.ReadBytes().CreateCodedInput());
            }
            else
            {
                input.SkipLastField();
            }
        }

        return string.IsNullOrEmpty(key) ? null : (key!, value);
    }

    // AnyValue is a protobuf oneof: the first recognised field wins.
    private static object? DecodeAnyValue(CodedInputStream input)
    {
        while (!input.IsAtEnd)
        {
            var tag = input.ReadTag();
            if (tag == 0) break;

            var field = WireFormat.GetTagFieldNumber(tag);
            var wire = WireFormat.GetTagWireType(tag);

            switch (field)
            {
                case 1 when wire == WireFormat.WireType.LengthDelimited:
                    return input.ReadString();
                case 2 when wire == WireFormat.WireType.Varint:
                    return input.ReadBool();
                case 3 when wire == WireFormat.WireType.Varint:
                    return input.ReadInt64();
                case 4 when wire == WireFormat.WireType.Fixed64:
                    return BitConverter.Int64BitsToDouble((long)input.ReadFixed64());
                case 7 when wire == WireFormat.WireType.LengthDelimited:
                    return input.ReadBytes().ToByteArray();
                default:
                    input.SkipLastField();
                    break;
            }
        }
        return null;
    }

    // Body-specific helper: coerce whatever AnyValue carries into a display
    // string for LogEvent.Message. Mirrors OtlpJsonDecoder.ReadBodyString.
    private static string DecodeAnyValueAsString(CodedInputStream input)
    {
        var value = DecodeAnyValue(input);
        return value switch
        {
            null => string.Empty,
            string s => s,
            bool b => b ? "true" : "false",
            _ => value.ToString() ?? string.Empty,
        };
    }
}

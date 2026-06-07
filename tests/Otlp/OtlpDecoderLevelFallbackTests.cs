#nullable enable

using FluentAssertions;
using MMP.Herald.Addons.OtlpSinks;
using MMP.Herald.Levels;
using Xunit;

namespace MMP.Herald.OSS.Tests.Otlp;

/// <summary>
/// Regression tests for the OTLP decoder level-fallback path.
///
/// The main <see cref="OtlpDecoderHeraldLevelKeyTests"/> covers the happy
/// path — resolvable severity numbers and standard OTel text spellings.
/// This file covers the unresolvable / malformed / fallback cases, including
/// the G1.1 crash bug where whitespace-only <c>severityText</c> caused an
/// uncaught <see cref="ArgumentException"/> that killed the entire batch.
/// </summary>
public sealed class OtlpDecoderLevelFallbackTests
{
    private static ILogLevelRegistry MakeFullRegistry()
    {
        var r = new LogLevelRegistry();
        r.Register(KnownLogLevels.Verbose);
        r.Register(KnownLogLevels.Debug);
        r.Register(KnownLogLevels.Information);
        r.Register(KnownLogLevels.Warning);
        r.Register(KnownLogLevels.Error);
        r.Register(KnownLogLevels.Fatal);
        return r;
    }

    // ── G1.1 regression: whitespace severityText must fall back, not throw ───
    //
    // Before the fix: IsNullOrEmpty let whitespace through as 'key', then
    // GetByKeyOrNull hit ThrowIfNullOrWhiteSpace — ArgumentException — uncaught
    // by OtlpLogsDecoder (which only catches JsonException). One bad field
    // silently dropped every record in the export request.

    [Theory]
    [InlineData(" ")]
    [InlineData("   ")]
    public void JsonDecoder_whitespace_severityText_falls_back_without_throwing(string whitespace)
    {
        var registry = MakeFullRegistry();
        var json = BuildJsonWithSeverityText(whitespace);

        var act = () => OtlpJsonDecoder.Decode(json, registry);

        act.Should().NotThrow("whitespace severityText must fall back to information, not crash the batch");
        OtlpJsonDecoder.Decode(json, registry)[0].Level.Key.Should().Be("information");
    }

    // ── G1.2: protobuf already guarded IsNullOrWhiteSpace — pin the parity ───
    //
    // The protobuf decoder did not have the bug (it used IsNullOrWhiteSpace).
    // This test pins that both decoders now behave identically on whitespace so
    // a future refactor cannot quietly reintroduce the divergence.

    [Theory]
    [InlineData(" ")]
    [InlineData("   ")]
    public void ProtobufDecoder_whitespace_severityText_falls_back_without_throwing(string whitespace)
    {
        var registry = MakeFullRegistry();
        var payload = BuildProtobufWithSeverityText(whitespace);

        var act = () => OtlpProtobufLogDecoder.Decode(payload, registry);

        act.Should().NotThrow();
        OtlpProtobufLogDecoder.Decode(payload, registry)[0].Level.Key.Should().Be("information");
    }

    // ── G1.3: unknown string level names fall back, not drop ─────────────────
    //
    // "CRITICAL" is the sharp case: a real syslog/NLog level name that a caller
    // reasonably expects to map to error/fatal. It falls back to information —
    // a severity downgrade. This is deliberate; the test pins it explicitly so
    // a future mapping is a conscious decision, not an accidental change.

    [Theory]
    [InlineData("CUSTOM_LEVEL")]
    [InlineData("NOTICE")]
    [InlineData("TRACE2")]
    [InlineData("CRITICAL")]  // known downgrade: syslog "critical" → "information"
    public void JsonDecoder_unknown_severityText_falls_back_to_information(string unknownLevel)
    {
        var registry = MakeFullRegistry();
        var json = BuildJsonWithSeverityText(unknownLevel);

        var events = OtlpJsonDecoder.Decode(json, registry);

        events.Should().HaveCount(1, $"unknown level '{unknownLevel}' must not drop the event");
        events[0].Level.Key.Should().Be("information",
            $"'{unknownLevel}' is not in the registry; falls back to information");
    }

    // ── G3.4: empty-string severityText falls back cleanly ───────────────────

    [Fact]
    public void JsonDecoder_empty_severityText_falls_back_without_throwing()
    {
        var registry = MakeFullRegistry();
        var json = BuildJsonWithSeverityText(string.Empty);

        var act = () => OtlpJsonDecoder.Decode(json, registry);

        act.Should().NotThrow();
        OtlpJsonDecoder.Decode(json, registry)[0].Level.Key.Should().Be("information");
    }

    // ── G3.5: JSON null severityText falls back cleanly ──────────────────────
    //
    // Real serializers emit explicit null for unset optional fields. The guard
    // `st.ValueKind == JsonValueKind.String` already rejects it before ReadLevel
    // runs, so null is safe — this test pins that it remains safe.

    [Fact]
    public void JsonDecoder_null_severityText_falls_back_without_throwing()
    {
        var registry = MakeFullRegistry();
        var json = BuildJsonWithNullSeverityText();

        var act = () => OtlpJsonDecoder.Decode(json, registry);

        act.Should().NotThrow();
        OtlpJsonDecoder.Decode(json, registry)[0].Level.Key.Should().Be("information");
    }

    // ── G1.4 fix: OtlpLogsDecoder forwards optionalLevelDefault ─────────────
    //
    // Before the fix, OtlpLogsDecoder.Decode and OtlpLogsDecoder.DecodeJson
    // accepted no optionalLevelDefault parameter, so the documented "fall back
    // to pipeline minimum" contract was unreachable from the production ingest
    // path. The fallback was always "information" regardless of the floor.

    [Fact]
    public void Dispatcher_json_optionalLevelDefault_used_for_unresolvable_severity()
    {
        var registry = MakeFullRegistry();
        var warningLevel = registry.GetByKeyOrNull("warning")!;

        // "CRITICAL" is not in the registry; without a floor it lands at "information".
        // With the floor forwarded, it must land at "warning".
        var json = BuildJsonWithSeverityText("CRITICAL");

        var events = OtlpLogsDecoder.DecodeJson(json, registry, optionalLevelDefault: warningLevel);

        events.Should().HaveCount(1);
        events[0].Level.Key.Should().Be("warning",
            "DecodeJson must forward optionalLevelDefault to the leaf decoder");
    }

    [Fact]
    public void Dispatcher_protobuf_optionalLevelDefault_used_for_unresolvable_severity()
    {
        var registry = MakeFullRegistry();
        var warningLevel = registry.GetByKeyOrNull("warning")!;

        var payload = BuildProtobufWithSeverityText("CRITICAL");

        var events = OtlpLogsDecoder.Decode(payload, registry, optionalLevelDefault: warningLevel);

        events.Should().HaveCount(1);
        events[0].Level.Key.Should().Be("warning",
            "Decode must forward optionalLevelDefault to the leaf decoder");
    }

    [Fact]
    public void Dispatcher_json_optionalLevelDefault_not_used_when_severity_resolves()
    {
        var registry = MakeFullRegistry();
        var fatalLevel = registry.GetByKeyOrNull("fatal")!;

        // "INFO" resolves to "information"; the floor must not override a resolved level.
        var json = BuildJsonWithSeverityText("INFO");

        var events = OtlpLogsDecoder.DecodeJson(json, registry, optionalLevelDefault: fatalLevel);

        events.Should().HaveCount(1);
        events[0].Level.Key.Should().Be("information",
            "optionalLevelDefault must only apply when severity is unresolvable, not when it resolves");
    }

    // ── Builders ─────────────────────────────────────────────────────────────

    private static string BuildJsonWithSeverityText(string severityText) => $$"""
        {
          "resourceLogs": [{
            "scopeLogs": [{
              "logRecords": [{
                "severityText": "{{severityText}}",
                "body": { "stringValue": "test" }
              }]
            }]
          }]
        }
        """;

    private static string BuildJsonWithNullSeverityText() => """
        {
          "resourceLogs": [{
            "scopeLogs": [{
              "logRecords": [{
                "severityText": null,
                "body": { "stringValue": "test" }
              }]
            }]
          }]
        }
        """;

    private static byte[] BuildProtobufWithSeverityText(string severityText)
    {
        var logRecord = BuildLogRecordWithSeverityText(severityText);
        var scopeLogs = WrapField(2, logRecord);     // ScopeLogs.logRecords
        var resourceLogs = WrapField(2, scopeLogs);  // ResourceLogs.scopeLogs

        using var ms = new System.IO.MemoryStream();
        using var w = new Google.Protobuf.CodedOutputStream(ms);
        w.WriteTag(1, Google.Protobuf.WireFormat.WireType.LengthDelimited);
        w.WriteBytes(Google.Protobuf.ByteString.CopyFrom(resourceLogs));
        w.Flush();
        return ms.ToArray();
    }

    private static byte[] BuildLogRecordWithSeverityText(string severityText)
    {
        using var ms = new System.IO.MemoryStream();
        using var w = new Google.Protobuf.CodedOutputStream(ms);
        // LogRecord field 3 = severity_text (string)
        w.WriteTag(3, Google.Protobuf.WireFormat.WireType.LengthDelimited);
        w.WriteString(severityText);
        // LogRecord field 5 = body (AnyValue)
        var body = BuildAnyValueBytes("test");
        w.WriteTag(5, Google.Protobuf.WireFormat.WireType.LengthDelimited);
        w.WriteBytes(Google.Protobuf.ByteString.CopyFrom(body));
        w.Flush();
        return ms.ToArray();
    }

    private static byte[] BuildAnyValueBytes(string value)
    {
        using var ms = new System.IO.MemoryStream();
        using var w = new Google.Protobuf.CodedOutputStream(ms);
        w.WriteTag(1, Google.Protobuf.WireFormat.WireType.LengthDelimited);
        w.WriteString(value);
        w.Flush();
        return ms.ToArray();
    }

    private static byte[] WrapField(int fieldNumber, byte[] content)
    {
        using var ms = new System.IO.MemoryStream();
        using var w = new Google.Protobuf.CodedOutputStream(ms);
        w.WriteTag(fieldNumber, Google.Protobuf.WireFormat.WireType.LengthDelimited);
        w.WriteBytes(Google.Protobuf.ByteString.CopyFrom(content));
        w.Flush();
        return ms.ToArray();
    }
}

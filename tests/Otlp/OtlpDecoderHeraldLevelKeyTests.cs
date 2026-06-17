#nullable enable

using System.Collections.Generic;
using FluentAssertions;
using MMP.Herald.Addons.OtlpSinks;
using MMP.Herald.Levels;
using Xunit;

namespace MMP.Herald.OSS.Tests.Otlp;

/// <summary>
/// Pins the Herald-side level key produced by <see cref="OtlpJsonDecoder"/>
/// and <see cref="OtlpProtobufLogDecoder"/> after the Serilog rename wave.
///
/// After Task 6, the decoders' <c>SeverityFromNumber</c> maps must emit
/// Serilog-vocab Herald keys directly ("verbose", "information", "warning",
/// "fatal") so they no longer depend on the transitional alias map that
/// Task 9 removes. OTel-spec SeverityText INPUT spellings ("INFO", "WARN",
/// "TRACE") are kept as-is — only the Herald OUTPUT is updated.
///
/// These tests use a registry WITH the alias to verify the existing contract
/// then additionally verify the mapping dictionary values are new-vocab
/// (via a no-alias registry shim) so Task 9 does not break ingest.
/// </summary>
public sealed class OtlpDecoderHeraldLevelKeyTests
{
    // ── Helper: build a full-featured registry (alias map is present) ─────

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

    // ── Helper: build a registry with ONLY new-vocab keys registered.
    // Simulates post-Task-9 state: old-vocab lookups return null.

    private static ILogLevelRegistry MakeNoAliasRegistry()
        => new NoAliasRegistry(MakeFullRegistry());

    // ── OtlpJsonDecoder — severity number → Herald level key ─────────────

    [Theory]
    [InlineData(1,  "verbose")]     // TRACE1
    [InlineData(2,  "verbose")]     // TRACE2
    [InlineData(3,  "verbose")]     // TRACE3
    [InlineData(4,  "verbose")]     // TRACE4
    [InlineData(5,  "debug")]       // DEBUG1
    [InlineData(8,  "debug")]       // DEBUG4
    [InlineData(9,  "information")] // INFO1  — was "info" before Task 6
    [InlineData(12, "information")] // INFO4
    [InlineData(13, "warning")]     // WARN1  — was "warn" before Task 6
    [InlineData(16, "warning")]     // WARN4
    [InlineData(17, "error")]       // ERROR1
    [InlineData(20, "error")]       // ERROR4
    [InlineData(21, "fatal")]       // FATAL1
    [InlineData(24, "fatal")]       // FATAL4
    public void JsonDecoder_severity_number_produces_serilog_vocab_key(int severityNumber, string expectedKey)
    {
        var registry = MakeFullRegistry();
        var json = BuildMinimalOtlpJson(severityNumber: severityNumber);

        var events = OtlpJsonDecoder.Decode(json, registry);

        events.Should().HaveCount(1);
        events[0].Level.Key.Should().Be(expectedKey,
            $"OtlpJsonDecoder severity number {severityNumber} must produce Serilog-vocab key '{expectedKey}', not old-vocab");
    }

    [Theory]
    [InlineData(9,  "information")] // INFO range
    [InlineData(13, "warning")]     // WARN range
    [InlineData(1,  "verbose")]     // TRACE range
    [InlineData(21, "fatal")]       // FATAL range
    public void JsonDecoder_severity_number_new_vocab_works_without_alias(int severityNumber, string expectedKey)
    {
        // Use a no-alias registry: old keys ("info", "warn", "trace", "critical")
        // won't resolve. After Task 6, the decoder emits new-vocab so it resolves
        // against new keys directly, not through the alias.
        var registry = MakeNoAliasRegistry();
        var json = BuildMinimalOtlpJson(severityNumber: severityNumber);

        var events = OtlpJsonDecoder.Decode(json, registry);

        events.Should().HaveCount(1,
            $"OtlpJsonDecoder must resolve severity number {severityNumber} without the alias map");
        events[0].Level.Key.Should().Be(expectedKey,
            $"decoded level must be new-vocab '{expectedKey}'");
    }

    // ── OtlpJsonDecoder — old-vocab absent from output ────────────────────

    [Theory]
    [InlineData(9)]   // INFO range
    [InlineData(13)]  // WARN range
    [InlineData(1)]   // TRACE range
    public void JsonDecoder_severity_number_does_not_produce_old_vocab(int severityNumber)
    {
        var registry = MakeFullRegistry();
        var json = BuildMinimalOtlpJson(severityNumber: severityNumber);
        var events = OtlpJsonDecoder.Decode(json, registry);

        events.Should().HaveCount(1);

        var key = events[0].Level.Key;
        var oldVocabKeys = new[] { "info", "warn", "trace", "critical" };
        oldVocabKeys.Should().NotContain(key,
            $"severity number {severityNumber} must not produce old-vocab key; got '{key}'");
    }

    // ── OtlpProtobufLogDecoder — severity number → Herald level key ───────

    [Theory]
    [InlineData(1,  "verbose")]
    [InlineData(4,  "verbose")]
    [InlineData(5,  "debug")]
    [InlineData(8,  "debug")]
    [InlineData(9,  "information")]
    [InlineData(12, "information")]
    [InlineData(13, "warning")]
    [InlineData(16, "warning")]
    [InlineData(17, "error")]
    [InlineData(20, "error")]
    [InlineData(21, "fatal")]
    [InlineData(24, "fatal")]
    public void ProtobufDecoder_severity_number_produces_serilog_vocab_key(int severityNumber, string expectedKey)
    {
        var registry = MakeFullRegistry();
        var payload = BuildMinimalOtlpProtobuf(severityNumber: severityNumber);

        var events = OtlpProtobufLogDecoder.Decode(payload, registry);

        events.Should().HaveCount(1);
        events[0].Level.Key.Should().Be(expectedKey,
            $"OtlpProtobufLogDecoder severity number {severityNumber} must produce Serilog-vocab key '{expectedKey}'");
    }

    [Theory]
    [InlineData(9,  "information")]
    [InlineData(13, "warning")]
    [InlineData(1,  "verbose")]
    [InlineData(21, "fatal")]
    public void ProtobufDecoder_severity_number_new_vocab_works_without_alias(int severityNumber, string expectedKey)
    {
        var registry = MakeNoAliasRegistry();
        var payload = BuildMinimalOtlpProtobuf(severityNumber: severityNumber);

        var events = OtlpProtobufLogDecoder.Decode(payload, registry);

        events.Should().HaveCount(1,
            $"OtlpProtobufLogDecoder must resolve severity number {severityNumber} without the alias map");
        events[0].Level.Key.Should().Be(expectedKey);
    }

    // ── OTel SeverityText INPUT spellings — JSON path ────────────────────

    [Theory]
    [InlineData("INFO",  "information")]
    [InlineData("WARN",  "warning")]
    [InlineData("ERROR", "error")]
    [InlineData("DEBUG", "debug")]
    [InlineData("FATAL", "fatal")]
    [InlineData("TRACE", "verbose")]
    public void JsonDecoder_otel_severity_text_input_resolves_to_serilog_vocab(string otelSeverityText, string expectedKey)
    {
        var registry = MakeFullRegistry();
        var json = BuildMinimalOtlpJsonWithSeverityText(otelSeverityText);

        var events = OtlpJsonDecoder.Decode(json, registry);

        events.Should().HaveCount(1);
        events[0].Level.Key.Should().Be(expectedKey,
            $"OTel SeverityText '{otelSeverityText}' must decode to Herald key '{expectedKey}'");
    }

    // ── OTel SeverityText INPUT spellings — protobuf path ────────────────
    //
    // Before the fix, the protobuf decoder had no SeverityTextToHeraldKey table.
    // OTel short spellings (TRACE/INFO/WARN) became lowercase ("trace"/"info"/"warn"),
    // which return null after the Task 9 alias removal, silently demoting every such
    // event to "information". The table now mirrors the JSON decoder.

    [Theory]
    [InlineData("INFO",  "information")]
    [InlineData("WARN",  "warning")]
    [InlineData("ERROR", "error")]
    [InlineData("DEBUG", "debug")]
    [InlineData("FATAL", "fatal")]
    [InlineData("TRACE", "verbose")]
    public void ProtobufDecoder_otel_severity_text_input_resolves_to_serilog_vocab(string otelSeverityText, string expectedKey)
    {
        var registry = MakeFullRegistry();
        var payload = BuildMinimalOtlpProtobufWithSeverityText(otelSeverityText);

        var events = OtlpProtobufLogDecoder.Decode(payload, registry);

        events.Should().HaveCount(1);
        events[0].Level.Key.Should().Be(expectedKey,
            $"OTel SeverityText '{otelSeverityText}' must decode to Herald key '{expectedKey}' on the protobuf path");
    }

    [Theory]
    [InlineData("INFO",  "information")]
    [InlineData("WARN",  "warning")]
    [InlineData("TRACE", "verbose")]
    [InlineData("FATAL", "fatal")]
    public void ProtobufDecoder_otel_severity_text_works_without_alias(string otelSeverityText, string expectedKey)
    {
        // Post-Task-9 no-alias registry: old short-key lookups ("info"/"warn"/"trace") return null.
        // The translation table must resolve before the registry lookup so the result is correct
        // rather than falling back to "information".
        var registry = MakeNoAliasRegistry();
        var payload = BuildMinimalOtlpProtobufWithSeverityText(otelSeverityText);

        var events = OtlpProtobufLogDecoder.Decode(payload, registry);

        events.Should().HaveCount(1,
            $"OTel SeverityText '{otelSeverityText}' must resolve without the alias map");
        events[0].Level.Key.Should().Be(expectedKey);
    }

    // ── Minimal OTLP JSON builder ─────────────────────────────────────────

    private static string BuildMinimalOtlpJson(int severityNumber)
    {
        return $$"""
            {
              "resourceLogs": [{
                "scopeLogs": [{
                  "logRecords": [{
                    "severityNumber": {{severityNumber}},
                    "body": { "stringValue": "test message" }
                  }]
                }]
              }]
            }
            """;
    }

    private static string BuildMinimalOtlpJsonWithSeverityText(string severityText)
    {
        return $$"""
            {
              "resourceLogs": [{
                "scopeLogs": [{
                  "logRecords": [{
                    "severityText": "{{severityText}}",
                    "body": { "stringValue": "test message" }
                  }]
                }]
              }]
            }
            """;
    }

    // ── Minimal OTLP protobuf builder ────────────────────────────────────
    // Writes a minimal ExportLogsServiceRequest with one ResourceLogs, one
    // ScopeLogs, one LogRecord carrying the given severityNumber. Field
    // numbers mirror OtlpProtobufLogSerializer. Only the fields needed for
    // the level-key test are emitted.

    private static byte[] BuildMinimalOtlpProtobuf(int severityNumber)
    {
        using var ms = new System.IO.MemoryStream();
        using var writer = new Google.Protobuf.CodedOutputStream(ms);

        // LogRecord: field 5 = body (AnyValue), field 2 = severityNumber (varint)
        var logRecordBytes = BuildLogRecordBytes(severityNumber);

        // ScopeLogs: field 2 = repeated logRecords
        var scopeLogsBytes = BuildScopeLogsBytes(logRecordBytes);

        // ResourceLogs: field 2 = repeated scopeLogs
        var resourceLogsBytes = BuildResourceLogsBytes(scopeLogsBytes);

        // ExportLogsServiceRequest: field 1 = repeated resourceLogs
        writer.WriteTag(1, Google.Protobuf.WireFormat.WireType.LengthDelimited);
        writer.WriteBytes(Google.Protobuf.ByteString.CopyFrom(resourceLogsBytes));
        writer.Flush();
        return ms.ToArray();
    }

    private static byte[] BuildMinimalOtlpProtobufWithSeverityText(string severityText)
    {
        var logRecordBytes = BuildLogRecordBytesWithSeverityText(severityText);
        var scopeLogsBytes = BuildScopeLogsBytes(logRecordBytes);
        var resourceLogsBytes = BuildResourceLogsBytes(scopeLogsBytes);

        using var ms = new System.IO.MemoryStream();
        using var writer = new Google.Protobuf.CodedOutputStream(ms);
        writer.WriteTag(1, Google.Protobuf.WireFormat.WireType.LengthDelimited);
        writer.WriteBytes(Google.Protobuf.ByteString.CopyFrom(resourceLogsBytes));
        writer.Flush();
        return ms.ToArray();
    }

    private static byte[] BuildLogRecordBytes(int severityNumber)
    {
        using var ms = new System.IO.MemoryStream();
        using var w = new Google.Protobuf.CodedOutputStream(ms);

        // field 2 = severityNumber (varint)
        w.WriteTag(2, Google.Protobuf.WireFormat.WireType.Varint);
        w.WriteInt32(severityNumber);

        // field 5 = body (AnyValue) — write an empty AnyValue
        var bodyBytes = BuildAnyValueStringBytes("test");
        w.WriteTag(5, Google.Protobuf.WireFormat.WireType.LengthDelimited);
        w.WriteBytes(Google.Protobuf.ByteString.CopyFrom(bodyBytes));

        w.Flush();
        return ms.ToArray();
    }

    private static byte[] BuildLogRecordBytesWithSeverityText(string severityText)
    {
        using var ms = new System.IO.MemoryStream();
        using var w = new Google.Protobuf.CodedOutputStream(ms);

        // field 3 = severityText (string)
        w.WriteTag(3, Google.Protobuf.WireFormat.WireType.LengthDelimited);
        w.WriteString(severityText);

        var bodyBytes = BuildAnyValueStringBytes("test");
        w.WriteTag(5, Google.Protobuf.WireFormat.WireType.LengthDelimited);
        w.WriteBytes(Google.Protobuf.ByteString.CopyFrom(bodyBytes));

        w.Flush();
        return ms.ToArray();
    }

    private static byte[] BuildAnyValueStringBytes(string value)
    {
        using var ms = new System.IO.MemoryStream();
        using var w = new Google.Protobuf.CodedOutputStream(ms);
        // AnyValue field 1 = stringValue
        w.WriteTag(1, Google.Protobuf.WireFormat.WireType.LengthDelimited);
        w.WriteString(value);
        w.Flush();
        return ms.ToArray();
    }

    private static byte[] BuildScopeLogsBytes(byte[] logRecordBytes)
    {
        using var ms = new System.IO.MemoryStream();
        using var w = new Google.Protobuf.CodedOutputStream(ms);
        // ScopeLogs field 2 = logRecords
        w.WriteTag(2, Google.Protobuf.WireFormat.WireType.LengthDelimited);
        w.WriteBytes(Google.Protobuf.ByteString.CopyFrom(logRecordBytes));
        w.Flush();
        return ms.ToArray();
    }

    private static byte[] BuildResourceLogsBytes(byte[] scopeLogsBytes)
    {
        using var ms = new System.IO.MemoryStream();
        using var w = new Google.Protobuf.CodedOutputStream(ms);
        // ResourceLogs field 2 = scopeLogs
        w.WriteTag(2, Google.Protobuf.WireFormat.WireType.LengthDelimited);
        w.WriteBytes(Google.Protobuf.ByteString.CopyFrom(scopeLogsBytes));
        w.Flush();
        return ms.ToArray();
    }
}

// ── No-alias registry shim ────────────────────────────────────────────────
// Wraps a real registry but intercepts GetByKeyOrNull to return null for
// any old-vocab key. This simulates the post-Task-9 state where the alias
// map is gone, so only new-vocab keys resolve. Used to verify that Task 6's
// decoder changes do not depend on the transitional alias.

internal sealed class NoAliasRegistry : ILogLevelRegistry
{
    private readonly ILogLevelRegistry _inner;

    // Old-vocab keys that will NOT resolve after Task 9 removes the alias.
    private static readonly HashSet<string> OldVocabKeys =
        new(System.StringComparer.OrdinalIgnoreCase) { "info", "warn", "trace", "critical" };

    public NoAliasRegistry(ILogLevelRegistry inner) => _inner = inner;

    public LogLevel? GetByKeyOrNull(string levelKey)
    {
        // Block old-vocab lookups — they must not exist after Task 9.
        if (OldVocabKeys.Contains(levelKey)) return null;
        return _inner.GetByKeyOrNull(levelKey);
    }

    // Delegate all other members to the inner registry.
    public void Register(LogLevel level) => _inner.Register(level);
    public void RegisterBefore(string existingLevelKey, LogLevel level) => _inner.RegisterBefore(existingLevelKey, level);
    public void RegisterAfter(string existingLevelKey, LogLevel level) => _inner.RegisterAfter(existingLevelKey, level);
    public bool Contains(string levelKey) => _inner.Contains(levelKey);
    public bool Contains(LogLevel level) => _inner.Contains(level);
    public int GetRank(LogLevel level) => _inner.GetRank(level);
    public RegisteredLogLevel GetRegisteredLevel(LogLevel level) => _inner.GetRegisteredLevel(level);
    public System.Collections.Generic.IReadOnlyList<RegisteredLogLevel> GetRegisteredLevels() => _inner.GetRegisteredLevels();
    public bool IsAtOrAbove(LogLevel candidate, LogLevel minimum) => _inner.IsAtOrAbove(candidate, minimum);
    public bool IsBelow(LogLevel candidate, LogLevel minimum) => _inner.IsBelow(candidate, minimum);
    public string DumpLevels() => _inner.DumpLevels();
}

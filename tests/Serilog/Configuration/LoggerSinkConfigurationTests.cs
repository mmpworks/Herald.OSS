#nullable enable

using FluentAssertions;
using MMP.Herald.Quick;
using MMP.Herald.Serilog;
using MMP.Herald.Serilog.Configuration;
using MMP.Herald.Serilog.Events;
using Xunit;

namespace MMP.Herald.OSS.Tests.Serilog.Configuration;

/// <summary>
/// T3 — LoggerSinkConfiguration translator parity tests.
///
/// <para>
/// Tests exercise <see cref="LoggerSinkConfiguration"/> directly by constructing
/// a <see cref="LoggerConfiguration"/> shim (Task 2 stub) and comparing the
/// resulting <see cref="QuickLogBuilder.ExportConfigJson"/> output against an
/// equivalent builder built via direct Herald API calls.
/// </para>
///
/// <para>
/// Tests that require the full <c>new LoggerConfiguration().WriteTo.X().CreateLogger()</c>
/// chain (CreateLogger not yet implemented) are skipped until Task 2 lands.
/// </para>
/// </summary>
public sealed class LoggerSinkConfigurationTests
{
    // ── Console ─────────────────────────────────────────────────────────────

    [Fact]
    public void Console_no_restriction_matches_direct_WithConsoleSink()
    {
        var translator = new LoggerConfiguration();
        translator.WriteTo.Console();

        var direct = QuickLogBuilder.Create();
        direct.WithConsoleSink();

        TranslatorParityOracle.AssertSameShape(translator.Builder, direct);
    }

    [Fact]
    public void Console_with_Warning_floor_matches_direct_WithConsoleSink_minLevel_warning()
    {
        var translator = new LoggerConfiguration();
        translator.WriteTo.Console(LogEventLevel.Warning);

        var direct = QuickLogBuilder.Create();
        direct.WithConsoleSink(minLevel: "warning");

        TranslatorParityOracle.AssertSameShape(translator.Builder, direct);
    }

    [Theory]
    [InlineData(LogEventLevel.Debug,       "debug")]
    [InlineData(LogEventLevel.Information, "information")]
    [InlineData(LogEventLevel.Warning,     "warning")]
    [InlineData(LogEventLevel.Error,       "error")]
    [InlineData(LogEventLevel.Fatal,       "fatal")]
    public void Console_non_Verbose_floor_passes_key_to_WithConsoleSink(
        LogEventLevel level, string expectedKey)
    {
        var translator = new LoggerConfiguration();
        translator.WriteTo.Console(level);

        var direct = QuickLogBuilder.Create();
        direct.WithConsoleSink(minLevel: expectedKey);

        TranslatorParityOracle.AssertSameShape(translator.Builder, direct);
    }

    [Fact]
    public void Console_Verbose_floor_passes_null_to_WithConsoleSink()
    {
        // Verbose = "no per-sink restriction" in Serilog. Floor() maps it to null.
        var translator = new LoggerConfiguration();
        translator.WriteTo.Console(LogEventLevel.Verbose);

        var direct = QuickLogBuilder.Create();
        direct.WithConsoleSink(minLevel: null);

        TranslatorParityOracle.AssertSameShape(translator.Builder, direct);
    }

    // ── File ────────────────────────────────────────────────────────────────

    [Fact]
    public void File_text_extension_matches_direct_WithFileSink()
    {
        var translator = new LoggerConfiguration();
        translator.WriteTo.File("app.log");

        var direct = QuickLogBuilder.Create();
        direct.WithFileSink("app.log");

        TranslatorParityOracle.AssertSameShape(translator.Builder, direct);
    }

    [Fact]
    public void File_ndjson_extension_matches_direct_WithFileSink()
    {
        // Herald infers json_file from .ndjson extension — both paths produce same JSON.
        var translator = new LoggerConfiguration();
        translator.WriteTo.File("app.ndjson");

        var direct = QuickLogBuilder.Create();
        direct.WithFileSink("app.ndjson");

        TranslatorParityOracle.AssertSameShape(translator.Builder, direct);
    }

    [Fact]
    public void File_with_Error_floor_matches_direct_WithFileSink_minLevel_error()
    {
        var translator = new LoggerConfiguration();
        translator.WriteTo.File("errors.log", LogEventLevel.Error);

        var direct = QuickLogBuilder.Create();
        direct.WithFileSink("errors.log", minLevel: "error");

        TranslatorParityOracle.AssertSameShape(translator.Builder, direct);
    }

    // ── HTTP ────────────────────────────────────────────────────────────────
    // Herald requires at least one primary sink (console/file/null) when
    // ExportConfigJson() is called. Network sinks (HTTP, TCP, UDP, ES, OTLP)
    // must pair with a primary sink — the validator enforces this by design.
    // Tests below pair the network sink under test with a null sink so the
    // validator is satisfied and the JSON comparison is still meaningful.

    [Fact]
    public void Http_no_restriction_matches_direct_WithHttpJsonSink()
    {
        var translator = new LoggerConfiguration();
        translator.WriteTo.Http("https://logs.example.com/ingest");
        translator.WriteTo.Null(); // satisfy primary-sink requirement

        var direct = QuickLogBuilder.Create();
        direct.WithHttpJsonSink("https://logs.example.com/ingest");
        direct.WithNullSink();

        TranslatorParityOracle.AssertSameShape(translator.Builder, direct);
    }

    [Fact]
    public void Http_with_Warning_floor_matches_direct_WithHttpJsonSink_minLevel_warning()
    {
        var translator = new LoggerConfiguration();
        translator.WriteTo.Http("https://logs.example.com/ingest", LogEventLevel.Warning);
        translator.WriteTo.Null();

        var direct = QuickLogBuilder.Create();
        direct.WithHttpJsonSink("https://logs.example.com/ingest", minLevel: "warning");
        direct.WithNullSink();

        TranslatorParityOracle.AssertSameShape(translator.Builder, direct);
    }

    // ── TCP ─────────────────────────────────────────────────────────────────

    [Fact]
    public void TCPSink_no_restriction_matches_direct_WithTcpJsonLineSink()
    {
        var translator = new LoggerConfiguration();
        translator.WriteTo.TCPSink("logstash.internal", 5000);
        translator.WriteTo.Null();

        var direct = QuickLogBuilder.Create();
        direct.WithTcpJsonLineSink("logstash.internal", 5000);
        direct.WithNullSink();

        TranslatorParityOracle.AssertSameShape(translator.Builder, direct);
    }

    [Fact]
    public void TCPSink_with_Error_floor_matches_direct_WithTcpJsonLineSink_minLevel_error()
    {
        var translator = new LoggerConfiguration();
        translator.WriteTo.TCPSink("logstash.internal", 5000, LogEventLevel.Error);
        translator.WriteTo.Null();

        var direct = QuickLogBuilder.Create();
        direct.WithTcpJsonLineSink("logstash.internal", 5000, minLevel: "error");
        direct.WithNullSink();

        TranslatorParityOracle.AssertSameShape(translator.Builder, direct);
    }

    // ── UDP ─────────────────────────────────────────────────────────────────

    [Fact]
    public void UDPSink_no_restriction_matches_direct_WithUdpJsonLineSink()
    {
        var translator = new LoggerConfiguration();
        translator.WriteTo.UDPSink("syslog.internal", 514);
        translator.WriteTo.Null();

        var direct = QuickLogBuilder.Create();
        direct.WithUdpJsonLineSink("syslog.internal", 514);
        direct.WithNullSink();

        TranslatorParityOracle.AssertSameShape(translator.Builder, direct);
    }

    [Fact]
    public void UDPSink_with_Warning_floor_matches_direct_WithUdpJsonLineSink_minLevel_warning()
    {
        var translator = new LoggerConfiguration();
        translator.WriteTo.UDPSink("syslog.internal", 514, LogEventLevel.Warning);
        translator.WriteTo.Null();

        var direct = QuickLogBuilder.Create();
        direct.WithUdpJsonLineSink("syslog.internal", 514, minLevel: "warning");
        direct.WithNullSink();

        TranslatorParityOracle.AssertSameShape(translator.Builder, direct);
    }

    // ── Elasticsearch ───────────────────────────────────────────────────────

    [Fact]
    public void Elasticsearch_no_restriction_matches_direct_WithElasticsearchSink()
    {
        var translator = new LoggerConfiguration();
        translator.WriteTo.Elasticsearch("https://es.internal:9200");
        translator.WriteTo.Null();

        var direct = QuickLogBuilder.Create();
        direct.WithElasticsearchSink("https://es.internal:9200");
        direct.WithNullSink();

        TranslatorParityOracle.AssertSameShape(translator.Builder, direct);
    }

    [Fact]
    public void Elasticsearch_with_Error_floor_matches_direct_WithElasticsearchSink_minLevel_error()
    {
        var translator = new LoggerConfiguration();
        translator.WriteTo.Elasticsearch("https://es.internal:9200", LogEventLevel.Error);
        translator.WriteTo.Null();

        var direct = QuickLogBuilder.Create();
        direct.WithElasticsearchSink("https://es.internal:9200", minLevel: "error");
        direct.WithNullSink();

        TranslatorParityOracle.AssertSameShape(translator.Builder, direct);
    }

    // ── OpenTelemetry (JSON) ─────────────────────────────────────────────────

    [Fact]
    public void OpenTelemetry_no_restriction_matches_direct_WithOtlpJsonSink()
    {
        // NOTE: Herald uses JSON format. Real Serilog.Sinks.OpenTelemetry defaults to Protobuf.
        var translator = new LoggerConfiguration();
        translator.WriteTo.OpenTelemetry("https://otel.internal:4318/v1/logs");
        translator.WriteTo.Null();

        var direct = QuickLogBuilder.Create();
        direct.WithOtlpJsonSink("https://otel.internal:4318/v1/logs");
        direct.WithNullSink();

        TranslatorParityOracle.AssertSameShape(translator.Builder, direct);
    }

    [Fact]
    public void OpenTelemetry_with_Warning_floor_matches_direct_WithOtlpJsonSink_minLevel_warning()
    {
        var translator = new LoggerConfiguration();
        translator.WriteTo.OpenTelemetry("https://otel.internal:4318/v1/logs", LogEventLevel.Warning);
        translator.WriteTo.Null();

        var direct = QuickLogBuilder.Create();
        direct.WithOtlpJsonSink("https://otel.internal:4318/v1/logs", minLevel: "warning");
        direct.WithNullSink();

        TranslatorParityOracle.AssertSameShape(translator.Builder, direct);
    }

    // ── OpenTelemetry (Protobuf) ─────────────────────────────────────────────

    [Fact]
    public void OpenTelemetryProtobuf_no_restriction_matches_direct_WithOtlpProtobufSink()
    {
        var translator = new LoggerConfiguration();
        translator.WriteTo.OpenTelemetryProtobuf("https://otel.internal:4318/v1/logs");
        translator.WriteTo.Null();

        var direct = QuickLogBuilder.Create();
        direct.WithOtlpProtobufSink("https://otel.internal:4318/v1/logs");
        direct.WithNullSink();

        TranslatorParityOracle.AssertSameShape(translator.Builder, direct);
    }

    // ── Null ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Null_no_restriction_matches_direct_WithNullSink()
    {
        var translator = new LoggerConfiguration();
        translator.WriteTo.Null();

        var direct = QuickLogBuilder.Create();
        direct.WithNullSink();

        TranslatorParityOracle.AssertSameShape(translator.Builder, direct);
    }

    [Fact]
    public void Null_with_Error_floor_matches_direct_WithNullSink_minLevel_error()
    {
        var translator = new LoggerConfiguration();
        translator.WriteTo.Null(LogEventLevel.Error);

        var direct = QuickLogBuilder.Create();
        direct.WithNullSink(minLevel: "error");

        TranslatorParityOracle.AssertSameShape(translator.Builder, direct);
    }

    // ── Chaining ────────────────────────────────────────────────────────────

    [Fact]
    public void WriteTo_returns_LoggerConfiguration_enabling_fluent_chaining()
    {
        // Each sink method must return LoggerConfiguration so chains like
        // .WriteTo.Console().WriteTo.File("x.log") compile and work.
        var config = new LoggerConfiguration();
        var returned = config.WriteTo.Console();
        returned.Should().BeSameAs(config);
    }

    [Fact]
    public void Multiple_sinks_on_same_builder_all_appear_in_exported_json()
    {
        // Chained WriteTo calls accumulate; both sinks must be in the output.
        var translator = new LoggerConfiguration();
        translator.WriteTo.Console();
        translator.WriteTo.Null();

        var direct = QuickLogBuilder.Create();
        direct.WithConsoleSink();
        direct.WithNullSink();

        TranslatorParityOracle.AssertSameShape(translator.Builder, direct);
    }

    // ── CreateLogger integration ─────────────────────────────────────────────

    [Fact]
    public void CreateLogger_with_console_sink_returns_non_null_ILogger()
    {
        // Task 2 landed CreateLogger(). Smoke-test the full chain to confirm
        // WriteTo and CreateLogger() compose without throwing.
        var config = new LoggerConfiguration();
        config.WriteTo.Console();

        var logger = config.CreateLogger();

        logger.Should().NotBeNull("CreateLogger must return a usable ILogger");
    }

    [Fact]
    public void MinimumLevel_Debug_then_WriteTo_Console_does_not_throw()
    {
        // Full chain: MinimumLevel + WriteTo + CreateLogger. If any translation
        // or build step fails this throws before the assertion fires.
        var config = new LoggerConfiguration();
        config.MinimumLevel.Debug();
        config.WriteTo.Console();

        var act = () => config.CreateLogger();

        act.Should().NotThrow("the full MinimumLevel→WriteTo→CreateLogger chain must compose cleanly");
    }
}


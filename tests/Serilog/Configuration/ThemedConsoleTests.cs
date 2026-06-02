#nullable enable

using FluentAssertions;
using MMP.Herald.OSS.Tests.Serilog.TestSupport;
using MMP.Herald.OSS.Tests.TestSupport;
using MMP.Herald.Serilog;
using MMP.Herald.Serilog.Configuration;
using MMP.Herald.Serilog.Events;
using MMP.Herald.Serilog.Formatting;
using Xunit;

namespace MMP.Herald.OSS.Tests.Serilog.Configuration;

/// <summary>
/// W4 — themed/colorizing console formatter.
///
/// <para>
/// Every assertion reads the EMITTED ARTIFACT: the literal bytes the formatter
/// writes into a <see cref="CapturingTextWriter"/>, ANSI escapes and all. The
/// blind spot that shipped this gap was a config-shape test ("a theme is wired")
/// that passes while the renderer emits no colour — these tests look at the ESC
/// bytes instead.
/// </para>
/// </summary>
public sealed class ThemedConsoleTests
{
    // Build one captured native event at the requested level via the adapter, then
    // wrap it in the Serilog mirror the formatter consumes.
    private static LogEvent CaptureMirrorEvent(LogEventLevel level, string template = "themed {Marker}", object value = null!)
    {
        var (herald, sink) = TestLoggers.CreateCapturing(minimumLevel: MMP.Herald.Levels.KnownLogLevels.Verbose);
        var adapter = new SerilogLoggerAdapter(herald);
        adapter.Write(level, template, value ?? "W4-OK");

        var captured = sink.GetEvents();
        captured.Should().HaveCount(1, "the capture helper must produce exactly one native event");
        return new LogEvent(captured[0]);
    }

    // ── A real theme emits ANSI escape bytes ────────────────────────────────────

    [Fact]
    public void Literate_theme_emits_ansi_escape_bytes_for_a_styled_level()
    {
        // emitAnsi:true forces the styling branch regardless of the test runner's
        // redirected stdout — this is the "theme actually colours" artifact.
        var formatter = new ThemedConsoleTextFormatter(ConsoleTheme.Literate, outputTemplate: null, emitAnsi: true);
        var evt = CaptureMirrorEvent(LogEventLevel.Error);
        var writer = new CapturingTextWriter();

        formatter.Format(evt, writer);

        writer.ContainsAnsiEscape.Should().BeTrue(
            "the Literate theme styles the Error level, so the rendered line must carry ANSI escape codes");
        writer.Captured.Should().Contain("W4-OK",
            "the styled line must still contain the rendered message body");
    }

    // ── None theme is byte-identical to the unthemed inner formatter ────────────

    [Fact]
    public void None_theme_is_byte_identical_to_unthemed_formatter()
    {
        var evt = CaptureMirrorEvent(LogEventLevel.Information);

        var themed = new ThemedConsoleTextFormatter(ConsoleTheme.None, outputTemplate: null, emitAnsi: true);
        var inner = new MessageTemplateTextFormatter();

        var themedOut = new CapturingTextWriter();
        var innerOut = new CapturingTextWriter();
        themed.Format(evt, themedOut);
        inner.Format(evt, innerOut);

        themedOut.ContainsAnsiEscape.Should().BeFalse(
            "ConsoleTheme.None resolves no style, so no ANSI escapes may be written");
        themedOut.Captured.Should().Be(innerOut.Captured,
            "ConsoleTheme.None output must be byte-identical to the unthemed MessageTemplateTextFormatter");
    }

    // ── Redirected output suppresses ANSI even with a colourful theme ───────────

    [Fact]
    public void Redirected_output_suppresses_ansi_even_with_a_colourful_theme()
    {
        // emitAnsi:false models Console.IsOutputRedirected == true.
        var formatter = new ThemedConsoleTextFormatter(ConsoleTheme.Literate, outputTemplate: null, emitAnsi: false);
        var evt = CaptureMirrorEvent(LogEventLevel.Error);
        var writer = new CapturingTextWriter();

        formatter.Format(evt, writer);

        writer.ContainsAnsiEscape.Should().BeFalse(
            "when output is redirected, a log file/pipe must receive clean text — no raw ESC sequences");
        writer.Captured.Should().Contain("W4-OK",
            "the message body must still be rendered when ANSI is suppressed");
    }

    // ── The facade maps Serilog names to the engine themes ──────────────────────

    [Fact]
    public void ConsoleTheme_facade_maps_to_distinct_engine_themes()
    {
        ConsoleTheme.Literate.Should().NotBeNull();
        ConsoleTheme.Code.Should().NotBeNull();
        ConsoleTheme.None.Should().BeSameAs(ConsoleTheme.Grayscale,
            "Grayscale and None both resolve to the unstyled engine theme");
    }
}


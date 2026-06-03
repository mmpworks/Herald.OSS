#nullable enable

using System.Collections.Generic;
using FluentAssertions;
using MMP.Herald.OSS.Tests.Serilog.TestSupport;
using MMP.Herald.OSS.Tests.TestSupport;
using MMP.Herald.Serilog;
using MMP.Herald.Serilog.Events;
using MMP.Herald.Serilog.Formatting;
using MMP.Herald.Serilog.Sinks.SystemConsole.Themes;
using Xunit;

namespace MMP.Herald.OSS.Tests.Serilog.Configuration;

/// <summary>
/// FIX 1 — the Serilog-shaped console theme surface a migrated config writes:
/// <c>AnsiConsoleTheme</c>/<c>SystemConsoleTheme</c> named accessors plus the
/// CUSTOM-palette constructors. Every assertion reads the EMITTED bytes (the
/// literal ANSI sequence the formatter writes), not the theme object.
/// </summary>
public sealed class CustomConsoleThemeTests
{
    // Build one captured native event at the requested level, wrapped in the
    // Serilog mirror the formatter consumes. Mirrors ThemedConsoleTests.
    private static LogEvent CaptureMirrorEvent(LogEventLevel level)
    {
        var (herald, sink) = TestLoggers.CreateCapturing(
            minimumLevel: MMP.Herald.Levels.KnownLogLevels.Verbose);
        var adapter = new SerilogLoggerAdapter(herald);
        adapter.Write(level, "themed {Marker}", "FIX1-OK");

        var captured = sink.GetEvents();
        captured.Should().HaveCount(1);
        return new LogEvent(captured[0]);
    }

    // (a) WriteTo.Console(theme: AnsiConsoleTheme.Code) compiles and colourises.
    [Fact]
    public void Named_AnsiConsoleTheme_Code_emits_ansi_for_a_styled_level()
    {
        // The named accessor must satisfy the IConsoleTheme overload (compile proof)
        // AND drive a styled line through the engine colour-name path.
        var formatter = new ThemedConsoleTextFormatter(
            AnsiConsoleTheme.Code, outputTemplate: null, emitAnsi: true);
        var evt = CaptureMirrorEvent(LogEventLevel.Error);
        var writer = new CapturingTextWriter();

        formatter.Format(evt, writer);

        writer.ContainsAnsiEscape.Should().BeTrue(
            "AnsiConsoleTheme.Code styles the Error level via the engine theme");
        writer.Captured.Should().Contain("FIX1-OK");
    }

    // (b) A CUSTOM AnsiConsoleTheme with a distinct LevelError escape emits THAT
    //     exact sequence for an error event.
    [Fact]
    public void Custom_AnsiConsoleTheme_emits_the_operators_exact_LevelError_escape()
    {
        // A deliberately unusual escape so the assertion can only pass if the
        // operator's literal sequence flowed through verbatim.
        const string customErrorEscape = "\x1b[38;5;199m"; // bright magenta (256-colour)
        var theme = new AnsiConsoleTheme(new Dictionary<ConsoleThemeStyle, string>
        {
            [ConsoleThemeStyle.LevelError] = customErrorEscape,
        });

        var formatter = new ThemedConsoleTextFormatter(theme, outputTemplate: null, emitAnsi: true);
        var evt = CaptureMirrorEvent(LogEventLevel.Error);
        var writer = new CapturingTextWriter();

        formatter.Format(evt, writer);

        writer.Captured.Should().StartWith(customErrorEscape,
            "the custom theme's literal LevelError escape must be emitted verbatim before the line");
        writer.Captured.Should().Contain("FIX1-OK");
        writer.Captured.Should().EndWith(ConsoleTheme.AnsiReset,
            "a styled line must be terminated with the ANSI reset");
    }

    // A custom theme styles ONLY the levels it maps. An unmapped level (Information
    // here) carries no custom escape, so the line falls through to the colour-name
    // path (the unstyled engine theme → no escape).
    [Fact]
    public void Custom_AnsiConsoleTheme_leaves_unmapped_levels_escape_free()
    {
        var theme = new AnsiConsoleTheme(new Dictionary<ConsoleThemeStyle, string>
        {
            [ConsoleThemeStyle.LevelError] = "\x1b[38;5;199m",
        });

        var formatter = new ThemedConsoleTextFormatter(theme, outputTemplate: null, emitAnsi: true);
        var evt = CaptureMirrorEvent(LogEventLevel.Information);
        var writer = new CapturingTextWriter();

        formatter.Format(evt, writer);

        writer.ContainsAnsiEscape.Should().BeFalse(
            "Information is not in the custom map, so no escape is emitted for it");
        writer.Captured.Should().Contain("FIX1-OK");
    }

    // (c) Grayscale / None stay escape-free (byte-identical to unthemed).
    [Fact]
    public void Grayscale_AnsiConsoleTheme_is_escape_free()
    {
        var formatter = new ThemedConsoleTextFormatter(
            AnsiConsoleTheme.Grayscale, outputTemplate: null, emitAnsi: true);
        var evt = CaptureMirrorEvent(LogEventLevel.Error);

        var themedOut = new CapturingTextWriter();
        var innerOut = new CapturingTextWriter();
        formatter.Format(evt, themedOut);
        new MessageTemplateTextFormatter().Format(evt, innerOut);

        themedOut.ContainsAnsiEscape.Should().BeFalse(
            "AnsiConsoleTheme.Grayscale resolves no style, so no ANSI escapes may be written");
        themedOut.Captured.Should().Be(innerOut.Captured,
            "Grayscale output must be byte-identical to the unthemed formatter");
    }

    // SystemConsoleTheme custom map: a ConsoleColor foreground for LevelError must
    // render as the matching ANSI foreground escape.
    [Fact]
    public void Custom_SystemConsoleTheme_renders_ConsoleColor_foreground_as_ansi()
    {
        var theme = new SystemConsoleTheme(new Dictionary<ConsoleThemeStyle, SystemConsoleThemeStyle>
        {
            [ConsoleThemeStyle.LevelError] =
                new SystemConsoleThemeStyle { Foreground = System.ConsoleColor.Red },
        });

        var formatter = new ThemedConsoleTextFormatter(theme, outputTemplate: null, emitAnsi: true);
        var evt = CaptureMirrorEvent(LogEventLevel.Error);
        var writer = new CapturingTextWriter();

        formatter.Format(evt, writer);

        // ConsoleColor.Red maps to the bright-red ANSI foreground (91).
        writer.Captured.Should().StartWith("\x1b[91m",
            "ConsoleColor.Red must translate to the ANSI 91 foreground escape");
        writer.Captured.Should().Contain("FIX1-OK");
        writer.Captured.Should().EndWith(ConsoleTheme.AnsiReset);
    }

    // End-to-end migration shape: the exact Serilog call site
    // `WriteTo.Console(theme: AnsiConsoleTheme.Code)` must compile and build a
    // logger. This is the compile proof a migrated config relies on.
    [Fact]
    public void WriteTo_Console_with_AnsiConsoleTheme_compiles_and_builds()
    {
        var logger = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .WriteTo.Console(theme: AnsiConsoleTheme.Code)
            .CreateLogger();

        logger.Should().NotBeNull();
        // A custom palette at the same call site must also compile + build.
        var custom = new LoggerConfiguration()
            .WriteTo.Console(theme: new AnsiConsoleTheme(new Dictionary<ConsoleThemeStyle, string>
            {
                [ConsoleThemeStyle.LevelError] = "[38;5;199m",
            }))
            .CreateLogger();
        custom.Should().NotBeNull();
    }

    // Named SystemConsoleTheme accessor compiles against the overload and colourises.
    [Fact]
    public void Named_SystemConsoleTheme_Colored_emits_ansi_for_a_styled_level()
    {
        var formatter = new ThemedConsoleTextFormatter(
            SystemConsoleTheme.Colored, outputTemplate: null, emitAnsi: true);
        var evt = CaptureMirrorEvent(LogEventLevel.Error);
        var writer = new CapturingTextWriter();

        formatter.Format(evt, writer);

        writer.ContainsAnsiEscape.Should().BeTrue();
        writer.Captured.Should().Contain("FIX1-OK");
    }
}


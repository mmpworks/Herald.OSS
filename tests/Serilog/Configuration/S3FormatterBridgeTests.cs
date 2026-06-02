#nullable enable

using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using MMP.Herald.Serilog;
using MMP.Herald.Serilog.Configuration;
using MMP.Herald.Serilog.Events;
using MMP.Herald.Serilog.Formatting;
using Xunit;

namespace MMP.Herald.OSS.Tests.Serilog.Configuration;

/// <summary>
/// S3 seam: WriteTo.Console(ITextFormatter) bridge tests.
///
/// <para>
/// Verifies that a user-supplied <see cref="ITextFormatter"/> is called with
/// the Serilog-shaped <see cref="LogEvent"/> mirror when events are logged
/// through <c>new LoggerConfiguration().WriteTo.Console(formatter)</c>.
/// </para>
/// </summary>
public sealed class S3FormatterBridgeTests
{
    /// <summary>
    /// Primary contract: the user formatter receives a non-null LogEvent
    /// with the expected level, and is called once per log call.
    /// </summary>
    [Fact]
    public void User_ITextFormatter_receives_mirrored_LogEvent_and_output_is_written()
    {
        // Arrange: a capturing ITextFormatter that records each call.
        var captured = new List<(LogEvent Event, string Output)>();
        var testFormatter = new CapturingTextFormatter(captured);

        // Act: configure LoggerConfiguration with the custom formatter.
        var config = new LoggerConfiguration()
            .WriteTo.Console(testFormatter);

        var logger = config.CreateLogger();
        logger.Information("test {X}", 42);

        // Assert: formatter was called exactly once with a valid LogEvent.
        captured.Should().HaveCount(1,
            "the formatter must be invoked once per logged event");
        var (evt, _) = captured[0];
        evt.Should().NotBeNull();
        evt.Level.Should().Be(LogEventLevel.Information,
            "Information maps directly to Serilog's Information level");
    }

    /// <summary>
    /// Fluency: WriteTo.Console(formatter) returns the same LoggerConfiguration
    /// so callers can chain further calls.
    /// </summary>
    [Fact]
    public void Console_with_formatter_returns_same_LoggerConfiguration_for_chaining()
    {
        var formatter = new NoOpTextFormatter();
        var config = new LoggerConfiguration();

        var returned = config.WriteTo.Console(formatter);

        returned.Should().BeSameAs(config,
            "WriteTo.Console(formatter) must return the root config for fluent chaining");
    }

    /// <summary>
    /// Minimum level: events below the floor do not reach the formatter.
    /// </summary>
    [Fact]
    public void Events_below_minimum_level_do_not_reach_formatter()
    {
        var captured = new List<(LogEvent Event, string Output)>();
        var formatter = new CapturingTextFormatter(captured);

        var config = new LoggerConfiguration()
            .MinimumLevel.Warning()
            .WriteTo.Console(formatter);

        var logger = config.CreateLogger();
        logger.Debug("this is below the floor");

        captured.Should().BeEmpty(
            "Debug events must be filtered out when the minimum level is Warning");
    }

    /// <summary>
    /// The formatter's TextWriter output is used — the captured string
    /// matches what the formatter wrote.
    /// </summary>
    [Fact]
    public void Formatter_output_string_matches_what_formatter_wrote()
    {
        var captured = new List<(LogEvent Event, string Output)>();
        var formatter = new CapturingTextFormatter(captured);

        var config = new LoggerConfiguration()
            .WriteTo.Console(formatter);

        var logger = config.CreateLogger();
        logger.Warning("hello {Name}", "world");

        captured.Should().HaveCount(1);
        captured[0].Output.Should().StartWith("custom:",
            "the formatter must have written output beginning with 'custom:'");
        captured[0].Output.Should().Contain("Warning",
            "the captured output must include the level name produced by the formatter");
    }

    // ── Test doubles ────────────────────────────────────────────────────────

    /// <summary>
    /// Records each Format() call. Writes "custom: {Level}" to the TextWriter.
    /// </summary>
    private sealed class CapturingTextFormatter(
        List<(LogEvent Event, string Output)> captured) : ITextFormatter
    {
        public void Format(LogEvent logEvent, TextWriter output)
        {
            var text = $"custom: {logEvent.Level}";
            output.Write(text);
            captured.Add((logEvent, text));
        }
    }

    /// <summary>No-op formatter used for fluency/level tests that don't need captures.</summary>
    private sealed class NoOpTextFormatter : ITextFormatter
    {
        public void Format(LogEvent logEvent, TextWriter output) { }
    }
}


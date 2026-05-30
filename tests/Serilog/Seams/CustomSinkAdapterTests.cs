#nullable enable

using System;
using System.Collections.Generic;
using FluentAssertions;
using MMP.Herald.Failures;
using MMP.Herald.Serilog.Configuration;
using MMP.Herald.Serilog.Core;
using MMP.Herald.Serilog.Events;
using Xunit;

namespace MMP.Herald.OSS.Tests.Serilog.Seams;

/// <summary>
/// S4 seam: WriteTo.Sink(ILogEventSink) adapter tests.
///
/// <para>
/// Verifies that a user-authored <see cref="ILogEventSink"/> receives mirrored
/// <see cref="LogEvent"/> instances when events are logged through
/// <c>new LoggerConfiguration().WriteTo.Sink(mySink).CreateLogger()</c>.
/// </para>
///
/// <para>
/// <strong>Hard boundary</strong>: WriteTo.Sink() works ONLY for sinks compiled
/// from source against <c>MMP.Herald.Serilog</c>. Pre-compiled community packages
/// (Serilog.Sinks.Seq, .MSSqlServer, .Datadog etc.) were compiled against real
/// Serilog's assembly identity (PublicKeyToken=24c2f752a8e58a10), which this shim
/// does not match. Do not interpret "custom sinks work" as "Seq works."
/// </para>
/// </summary>
public sealed class CustomSinkAdapterTests
{
    /// <summary>
    /// Primary contract: the user sink receives a mirrored LogEvent with the
    /// expected level, and the property captured in the message template is present.
    /// </summary>
    [Fact]
    public void WriteTo_Sink_delivers_mirrored_event_to_user_sink()
    {
        // Arrange
        var received = new List<LogEvent>();
        var recording = new RecordingLogEventSink(received);

        var log = new LoggerConfiguration()
            .WriteTo.Sink(recording)
            .CreateLogger();

        // Act
        log.Information("user {Id} acted", 7);

        // Assert
        var e = Assert.Single(received);
        e.Level.Should().Be(LogEventLevel.Information,
            "Information must map directly to Serilog's Information level");
        e.Properties.Should().ContainKey("Id",
            "the structured property from the message template must be projected into the mirror");
    }

    /// <summary>
    /// Fluency: WriteTo.Sink() returns the same LoggerConfiguration for chaining.
    /// </summary>
    [Fact]
    public void WriteTo_Sink_returns_same_LoggerConfiguration_for_chaining()
    {
        var sink = new RecordingLogEventSink(new List<LogEvent>());
        var config = new LoggerConfiguration();

        var returned = config.WriteTo.Sink(sink);

        returned.Should().BeSameAs(config,
            "WriteTo.Sink must return the root LoggerConfiguration to support fluent chaining");
    }

    /// <summary>
    /// Multiple sinks: all registered user sinks receive every event.
    /// </summary>
    [Fact]
    public void Multiple_user_sinks_all_receive_the_same_event()
    {
        // Arrange: two independent recording sinks on the same builder.
        var received1 = new List<LogEvent>();
        var received2 = new List<LogEvent>();

        var log = new LoggerConfiguration()
            .WriteTo.Sink(new RecordingLogEventSink(received1))
            .WriteTo.Sink(new RecordingLogEventSink(received2))
            .CreateLogger();

        // Act
        log.Warning("fan-out test");

        // Assert: both sinks received the event.
        received1.Should().HaveCount(1, "first sink must receive the event");
        received2.Should().HaveCount(1, "second sink must receive the event");
        received1[0].Level.Should().Be(LogEventLevel.Warning);
        received2[0].Level.Should().Be(LogEventLevel.Warning);
    }

    /// <summary>
    /// Minimum-level gating: events below the pipeline floor never reach the sink.
    /// </summary>
    [Fact]
    public void Events_below_minimum_level_do_not_reach_user_sink()
    {
        var received = new List<LogEvent>();
        var recording = new RecordingLogEventSink(received);

        var log = new LoggerConfiguration()
            .MinimumLevel.Warning()
            .WriteTo.Sink(recording)
            .CreateLogger();

        log.Debug("this is below the floor");

        received.Should().BeEmpty(
            "Debug events must be dropped when the pipeline minimum level is Warning");
    }

    /// <summary>
    /// Exception swallowing: a throwing sink in normal mode does not crash the application
    /// and subsequent sinks still receive the event.
    /// </summary>
    [Fact]
    public void Throwing_sink_in_normal_mode_does_not_prevent_next_sink_from_receiving()
    {
        var received = new List<LogEvent>();

        var log = new LoggerConfiguration()
            .WriteTo.Sink(new ThrowingLogEventSink())    // first: throws
            .WriteTo.Sink(new RecordingLogEventSink(received))  // second: records
            .CreateLogger();

        // Act: must not throw
        var act = () => log.Information("should not crash");
        act.Should().NotThrow("a throwing sink in normal mode must not propagate its exception");

        // The second sink must still have received the event.
        received.Should().HaveCount(1,
            "the recording sink registered after the throwing sink must still receive the event");
    }

    /// <summary>
    /// Audit mode: exceptions from the sink are wrapped in AuditLogFailureException
    /// and propagate so delivery failures surface to the caller.
    ///
    /// The Herald kernel re-throws AuditLogFailureException specifically
    /// (KernelCompiler catch-only-audit rule), so the original InvalidOperationException
    /// from the sink arrives as the InnerException of the wrapper.
    /// </summary>
    [Fact]
    public void Audit_mode_propagates_exception_from_throwing_sink()
    {
        var log = new LoggerConfiguration()
            .WriteTo.Sink(new ThrowingLogEventSink(), auditMode: true)
            .CreateLogger();

        var act = () => log.Information("audit delivery required");
        act.Should().Throw<AuditLogFailureException>(
            "audit mode must wrap sink exceptions in AuditLogFailureException so the Herald " +
            "kernel's catch-only-audit path surfaces the delivery failure");
    }

    /// <summary>
    /// Level mapping: Warning events arrive with LogEventLevel.Warning on the mirror.
    /// </summary>
    [Fact]
    public void Warning_event_arrives_with_Warning_level_on_mirror()
    {
        var received = new List<LogEvent>();

        var log = new LoggerConfiguration()
            .WriteTo.Sink(new RecordingLogEventSink(received))
            .CreateLogger();

        log.Warning("watch out");

        received.Should().HaveCount(1);
        received[0].Level.Should().Be(LogEventLevel.Warning);
    }

    // ── Test doubles ────────────────────────────────────────────────────────

    /// <summary>
    /// Records every emitted <see cref="LogEvent"/> into the supplied list.
    /// </summary>
    private sealed class RecordingLogEventSink(List<LogEvent> received) : ILogEventSink
    {
        public void Emit(LogEvent logEvent) => received.Add(logEvent);
    }

    /// <summary>
    /// Always throws on Emit. Used to verify error-handling semantics.
    /// </summary>
    private sealed class ThrowingLogEventSink : ILogEventSink
    {
        public void Emit(LogEvent logEvent)
            => throw new InvalidOperationException("sink delivery failed");
    }
}

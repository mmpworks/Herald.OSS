#nullable enable
#if NET9_0_OR_GREATER
using FluentAssertions;
using MMP.Herald.OSS.Tests.TestSupport;
using MMP.Herald.Serilog;
using MMP.Herald.Serilog.Events;
using Xunit;

namespace MMP.Herald.OSS.Tests.Serilog;

public sealed class StaticLogFacadeTests
{
    [Fact]
    public void Log_forwards_to_the_assigned_Logger()
    {
        var (herald, sink) = TestLoggers.CreateCapturing<StaticLogFacadeTests>();
        Log.Logger = new SerilogLoggerAdapter(herald);

        Log.Information("hello {X}", 1);
        Log.Fatal("bye");

        var events = sink.GetEvents();
        events[0].Level.Key.Should().Be("information");
        events[1].Level.Key.Should().Be("fatal");
    }

    [Fact]
    public void Log_before_assignment_is_silent_not_null_ref()
    {
        Log.CloseAndFlush(); // reset to unassigned state first
        var act = () => Log.Information("no logger yet");
        act.Should().NotThrow("ambient default is a silent no-op SilentLogger");
    }

    [Fact]
    public void CloseAndFlush_is_idempotent()
    {
        var (herald, _) = TestLoggers.CreateCapturing<StaticLogFacadeTests>();
        Log.Logger = new SerilogLoggerAdapter(herald);

        // Double flush: must not throw, must not double-dispose.
        var act = () => { Log.CloseAndFlush(); Log.CloseAndFlush(); };
        act.Should().NotThrow();

        // After CloseAndFlush, Log is back to silent — calls must not throw.
        var act2 = () => Log.Information("after flush");
        act2.Should().NotThrow();
    }

    [Fact]
    public void Log_ForContext_returns_a_logger_that_adds_context_property()
    {
        var (herald, sink) = TestLoggers.CreateCapturing<StaticLogFacadeTests>();
        Log.Logger = new SerilogLoggerAdapter(herald);

        var contextLog = Log.ForContext("Service", "payments");
        contextLog.Information("request");

        // ForContext properties arrive in LogEvent.Context (the ambient-context
        // dictionary), not LogEvent.Properties (the template-hole bindings).
        var events = sink.GetEvents();
        events[0].Context.Should().ContainKey("Service");
    }
}
#endif

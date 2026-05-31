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

    // CRIT-FM-L2 slot-identity test — deferred to P7 Task 6 corpus test.
    //
    // What it verifies:
    //   Layer-2 (Serilog.Log.Logger) shares the SAME slot as Layer-1
    //   (MMP.Herald.Serilog.Log.Logger). Setting via L1 and reading via L2
    //   must return a wrapper over the identical adapter instance (not a copy).
    //
    // Why it cannot run here:
    //   This test project (Herald.OSS.Tests) references the real Serilog NuGet
    //   package (assembly identity: Serilog, PublicKeyToken=24c2f752a8e58a10).
    //   Adding a ProjectReference to MMP.Herald.Compat.Serilog (which also
    //   produces an assembly named "Serilog") causes an ambiguous-assembly
    //   collision (CS0433 / assembly loader conflict). The two cannot coexist
    //   in one test project.
    //
    // Where it will run:
    //   Task 6 creates a dedicated P7 corpus test project that references ONLY
    //   MMP.Herald.Compat.Serilog (no real Serilog NuGet). That project can
    //   import Serilog.Core.Logger.Inner (internal, granted via InternalsVisibleTo
    //   in the Layer-2 AssemblyInfo) and perform:
    //
    //     var (herald, sink) = TestLoggers.CreateCapturing<SlotIdentityTest>();
    //     var adapter = new MMP.Herald.Serilog.SerilogLoggerAdapter(herald);
    //     MMP.Herald.Serilog.Log.Logger = adapter;              // set via Layer 1
    //     var l2Logger = Serilog.Log.Logger;                    // read via Layer 2
    //     var inner = ((Serilog.Core.Logger)l2Logger).Inner;    // unwrap
    //     Assert.Same(adapter, inner);                          // same slot, same object
    //
    //   Until Task 6 ships, this comment is the tracking artifact.
    //   Tracking: CRIT-FM-L2 / Task 6 / P7-corpus-test project.
}
#endif

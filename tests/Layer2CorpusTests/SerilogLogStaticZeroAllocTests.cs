#nullable enable

// G-OPT2: Verify that Serilog.Log.Information<T1..T4> routes to the zero-alloc
// kernel after Option 2 (lift typed overloads to Serilog.Core.Logger + Serilog.Log).
//
// The test assigns a null-sink SerilogLoggerAdapter to Serilog.Log.Logger, then
// measures the per-call allocation via the per-thread counter.
// 0 B confirms the typed overload (from [OverloadResolutionPriority]) was selected
// and routed through _adapter to SerilogLoggerAdapter.Information<T1..T4> without
// boxing any argument or allocating a params object[] array.

using System;
using FluentAssertions;
using MMP.Herald.Quick;
using MMP.Herald.Serilog;
using Xunit;

// Type aliases matching CorpusTests.cs conventions.
using SerilogLog = Serilog.Log;
using CoreLogger = Serilog.Core.Logger;

namespace MMP.Herald.Compat.Layer2.Tests;

public sealed class SerilogLogStaticZeroAllocTests : IDisposable
{
    private readonly QuickLogResult _result;
    private readonly SerilogLoggerAdapter _adapter;

    public SerilogLogStaticZeroAllocTests()
    {
        _result = QuickLogBuilder.Create()
            .WithNullSink()
            .WithMinimumLevel("verbose")
            .BuildAndCommit();

        _adapter = new SerilogLoggerAdapter(_result.Logger);

        // Assign via Serilog.Log.Logger so the Gate-2 generated statics can reach _adapter.
        SerilogLog.Logger = new CoreLogger(_adapter);
    }

    public void Dispose()
    {
        SerilogLog.CloseAndFlush();
        if (_result.AsyncResource is { } resource)
            resource.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    // ── Zero-alloc probe helper ─────────────────────────────────────────────────
    // Inline because AllocationProbe lives in Herald.OSS.Tests (can't be referenced
    // here without a Serilog NuGet collision). Same warmup/measure logic.
    private static long BytesPerIteration(Action action, int warmup = 2_000, int measured = 100_000)
    {
        for (var i = 0; i < warmup; i++) action();
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < measured; i++) action();
        var total = GC.GetAllocatedBytesForCurrentThread() - before;
        return total < 0 ? 0 : total / measured;
    }

    // ── G-OPT2.1: 4-arg int path through Serilog.Log static facade ─────────────
    // Serilog.Log.Information<T1,T2,T3,T4>(tmpl, a, b, c, d) must bind to the
    // generated typed static (Gate-2 output), NOT the params object[] fallback.
    // After Option 2 this must read 0 B; before Option 2 it would read ~120 B.
    [Fact]
    public void SerilogLog_Information_4int_args_allocates_zero_bytes()
    {
        const string template = "User {Name} from {City} did {Action} on {Resource}";

        var bytes = BytesPerIteration(
            () => SerilogLog.Information(template, 1, 2, 3, 4));

        bytes.Should().Be(0,
            "Serilog.Log.Information<T1,T2,T3,T4> must route to the typed static " +
            "overload via [OverloadResolutionPriority], not the params object[] fallback. " +
            "Non-zero means Option 2 Gate-2 is not wired or _adapter is null.");
    }

    // ── G-OPT2.2: 2-arg string path ─────────────────────────────────────────────
    [Fact]
    public void SerilogLog_Information_2string_args_allocates_zero_bytes()
    {
        const string template = "User {Name} from {City}";

        var bytes = BytesPerIteration(
            () => SerilogLog.Information(template, "alice", "London"));

        bytes.Should().Be(0,
            "2-arg string Information must route to the typed static and box nothing.");
    }

    // ── G-OPT2.3: Core.Logger typed overload ────────────────────────────────────
    // Serilog.Core.Logger.Information<T1,T2>(tmpl, a, b) should also be 0 B when
    // _adapter is non-null (the generated partial class overload calls _adapter directly).
    [Fact]
    public void CoreLogger_Information_2int_args_allocates_zero_bytes()
    {
        var logger = new CoreLogger(_adapter);
        const string template = "Count {A} total {B}";

        var bytes = BytesPerIteration(
            () => logger.Information(template, 7, 42));

        bytes.Should().Be(0,
            "Core.Logger.Information<T1,T2> must forward to _adapter and allocate nothing. " +
            "Non-zero means the generated partial class overload is missing or _adapter is null.");
    }

    // ── G-OPT2.4: SilentLogger path does not throw ──────────────────────────────
    // When Log.Logger holds a SilentLogger (before assignment), typed static calls
    // must be silent, not throw. This exercises the fallback in the generated static.
    [Fact]
    public void SerilogLog_Information_before_assignment_is_silent_not_throwing()
    {
        SerilogLog.CloseAndFlush(); // resets slot to SilentLogger

        var act = () => SerilogLog.Information("silent {A}", 1);

        act.Should().NotThrow(
            "typed static falls through to params-object verb which reaches SilentLogger");

        // Reassign for Dispose.
        SerilogLog.Logger = new CoreLogger(_adapter);
    }
}


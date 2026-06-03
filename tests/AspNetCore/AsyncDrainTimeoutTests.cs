#nullable enable

// AsyncLogger drain-timeout shutdown regression suite (the wedged-sink hang bug).
//
// This is a CLASS of bug living in the SHARED chain AsyncLogger: on shutdown,
// AsyncLogger.DisposeAsync drains the queue by awaiting the background
// processing task. When no drainTimeout was supplied, the constructor used to
// leave _drainTimeout null, so DisposeAsync took the unbounded
// `await _processingTask` branch. A downstream sink wedged inside LogAsync
// never lets that task complete, so host shutdown hung forever.
//
// The fix is a constructor default: _drainTimeout = drainTimeout ??
// DefaultDrainTimeout (5s, mirroring FastPathAsyncSink). Because the default
// lives in the AsyncLogger constructor, EVERY chain-async pipeline inherits the
// bounded wait — both entry points that drain the same chain AsyncResource:
//
//   1. AddHerald (QuickLogResult.DisposeAsync -> AsyncResource.DisposeAsync)
//   2. UseSerilog (SerilogLoggerAdapter.FromBuild -> AsyncResource.DisposeAsync)
//
// The gap was in the shared AsyncLogger, so a test on only one path leaves the
// other unproven. These tests exercise BOTH:
//
//   AddHerald_WedgedSink_DrainIsBoundedByTimeout — the AddHerald/QuickLogResult
//     drain returns inside the 5s timeout against a sink wedged in LogAsync.
//   Serilog_WedgedSink_DrainIsBoundedByTimeout — the same proof on the
//     SerilogLoggerAdapter.FromBuild teardown path.

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MMP.Herald.Events;
using MMP.Herald.Pipeline;
using MMP.Herald.Quick;
using MMP.Herald.Serilog;
using Xunit;

namespace Herald.OSS.Serilog.AspNetCore.Tests;

public sealed class AsyncDrainTimeoutTests
{
    // The unbounded-drain hang only manifests if the wedge outlasts the 5s
    // timeout. We give the drain a hard ceiling well above DefaultDrainTimeout
    // so a genuine regression (an actual hang) FAILS the test instead of
    // hanging the whole CI run. Five seconds of timeout plus headroom for a
    // slow agent.
    private static readonly TimeSpan HardHangCeiling = TimeSpan.FromSeconds(20);

    // The drain must complete close to DefaultDrainTimeout, not instantly. A
    // near-instant return would mean the event was dropped before it reached
    // the wedged sink — which would make the test pass for the wrong reason and
    // never exercise the timeout. This floor proves the timeout actually fired.
    private static readonly TimeSpan TimeoutFiredFloor =
        AsyncLogger.DefaultDrainTimeout - TimeSpan.FromSeconds(2);

    // The drain must return within the timeout plus margin for scheduling and
    // the WhenAny/Delay resolution. This is the must-not-hang assertion.
    private static readonly TimeSpan BoundedCeiling =
        AsyncLogger.DefaultDrainTimeout + TimeSpan.FromSeconds(10);

    // ------------------------------------------------------------------------
    // A sink that wedges inside LogAsync — it blocks on a gate the test owns and
    // ignores the drain's cancellation token, the way a stuck network or disk
    // sink would. The test releases the gate in a finally so the background
    // worker thread is not leaked once the assertion has run.
    // ------------------------------------------------------------------------
    private sealed class WedgedSink : MMP.Herald.ILogger, IDisposable
    {
        private readonly ManualResetEventSlim _gate = new(initialState: false);
        private readonly ManualResetEventSlim _entered = new(initialState: false);

        // Signals that the drain has actually reached the sink. Lets the test
        // confirm the wedge engaged rather than racing the assertion.
        public bool WaitUntilEntered(TimeSpan timeout) => _entered.Wait(timeout);

        public void Log(LogEvent logEvent) => Block();

        public ValueTask LogAsync(LogEvent logEvent, CancellationToken cancellationToken)
        {
            Block();
            return ValueTask.CompletedTask;
        }

        private void Block()
        {
            _entered.Set();
            // No token: a wedged sink does not honour cancellation. The drain
            // timeout — not cooperative cancellation — is what must bound this.
            _gate.Wait();
        }

        // Release the wedge so the worker thread unblocks and exits after the
        // test has measured the (bounded) drain.
        public void Release() => _gate.Set();

        public void Dispose()
        {
            _gate.Set();
            _gate.Dispose();
            _entered.Dispose();
        }
    }

    // Emit one event into an async-buffered pipeline, then measure how long the
    // supplied teardown takes. Shared by both entry-point tests so the timing
    // contract is asserted identically on each surface. Each caller supplies its
    // own idiomatic emit action so the test exercises the real entry surface.
    private static async Task<TimeSpan> MeasureBoundedDrainAsync(
        WedgedSink sink,
        Action emitOne,
        Func<Task> teardown)
    {
        // Push one event through the async queue. The background drain picks it
        // up and wedges inside the sink's LogAsync.
        emitOne();

        // Make sure the wedge actually engaged before we trigger teardown,
        // otherwise the drain could finish before the sink ever blocked.
        sink.WaitUntilEntered(TimeSpan.FromSeconds(5))
            .Should().BeTrue("the drain must reach the wedged sink before shutdown");

        var stopwatch = Stopwatch.StartNew();

        // Run teardown under a hard ceiling. A genuine hang (the pre-fix bug)
        // loses this race and fails the assertion below instead of blocking CI.
        var teardownTask = teardown();
        var winner = await Task.WhenAny(teardownTask, Task.Delay(HardHangCeiling));
        stopwatch.Stop();

        winner.Should().BeSameAs(teardownTask,
            "DisposeAsync must return on its own (bounded by the drain timeout), not hang");

        // Surface any exception the teardown itself threw.
        await teardownTask;

        return stopwatch.Elapsed;
    }

    // ------------------------------------------------------------------------
    // Entry point 1 — AddHerald path: QuickLogResult.DisposeAsync drains the
    // chain AsyncResource. The wedged sink must not hang it.
    // ------------------------------------------------------------------------
    [Fact]
    public async Task AddHerald_WedgedSink_DrainIsBoundedByTimeout()
    {
        using var sink = new WedgedSink();

        var result = QuickLogBuilder.Create()
            .WithBridge(sink)
            .WithAsyncLogging(capacity: 1024)
            .WithMinimumLevel("verbose")
            .BuildAndCommit();

        try
        {
            var elapsed = await MeasureBoundedDrainAsync(
                sink,
                () => result.Logger.Information(LogCategory.None, "wedge the drain"),
                async () => await result.DisposeAsync());

            elapsed.Should().BeLessThan(BoundedCeiling,
                "QuickLogResult.DisposeAsync must be bounded by AsyncLogger.DefaultDrainTimeout, not hang on a wedged sink");
            elapsed.Should().BeGreaterThan(TimeoutFiredFloor,
                "the drain timeout must actually fire — a near-instant return means the event never reached the wedged sink");
        }
        finally
        {
            sink.Release();
        }
    }

    // ------------------------------------------------------------------------
    // Entry point 2 — UseSerilog path: SerilogLoggerAdapter.FromBuild owns the
    // pipeline and drains the SAME chain AsyncResource on DisposeAsync (the path
    // Log.CloseAndFlushAsync drives on ApplicationStopped). The wedged sink must
    // not hang it.
    // ------------------------------------------------------------------------
    [Fact]
    public async Task Serilog_WedgedSink_DrainIsBoundedByTimeout()
    {
        using var sink = new WedgedSink();

        var buildResult = QuickLogBuilder.Create()
            .WithBridge(sink)
            .WithAsyncLogging(capacity: 1024)
            .WithMinimumLevel("verbose")
            .Build();

        var adapter = SerilogLoggerAdapter.FromBuild(buildResult);

        try
        {
            var elapsed = await MeasureBoundedDrainAsync(
                sink,
                () => adapter.Information("wedge the drain"),
                async () => await adapter.DisposeAsync());

            elapsed.Should().BeLessThan(BoundedCeiling,
                "SerilogLoggerAdapter.DisposeAsync must be bounded by AsyncLogger.DefaultDrainTimeout, not hang on a wedged sink");
            elapsed.Should().BeGreaterThan(TimeoutFiredFloor,
                "the drain timeout must actually fire — a near-instant return means the event never reached the wedged sink");
        }
        finally
        {
            sink.Release();
        }
    }
}

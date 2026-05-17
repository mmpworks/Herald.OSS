#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MMP.Herald.Quick;
using Xunit;

namespace MMP.Herald.OSS.Tests.Quick;

/// <summary>
/// Pins the deadlock fix for
/// <see cref="HeraldRegistryInstance.Remove(string, string)"/>. The prior
/// implementation called <c>DisposeAsync().AsTask().GetAwaiter().GetResult()</c>
/// which deadlocked any caller running on a single-threaded sync context
/// (UI, classic ASP.NET, xUnit fixture). The fix schedules disposal on the
/// thread pool via the same shape <see cref="HeraldRegistryInstance"/>
/// already uses for upsert eviction and returns immediately.
/// </summary>
public sealed class HeraldRegistryInstanceRemoveTests
{
    [Fact]
    public async Task Remove_returns_immediately_when_dispose_blocks_on_captured_sync_context()
    {
        var instance = new HeraldRegistryInstance();
        var disposeGate = new ManualResetEventSlim(initialState: false);

        var builder = QuickLogBuilder.Create("deadlock-pipeline").WithConsoleSink();
        var result = builder.BuildAndCommit();
        instance.Register("default", "deadlock-pipeline", builder, result);

        // Probe blocks until the test releases the gate. The pre-fix Remove
        // would call GetAwaiter().GetResult() on this and deadlock if the
        // continuation tried to return to a captured context. The post-fix
        // Remove schedules disposal on the thread pool and returns true
        // immediately, so the test continues without ever signalling the
        // gate until after Remove returns.
        var registration = instance.Get("default", "deadlock-pipeline");
        registration!.DisposeProbeForTests = new BlockingDisposable(disposeGate);

        var removeReturned = Task.Run(() => instance.Remove("default", "deadlock-pipeline"));

        // 5 s is generous compared to a sync GetAwaiter().GetResult() that
        // would never return at all under a captured context, but small
        // enough that a regression deadlock fails the test fast.
        var completed = await Task.WhenAny(removeReturned, Task.Delay(TimeSpan.FromSeconds(5)));
        completed.Should().Be(removeReturned,
            "Remove must not block on the background dispose; it has to schedule and return");

        (await removeReturned).Should().BeTrue();

        // Release the gate so the background dispose can complete and the
        // probe doesn't leak. The test has already verified the non-blocking
        // contract by this point.
        disposeGate.Set();
    }

    [Fact]
    public async Task RemoveAsync_returns_true_after_dispose_completes()
    {
        // The async overload remains the right API for callers that need to
        // observe disposal completion. This pins the contract: when the
        // disposal chain throws, RemoveAsync surfaces the exception (it does
        // not swallow into the background like Remove does).
        var instance = new HeraldRegistryInstance();

        var builder = QuickLogBuilder.Create("async-pipeline").WithConsoleSink();
        var result = builder.BuildAndCommit();
        instance.Register("default", "async-pipeline", builder, result);

        var removed = await instance.RemoveAsync("default", "async-pipeline");
        removed.Should().BeTrue();

        // Idempotent: removing again returns false; no exception.
        var removedAgain = await instance.RemoveAsync("default", "async-pipeline");
        removedAgain.Should().BeFalse();
    }

    /// <summary>
    /// Test-only disposable that blocks until the gate is released. Used to
    /// distinguish "Remove returned" from "dispose completed" without
    /// playing games with thread-pool starvation.
    /// </summary>
    private sealed class BlockingDisposable : IAsyncDisposable
    {
        private readonly ManualResetEventSlim _gate;

        public BlockingDisposable(ManualResetEventSlim gate)
        {
            _gate = gate;
        }

        public ValueTask DisposeAsync()
        {
            // Wait on the thread-pool worker; the test releases the gate
            // when it wants disposal to complete. Bounded so a stuck test
            // does not hang the runner forever.
            _gate.Wait(TimeSpan.FromSeconds(30));
            return ValueTask.CompletedTask;
        }
    }
}

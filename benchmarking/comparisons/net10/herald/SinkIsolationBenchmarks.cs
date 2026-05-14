#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using MMP.Herald.Events;
using MMP.Herald.Quick;

namespace MMP.Herald.OSS.Benchmarks.Comparisons.HeraldRow;

/// <summary>
/// Sink isolation under load: a misbehaving sink should not DoS the
/// rest of the pipeline. The bench wires five bridge sinks and has
/// one of them throw <see cref="InvalidOperationException"/> on every
/// emit. <see cref="MMP.Herald.Pipeline.SafeCompositeLogger"/> is
/// expected to catch the throw, route it through the failure sink,
/// and continue dispatching to the four healthy sinks.
///
/// <para>
/// What "passing" looks like: the healthy sinks each see every event
/// (counter check), no exception escapes to the bench's
/// <c>Logger.Info</c> caller, and the per-emit latency stays within a
/// small multiple of the four-healthy-sinks baseline (no head-of-line
/// blocking from the throwing sink).
/// </para>
///
/// <para>
/// Bridge sinks are not kernel-eligible, so this bench measures the
/// chain path. The throwing isolation guarantee is what's under test,
/// not raw fan-out speed.
/// </para>
/// </summary>
[MemoryDiagnoser]
public class SinkIsolationBenchmarks
{
    private QuickLogResult _allHealthy = null!;
    private QuickLogResult _oneThrowing = null!;
    private CountingLogger _h1 = null!;
    private CountingLogger _h2 = null!;
    private CountingLogger _h3 = null!;
    private CountingLogger _h4 = null!;
    private CountingLogger _h5 = null!;
    private CountingLogger _t1 = null!;
    private CountingLogger _t2 = null!;
    private ThrowingLogger _throwSink = null!;
    private CountingLogger _t4 = null!;
    private CountingLogger _t5 = null!;

    [GlobalSetup]
    public void Setup()
    {
        _h1 = new CountingLogger();
        _h2 = new CountingLogger();
        _h3 = new CountingLogger();
        _h4 = new CountingLogger();
        _h5 = new CountingLogger();

        _allHealthy = QuickLogBuilder.Create()
            .WithBridge(_h1).WithBridge(_h2).WithBridge(_h3).WithBridge(_h4).WithBridge(_h5)
            .WithMinimumLevel("trace")
            .BuildAndCommit();

        _t1 = new CountingLogger();
        _t2 = new CountingLogger();
        _throwSink = new ThrowingLogger();
        _t4 = new CountingLogger();
        _t5 = new CountingLogger();

        _oneThrowing = QuickLogBuilder.Create()
            .WithBridge(_t1).WithBridge(_t2).WithBridge(_throwSink).WithBridge(_t4).WithBridge(_t5)
            .WithMinimumLevel("trace")
            .BuildAndCommit();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        DisposeAsync(_allHealthy);
        DisposeAsync(_oneThrowing);
    }

    [Benchmark(Baseline = true)]
    public void FiveHealthy_AllSinksLand()
    {
        _allHealthy.Logger.Info(LogCategory.App, "tick {N}", 1);
    }

    [Benchmark]
    public void FourHealthyOneThrowing_HealthySinksStillLand()
    {
        _oneThrowing.Logger.Info(LogCategory.App, "tick {N}", 1);
    }

    private static void DisposeAsync(QuickLogResult? r)
    {
        if (r?.AsyncResource is { } resource)
            resource.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    /// <summary>Counts every event it receives. Used to verify healthy sinks still land.</summary>
    private sealed class CountingLogger : ILogger
    {
        public long Count;
        public void Log(LogEvent logEvent) => Interlocked.Increment(ref Count);
        public ValueTask LogAsync(LogEvent logEvent, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Count);
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>Throws on every emit. SafeCompositeLogger must catch.</summary>
    private sealed class ThrowingLogger : ILogger
    {
        public long ThrowCount;
        public void Log(LogEvent logEvent)
        {
            Interlocked.Increment(ref ThrowCount);
            throw new InvalidOperationException("sink #3 says no");
        }
        public ValueTask LogAsync(LogEvent logEvent, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref ThrowCount);
            throw new InvalidOperationException("sink #3 says no");
        }
    }
}

#nullable enable

using BenchmarkDotNet.Attributes;
using MMP.Herald.Events;
using MMP.Herald.Quick;

namespace MMP.Herald.OSS.Benchmarks.Comparisons.HeraldRow;

/// <summary>
/// Herald.OSS accept-path comparison row. Three method shapes —
/// zero / one / four properties — matched across every competitor in
/// `benchmarking/comparisons/net10/` so the comparison is apples-to-
/// apples.
///
/// <para>
/// Sink is <see cref="QuickLogBuilder.WithNullSink"/>, which wires a
/// <see cref="MMP.Herald.Pipeline.NoOpLogger"/> that implements
/// <see cref="MMP.Herald.Pipeline.Kernel.IKernelSink"/>. The kernel
/// fast path discovers the kernel-eligible sink, fans events out
/// through a stack-allocated <see cref="MMP.Herald.Pipeline.Kernel.LogEventBuffer"/>,
/// and skips heap LogEvent materialization entirely. This is the
/// canonical "Herald with its own idiomatic null sink" shape — the
/// fairest analogue to NLog's <c>NullTarget</c>, log4net's
/// <c>NullAppender</c>, etc.
/// </para>
///
/// <para>
/// Earlier iterations of this bench used <c>WithBridge</c> with a
/// custom discarding <c>ILogger</c>. That forces the chain path
/// (LogEvent materialization + decorator traversal + property bag
/// allocation) — not what an adopter would write for "fast discard"
/// and not the comparison Herald should be measured by. The current
/// shape matches what Herald.Core's competitive bench uses.
/// </para>
/// </summary>
[MemoryDiagnoser]
public class AcceptCallBenchmarks
{
    private QuickLogResult _result = null!;

    [GlobalSetup]
    public void Setup()
    {
        _result = QuickLogBuilder.Create()
            .WithNullSink()
            .WithMinimumLevel("trace")
            .BuildAndCommit();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        if (_result.AsyncResource is { } resource)
        {
            resource.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    [Benchmark]
    public void Herald_ZeroProps()
    {
        _result.Logger.Info(LogCategory.App, "accept-zero");
    }

    [Benchmark]
    public void Herald_OneProp()
    {
        _result.Logger.Info(LogCategory.App, "accept-one {Value}", 42);
    }

    [Benchmark]
    public void Herald_TwoProps()
    {
        _result.Logger.Info(
            LogCategory.App,
            "accept-two {A} {B}",
            "alpha", 7);
    }

    [Benchmark]
    public void Herald_FourProps()
    {
        _result.Logger.Info(
            LogCategory.App,
            "accept-four {A} {B} {C} {D}",
            "alpha", 7, true, 3.14);
    }

    [Benchmark]
    public void Herald_EightProps()
    {
        _result.Logger.Info(
            LogCategory.App,
            "accept-eight {A} {B} {C} {D} {E} {F} {G} {H}",
            "alpha", 7, true, 3.14, "beta", 11, false, 2.71);
    }

    [Benchmark]
    public void Herald_SixteenProps()
    {
        _result.Logger.Info(
            LogCategory.App,
            "accept-sixteen {A} {B} {C} {D} {E} {F} {G} {H} {I} {J} {K} {L} {M} {N} {O} {P}",
            "alpha", 7, true, 3.14, "beta", 11, false, 2.71,
            "gamma", 13, true, 1.41, "delta", 17, false, 1.73);
    }
}

#nullable enable

using BenchmarkDotNet.Attributes;
using MMP.Herald.Events;
using MMP.Herald.Quick;

namespace MMP.Herald.OSS.Benchmarks.Comparisons.HeraldRow;

/// <summary>
/// Rejected-call latency: emit events BELOW the pipeline's configured
/// minimum level. Production systems reject 100× more events than they
/// accept — `trace` and `debug` flow through every emit site but are
/// dropped at the floor. The cost of "rejected" therefore matters more
/// than the cost of "accepted" in aggregate.
///
/// <para>
/// Pipeline minimum level is <c>warn</c>. Emits at <c>trace</c>,
/// <c>debug</c>, <c>info</c> are all below the floor and should
/// short-circuit before any work happens. Herald's level-bound fast
/// path is the test target — the structured logger pre-resolves the
/// rank comparison and the rejected call lands at single-digit
/// nanoseconds with zero allocation.
/// </para>
///
/// <para>
/// Competitor rows for this bench come in a later iteration; the
/// Herald row pins the floor and the comparison fills in around it.
/// </para>
/// </summary>
[MemoryDiagnoser]
public class RejectedCallBenchmarks
{
    private QuickLogResult _result = null!;

    [GlobalSetup]
    public void Setup()
    {
        _result = QuickLogBuilder.Create()
            .WithNullSink()
            .WithMinimumLevel("warn")
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
    public void Herald_Rejected_Trace_ZeroProps()
    {
        _result.Logger.Verbose(LogCategory.App, "rejected-trace");
    }

    [Benchmark]
    public void Herald_Rejected_Debug_ZeroProps()
    {
        _result.Logger.Debug(LogCategory.App, "rejected-debug");
    }

    [Benchmark]
    public void Herald_Rejected_Info_ZeroProps()
    {
        _result.Logger.Information(LogCategory.App, "rejected-info");
    }

    [Benchmark]
    public void Herald_Rejected_Debug_OneProp()
    {
        _result.Logger.Debug(LogCategory.App, "rejected-debug-{Value}", 42);
    }

    [Benchmark]
    public void Herald_Rejected_Debug_FourProps()
    {
        _result.Logger.Debug(
            LogCategory.App,
            "rejected-debug-{A}-{B}-{C}-{D}",
            "alpha", 7, true, 3.14);
    }

    [Benchmark(Baseline = true)]
    public void Herald_Accepted_Warn_ZeroProps()
    {
        // Reference accept-path emit so the rejected numbers can be read
        // against an above-floor baseline from the same run.
        _result.Logger.Warning(LogCategory.App, "accepted-warn");
    }
}

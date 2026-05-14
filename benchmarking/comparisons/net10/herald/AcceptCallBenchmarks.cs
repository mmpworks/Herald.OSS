#nullable enable

using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using MMP.Herald.Events;
using MMP.Herald.Quick;

namespace MMP.Herald.OSS.Benchmarks.Comparisons.HeraldRow;

/// <summary>
/// Herald.OSS accept-path comparison row. Three method shapes —
/// zero / one / four properties — matched across every competitor in
/// `benchmarking/comparisons/net10/` so the comparison is apples-to-
/// apples. Sink is a discarding bridge: the per-call cost reported
/// here is "everything Herald does after the caller emits, minus
/// sink work".
/// </summary>
[MemoryDiagnoser]
public class AcceptCallBenchmarks
{
    private QuickLogResult _result = null!;

    [GlobalSetup]
    public void Setup()
    {
        _result = QuickLogBuilder.Create()
            .WithBridge(new DiscardingLogger())
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
    public void Herald_FourProps()
    {
        _result.Logger.Info(
            LogCategory.App,
            "accept-four {A} {B} {C} {D}",
            "alpha", 7, true, 3.14);
    }

    private sealed class DiscardingLogger : ILogger
    {
        public void Log(LogEvent logEvent) { }
        public ValueTask LogAsync(LogEvent logEvent, CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;
    }
}

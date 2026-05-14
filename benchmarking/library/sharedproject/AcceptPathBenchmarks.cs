#nullable enable

using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using MMP.Herald.Events;
using MMP.Herald.Quick;

namespace MMP.Herald.OSS.Benchmarks;

/// <summary>
/// Accept-path cost through a real pipeline. The bridge sink discards
/// every event, so the time measured is the cost of getting from the
/// caller into the bridge — template parsing, level filtering, kernel
/// dispatch, fan-out to one sink.
///
/// Numbers from this bench are the canonical "Herald accepted-call"
/// figures the README quotes.
/// </summary>
[MemoryDiagnoser]
public class AcceptPathBenchmarks
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
    public void Info_no_properties()
    {
        _result.Logger.Info(LogCategory.App, "accept-path-no-props");
    }

    [Benchmark]
    public void Info_with_one_property()
    {
        _result.Logger.Info(LogCategory.App, "accept-path-one-prop {Value}", 42);
    }

    [Benchmark]
    public void Info_with_three_properties()
    {
        _result.Logger.Info(
            LogCategory.App,
            "accept-path-three-props {A} {B} {C}",
            "alpha", 7, true);
    }

    private sealed class DiscardingLogger : ILogger
    {
        public void Log(LogEvent logEvent) { }
        public ValueTask LogAsync(LogEvent logEvent, CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;
    }
}

#nullable enable

using BenchmarkDotNet.Attributes;
using MMP.Herald.Events;
using MMP.Herald.Quick;

namespace MMP.Herald.OSS.Benchmarks;

/// <summary>
/// Accept-path cost through a real pipeline. The sink is
/// <see cref="QuickLogBuilder.WithNullSink"/>, which wires a
/// kernel-eligible <c>NoOpLogger</c>. Events take the kernel fast
/// path: stack-allocated <see cref="MMP.Herald.Pipeline.Kernel.LogEventBuffer"/>,
/// no heap LogEvent materialization, zero allocation for
/// no-properties calls.
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
}

#nullable enable

using BenchmarkDotNet.Attributes;
using NLog;
using NLog.Config;
using NLog.Targets;

namespace MMP.Herald.OSS.Benchmarks.Comparisons.NLogRow;

/// <summary>
/// NLog accept-path comparison row. Matched shape across every
/// competitor in benchmarking/comparisons/net10/. Sink is NLog's
/// built-in <see cref="NullTarget"/>, which skips layout entirely —
/// the cheapest discarding shape NLog ships out of the box.
/// </summary>
[MemoryDiagnoser]
public class AcceptCallBenchmarks
{
    private Logger _logger = null!;

    [GlobalSetup]
    public void Setup()
    {
        var config = new LoggingConfiguration();
        var nullTarget = new NullTarget("null");
        config.AddTarget(nullTarget);
        config.AddRule(LogLevel.Trace, LogLevel.Fatal, nullTarget);
        LogManager.Configuration = config;

        _logger = LogManager.GetLogger("bench");
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        LogManager.Shutdown();
    }

    [Benchmark]
    public void NLog_ZeroProps()
    {
        _logger.Info("accept-zero");
    }

    [Benchmark]
    public void NLog_OneProp()
    {
        _logger.Info("accept-one {Value}", 42);
    }

    [Benchmark]
    public void NLog_FourProps()
    {
        _logger.Info("accept-four {A} {B} {C} {D}", "alpha", 7, true, 3.14);
    }
}

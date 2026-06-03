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
    public void NLog_TwoProps()
    {
        _logger.Info("accept-two {A} {B}", "alpha", 7);
    }

    [Benchmark]
    public void NLog_FourProps()
    {
        _logger.Info("accept-four {A} {B} {C} {D}", "alpha", 7, true, 3.14);
    }

    [Benchmark]
    public void NLog_EightProps()
    {
        _logger.Info(
            "accept-eight {A} {B} {C} {D} {E} {F} {G} {H}",
            "alpha", 7, true, 3.14, "beta", 11, false, 2.71);
    }

    [Benchmark]
    public void NLog_TwelveProps()
    {
        _logger.Info(
            "accept-twelve {A} {B} {C} {D} {E} {F} {G} {H} {I} {J} {K} {L}",
            "alpha", 7, true, 3.14, "beta", 11, false, 2.71,
            "gamma", 13, true, 1.41);
    }

    [Benchmark]
    public void NLog_SixteenProps()
    {
        _logger.Info(
            "accept-sixteen {A} {B} {C} {D} {E} {F} {G} {H} {I} {J} {K} {L} {M} {N} {O} {P}",
            "alpha", 7, true, 3.14, "beta", 11, false, 2.71,
            "gamma", 13, true, 1.41, "delta", 17, false, 1.73);
    }
}

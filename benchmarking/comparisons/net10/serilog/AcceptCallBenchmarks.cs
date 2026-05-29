#nullable enable

using BenchmarkDotNet.Attributes;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace MMP.Herald.OSS.Benchmarks.Comparisons.SerilogRow;

/// <summary>
/// Serilog accept-path comparison row. Matched shape across every
/// competitor in benchmarking/comparisons/net10/. Sink is the no-op
/// SerilogNullSink — Serilog defers rendering to the sink, so a null
/// sink that doesn't format is the closest fair analogue to Herald's
/// discarding bridge.
/// </summary>
[MemoryDiagnoser]
public class AcceptCallBenchmarks
{
    private Logger _logger = null!;

    [GlobalSetup]
    public void Setup()
    {
        _logger = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .WriteTo.Sink(new SerilogNullSink())
            .CreateLogger();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _logger.Dispose();
    }

    [Benchmark]
    public void Serilog_ZeroProps()
    {
        _logger.Information("accept-zero");
    }

    [Benchmark]
    public void Serilog_OneProp()
    {
        _logger.Information("accept-one {Value}", 42);
    }

    [Benchmark]
    public void Serilog_TwoProps()
    {
        _logger.Information("accept-two {A} {B}", "alpha", 7);
    }

    [Benchmark]
    public void Serilog_FourProps()
    {
        _logger.Information("accept-four {A} {B} {C} {D}", "alpha", 7, true, 3.14);
    }

    [Benchmark]
    public void Serilog_EightProps()
    {
        _logger.Information(
            "accept-eight {A} {B} {C} {D} {E} {F} {G} {H}",
            "alpha", 7, true, 3.14, "beta", 11, false, 2.71);
    }

    [Benchmark]
    public void Serilog_SixteenProps()
    {
        _logger.Information(
            "accept-sixteen {A} {B} {C} {D} {E} {F} {G} {H} {I} {J} {K} {L} {M} {N} {O} {P}",
            "alpha", 7, true, 3.14, "beta", 11, false, 2.71,
            "gamma", 13, true, 1.41, "delta", 17, false, 1.73);
    }

    /// <summary>
    /// No-op Serilog sink. Consumes events without rendering or
    /// writing. Serilog ships no public null sink, so the comparison
    /// suite ships this one to keep the row fair.
    /// </summary>
    private sealed class SerilogNullSink : ILogEventSink
    {
        public void Emit(LogEvent logEvent) { }
    }
}

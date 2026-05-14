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
    public void Serilog_FourProps()
    {
        _logger.Information("accept-four {A} {B} {C} {D}", "alpha", 7, true, 3.14);
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

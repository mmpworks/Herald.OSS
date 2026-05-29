#nullable enable

using BenchmarkDotNet.Attributes;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace MMP.Herald.OSS.Benchmarks.Comparisons.SerilogRow;

/// <summary>
/// Serilog rejected-call comparison row. Mirrors Herald's
/// RejectedCallBenchmarks shape: configure the logger with a minimum
/// level of Information so emits at Debug/Trace fall below the floor and
/// are gated out before the sink runs. Production systems reject far
/// more events than they accept, so the gated-call cost is what matters
/// in aggregate.
///
/// <para>
/// Serilog's level gate lives behind <see cref="Logger.Debug(string)"/>
/// — the call checks the configured minimum level and short-circuits
/// when the event is below the floor. Serilog also exposes an explicit
/// <see cref="Logger.IsEnabled"/> guard that real adopters write around
/// expensive argument construction; the *_Guarded variant reflects that
/// realistic usage.
/// </para>
/// </summary>
[MemoryDiagnoser]
public class RejectedCallBenchmarks
{
    private Logger _logger = null!;

    [GlobalSetup]
    public void Setup()
    {
        // Minimum level = Information: Debug and Trace are below the floor.
        _logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Sink(new SerilogNullSink())
            .CreateLogger();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _logger.Dispose();
    }

    [Benchmark]
    public void Serilog_Rejected_Debug_ZeroProps()
    {
        // logger.Debug(...) checks the configured minimum level (Information)
        // and short-circuits because Debug is below the floor.
        _logger.Debug("rejected-debug");
    }

    [Benchmark]
    public void Serilog_Rejected_Debug_OneProp()
    {
        _logger.Debug("rejected-debug-{Value}", 42);
    }

    [Benchmark]
    public void Serilog_Rejected_Debug_FourProps()
    {
        _logger.Debug("rejected-debug-{A}-{B}-{C}-{D}", "alpha", 7, true, 3.14);
    }

    [Benchmark]
    public void Serilog_Rejected_Debug_FourProps_Guarded()
    {
        // Realistic adopter pattern: guard with IsEnabled so the argument
        // array is never built when the level is below the floor.
        if (_logger.IsEnabled(LogEventLevel.Debug))
        {
            _logger.Debug("rejected-debug-{A}-{B}-{C}-{D}", "alpha", 7, true, 3.14);
        }
    }

    [Benchmark(Baseline = true)]
    public void Serilog_Accepted_Warn_ZeroProps()
    {
        // Above-floor reference emit so the rejected numbers read against
        // an accepted baseline from the same run.
        _logger.Warning("accepted-warn");
    }

    /// <summary>
    /// No-op Serilog sink. Consumes events without rendering or writing.
    /// Serilog ships no public null sink, so this row provides one to keep
    /// the comparison fair.
    /// </summary>
    private sealed class SerilogNullSink : ILogEventSink
    {
        public void Emit(LogEvent logEvent) { }
    }
}

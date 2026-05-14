#nullable enable

using BenchmarkDotNet.Attributes;
using log4net;
using log4net.Appender;
using log4net.Core;
using log4net.Repository.Hierarchy;

namespace MMP.Herald.OSS.Benchmarks.Comparisons.Log4NetRow;

/// <summary>
/// log4net accept-path comparison row. Matched shape across every
/// competitor in benchmarking/comparisons/net10/. Sink is a no-op
/// <see cref="AppenderSkeleton"/> override; log4net ships no public
/// NullAppender, so this row provides one to keep the comparison
/// fair (no downstream I/O on any row).
/// </summary>
[MemoryDiagnoser]
public class AcceptCallBenchmarks
{
    private ILog _logger = null!;

    [GlobalSetup]
    public void Setup()
    {
        var hierarchy = (Hierarchy)LogManager.GetRepository();
        hierarchy.Root.Level = Level.All;

        var nullAppender = new NullAppender { Name = "bench-null" };
        nullAppender.ActivateOptions();

        var benchLogger = (log4net.Repository.Hierarchy.Logger)hierarchy.GetLogger("bench-l4n");
        benchLogger.Level = Level.All;
        benchLogger.AddAppender(nullAppender);
        benchLogger.Additivity = false;

        hierarchy.Configured = true;

        _logger = LogManager.GetLogger(typeof(AcceptCallBenchmarks).Assembly, "bench-l4n");
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        LogManager.Shutdown();
    }

    [Benchmark]
    public void Log4Net_ZeroProps()
    {
        _logger.Info("accept-zero");
    }

    [Benchmark]
    public void Log4Net_OneProp()
    {
        // log4net does not support {Placeholder} structured templates; it
        // formats positionally. The row is parameterised the same way an
        // adopter would actually write it.
        _logger.InfoFormat("accept-one {0}", 42);
    }

    [Benchmark]
    public void Log4Net_FourProps()
    {
        _logger.InfoFormat("accept-four {0} {1} {2} {3}", "alpha", 7, true, 3.14);
    }

    /// <summary>
    /// No-op appender. log4net ships no public NullAppender — every
    /// shipped appender writes somewhere — so this row provides one
    /// to keep the comparison terminus identical to the other rows.
    /// </summary>
    private sealed class NullAppender : AppenderSkeleton
    {
        protected override void Append(LoggingEvent loggingEvent) { }
        protected override void Append(LoggingEvent[] loggingEvents) { }
    }
}

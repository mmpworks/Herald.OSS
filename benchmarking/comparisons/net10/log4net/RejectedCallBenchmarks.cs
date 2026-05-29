#nullable enable

using BenchmarkDotNet.Attributes;
using log4net;
using log4net.Appender;
using log4net.Core;
using log4net.Repository.Hierarchy;

namespace MMP.Herald.OSS.Benchmarks.Comparisons.Log4NetRow;

/// <summary>
/// log4net rejected-call comparison row. Mirrors Herald's
/// RejectedCallBenchmarks shape: the logger's level is set to
/// <see cref="Level.Info"/>, so emits at Debug fall below the floor and
/// are gated out before the appender runs.
///
/// <para>
/// log4net's level gate lives behind <see cref="ILog.Debug(object)"/> —
/// the call checks the logger's effective level and short-circuits when
/// the event is below it. log4net also exposes
/// <see cref="ILog.IsDebugEnabled"/>, the canonical guard real adopters
/// write around expensive argument construction; the *_Guarded variant
/// reflects that. Note: log4net's <c>DebugFormat</c> defers the format
/// call until after the level check, but it still boxes value-type
/// arguments into the params array at the call site — so the
/// arity-shaped rows below pay that boxing even when the level rejects,
/// which is the honest cost a log4net adopter incurs without a guard.
/// </para>
/// </summary>
[MemoryDiagnoser]
public class RejectedCallBenchmarks
{
    private ILog _logger = null!;

    [GlobalSetup]
    public void Setup()
    {
        var hierarchy = (Hierarchy)LogManager.GetRepository();
        hierarchy.Root.Level = Level.All;

        var nullAppender = new NullAppender { Name = "bench-null" };
        nullAppender.ActivateOptions();

        var benchLogger = (log4net.Repository.Hierarchy.Logger)hierarchy.GetLogger("bench-l4n-reject");

        // Minimum level = Info: Debug is below the floor.
        benchLogger.Level = Level.Info;
        benchLogger.AddAppender(nullAppender);
        benchLogger.Additivity = false;

        hierarchy.Configured = true;

        _logger = LogManager.GetLogger(typeof(RejectedCallBenchmarks).Assembly, "bench-l4n-reject");
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        LogManager.Shutdown();
    }

    [Benchmark]
    public void Log4Net_Rejected_Debug_ZeroProps()
    {
        // log.Debug(...) checks the logger's effective level (Info) and
        // short-circuits because Debug is below the floor.
        _logger.Debug("rejected-debug");
    }

    [Benchmark]
    public void Log4Net_Rejected_Debug_OneProp()
    {
        // log4net formats positionally, not with {Placeholder} templates.
        // DebugFormat defers the format call past the level check, but the
        // value-type arg is boxed into the params array at the call site.
        _logger.DebugFormat("rejected-debug {0}", 42);
    }

    [Benchmark]
    public void Log4Net_Rejected_Debug_FourProps()
    {
        _logger.DebugFormat("rejected-debug {0} {1} {2} {3}", "alpha", 7, true, 3.14);
    }

    [Benchmark]
    public void Log4Net_Rejected_Debug_FourProps_Guarded()
    {
        // Canonical log4net adopter pattern: guard with IsDebugEnabled so
        // the params array (and its boxing) is never built when Debug is
        // below the floor.
        if (_logger.IsDebugEnabled)
        {
            _logger.DebugFormat("rejected-debug {0} {1} {2} {3}", "alpha", 7, true, 3.14);
        }
    }

    [Benchmark(Baseline = true)]
    public void Log4Net_Accepted_Warn_ZeroProps()
    {
        // Above-floor reference emit so the rejected numbers read against
        // an accepted baseline from the same run.
        _logger.Warn("accepted-warn");
    }

    /// <summary>
    /// No-op appender. log4net ships no public NullAppender, so this row
    /// provides one to keep the comparison terminus identical to the
    /// other rows.
    /// </summary>
    private sealed class NullAppender : AppenderSkeleton
    {
        protected override void Append(LoggingEvent loggingEvent) { }
        protected override void Append(LoggingEvent[] loggingEvents) { }
    }
}

#nullable enable

using BenchmarkDotNet.Attributes;
using NLog;
using NLog.Config;
using NLog.Targets;

namespace MMP.Herald.OSS.Benchmarks.Comparisons.NLogRow;

/// <summary>
/// NLog rejected-call comparison row. Mirrors Herald's
/// RejectedCallBenchmarks shape: the logging rule admits Info..Fatal
/// only, so emits at Debug/Trace fall below the floor and are gated out
/// before the target runs.
///
/// <para>
/// NLog's level gate lives behind <see cref="Logger.Debug(string)"/> —
/// the call checks the logger's effective level (driven by the rule's
/// minimum) and short-circuits when the event is below it. NLog also
/// exposes <see cref="Logger.IsDebugEnabled"/>, a guard real adopters
/// write around expensive argument construction; the *_Guarded variant
/// reflects that realistic usage.
/// </para>
/// </summary>
[MemoryDiagnoser]
public class RejectedCallBenchmarks
{
    private Logger _logger = null!;

    [GlobalSetup]
    public void Setup()
    {
        var config = new LoggingConfiguration();
        var nullTarget = new NullTarget("null");
        config.AddTarget(nullTarget);

        // Minimum level = Info: Debug and Trace are below the floor.
        config.AddRule(LogLevel.Info, LogLevel.Fatal, nullTarget);
        LogManager.Configuration = config;

        _logger = LogManager.GetLogger("bench");
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        LogManager.Shutdown();
    }

    [Benchmark]
    public void NLog_Rejected_Debug_ZeroProps()
    {
        // logger.Debug(...) consults the logger's effective level and
        // short-circuits because the Info-floor rule excludes Debug.
        _logger.Debug("rejected-debug");
    }

    [Benchmark]
    public void NLog_Rejected_Debug_OneProp()
    {
        _logger.Debug("rejected-debug {Value}", 42);
    }

    [Benchmark]
    public void NLog_Rejected_Debug_FourProps()
    {
        _logger.Debug("rejected-debug {A} {B} {C} {D}", "alpha", 7, true, 3.14);
    }

    [Benchmark]
    public void NLog_Rejected_Debug_FourProps_Guarded()
    {
        // Realistic adopter pattern: guard with IsDebugEnabled so the
        // argument array is never built when Debug is below the floor.
        if (_logger.IsDebugEnabled)
        {
            _logger.Debug("rejected-debug {A} {B} {C} {D}", "alpha", 7, true, 3.14);
        }
    }

    [Benchmark(Baseline = true)]
    public void NLog_Accepted_Warn_ZeroProps()
    {
        // Above-floor reference emit so the rejected numbers read against
        // an accepted baseline from the same run.
        _logger.Warn("accepted-warn");
    }
}

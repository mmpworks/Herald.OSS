#nullable enable

using System.IO;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Logging;
using ZLogger;

namespace MMP.Herald.OSS.Benchmarks.Comparisons.ZLoggerRow;

/// <summary>
/// ZLogger rejected-call comparison row. Mirrors Herald's
/// RejectedCallBenchmarks shape: the logger factory sets a minimum level
/// of Information, so <c>ZLogDebug</c> emits fall below the floor and are
/// gated out before the null-stream sink runs.
///
/// <para>
/// ZLogger sits on the Microsoft.Extensions.Logging pipeline. Its level
/// gate lives behind <c>logger.ZLogDebug($"...")</c> — the generated call
/// checks <see cref="ILogger.IsEnabled"/> for the level before
/// constructing the interpolated-string state, so a below-floor Debug
/// emit short-circuits cheaply. The *_Guarded variant adds the explicit
/// <see cref="ILogger.IsEnabled"/> guard a cautious adopter would write.
/// </para>
/// </summary>
[MemoryDiagnoser]
public class RejectedCallBenchmarks
{
    private ILoggerFactory _factory = null!;
    private ILogger _logger = null!;

    [GlobalSetup]
    public void Setup()
    {
        _factory = LoggerFactory.Create(builder =>
        {
            // Minimum level = Information: Debug and Trace are below the floor.
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddZLoggerStream(Stream.Null);
        });
        _logger = _factory.CreateLogger("bench");
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _factory.Dispose();
    }

    [Benchmark]
    public void ZLogger_Rejected_Debug_ZeroProps()
    {
        // ZLogDebug checks IsEnabled(Debug) before building state; the
        // Information floor gates this out.
        _logger.ZLogDebug($"rejected-debug");
    }

    [Benchmark]
    public void ZLogger_Rejected_Debug_OneProp()
    {
        var value = 42;
        _logger.ZLogDebug($"rejected-debug {value}");
    }

    [Benchmark]
    public void ZLogger_Rejected_Debug_FourProps()
    {
        var a = "alpha";
        var b = 7;
        var c = true;
        var d = 3.14;
        _logger.ZLogDebug($"rejected-debug {a} {b} {c} {d}");
    }

    [Benchmark]
    public void ZLogger_Rejected_Debug_FourProps_Guarded()
    {
        // Realistic adopter pattern: explicit IsEnabled guard around the
        // interpolation so no work happens when Debug is below the floor.
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            var a = "alpha";
            var b = 7;
            var c = true;
            var d = 3.14;
            _logger.ZLogDebug($"rejected-debug {a} {b} {c} {d}");
        }
    }

    [Benchmark(Baseline = true)]
    public void ZLogger_Accepted_Warn_ZeroProps()
    {
        // Above-floor reference emit so the rejected numbers read against
        // an accepted baseline from the same run.
        _logger.ZLogWarning($"accepted-warn");
    }
}

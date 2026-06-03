#nullable enable

using System;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Logging;

namespace MMP.Herald.OSS.Benchmarks.Comparisons.MelRow;

/// <summary>
/// Microsoft.Extensions.Logging rejected-call comparison row. Mirrors
/// Herald's RejectedCallBenchmarks shape: the logger factory sets a
/// minimum level of Information, so <c>LogDebug</c> emits fall below the
/// floor and are gated out.
///
/// <para>
/// MEL's level gate is the factory-level filter combined with the
/// logger's <see cref="ILogger.IsEnabled"/> check. <c>LogDebug</c>
/// internally calls <c>IsEnabled(LogLevel.Debug)</c> before formatting,
/// so a below-floor emit short-circuits. The *_Guarded variant adds the
/// explicit <see cref="ILogger.IsEnabled"/> guard real adopters write
/// around expensive argument construction.
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
            builder.ClearProviders();
            builder.AddProvider(new ActiveNullProvider());
        });
        _logger = _factory.CreateLogger("bench");
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _factory.Dispose();
    }

    [Benchmark]
    public void Mel_Rejected_Debug_ZeroProps()
    {
        // LogDebug calls IsEnabled(Debug) before formatting; the
        // Information floor gates this out.
        _logger.LogDebug("rejected-debug");
    }

    [Benchmark]
    public void Mel_Rejected_Debug_OneProp()
    {
        _logger.LogDebug("rejected-debug {Value}", 42);
    }

    [Benchmark]
    public void Mel_Rejected_Debug_TwoProps()
    {
        _logger.LogDebug("rejected-debug {A} {B}", "alpha", 7);
    }

    [Benchmark]
    public void Mel_Rejected_Debug_FourProps()
    {
        _logger.LogDebug("rejected-debug {A} {B} {C} {D}", "alpha", 7, true, 3.14);
    }

    [Benchmark]
    public void Mel_Rejected_Debug_EightProps()
    {
        _logger.LogDebug(
            "rejected-debug {A} {B} {C} {D} {E} {F} {G} {H}",
            "alpha", 7, true, 3.14, "beta", 11, false, 2.71);
    }

    [Benchmark]
    public void Mel_Rejected_Debug_TwelveProps()
    {
        _logger.LogDebug(
            "rejected-debug {A} {B} {C} {D} {E} {F} {G} {H} {I} {J} {K} {L}",
            "alpha", 7, true, 3.14, "beta", 11, false, 2.71,
            "gamma", 13, true, 1.41);
    }

    [Benchmark]
    public void Mel_Rejected_Debug_SixteenProps()
    {
        _logger.LogDebug(
            "rejected-debug {A} {B} {C} {D} {E} {F} {G} {H} {I} {J} {K} {L} {M} {N} {O} {P}",
            "alpha", 7, true, 3.14, "beta", 11, false, 2.71,
            "gamma", 13, true, 1.41, "delta", 17, false, 1.73);
    }

    [Benchmark]
    public void Mel_Rejected_Debug_FourProps_Guarded()
    {
        // Realistic adopter pattern: explicit IsEnabled guard so the
        // params object[] is never built when Debug is below the floor.
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug("rejected-debug {A} {B} {C} {D}", "alpha", 7, true, 3.14);
        }
    }

    [Benchmark(Baseline = true)]
    public void Mel_Accepted_Warn_ZeroProps()
    {
        // Above-floor reference emit so the rejected numbers read against
        // an accepted baseline from the same run.
        _logger.LogWarning("accepted-warn");
    }

    /// <summary>
    /// Active null provider: IsEnabled returns true (so above-floor events
    /// flow through) and the formatter callback runs, discarding the
    /// rendered string. The factory-level minimum filter is what rejects
    /// the below-floor Debug emits before they reach this provider.
    /// </summary>
    private sealed class ActiveNullProvider : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => ActiveNullLogger.Instance;

        public void Dispose() { }

        private sealed class ActiveNullLogger : ILogger
        {
            public static readonly ActiveNullLogger Instance = new();

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                _ = formatter(state, exception);
            }
        }
    }
}

#nullable enable

using System;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Logging;

namespace MMP.Herald.OSS.Benchmarks.Comparisons.MelRow;

/// <summary>
/// Microsoft.Extensions.Logging accept-path comparison row. Matched
/// shape across every competitor in
/// benchmarking/comparisons/net10/. Sink is an "active" null provider
/// — <see cref="IsEnabled"/> returns true and the formatter callback
/// runs (so the template formatter cost is measured), but the rendered
/// string is discarded. This matches what a real sink would do and
/// keeps the comparison fair against libraries that always run their
/// formatter.
/// </summary>
[MemoryDiagnoser]
public class AcceptCallBenchmarks
{
    private ILoggerFactory _factory = null!;
    private ILogger _logger = null!;

    [GlobalSetup]
    public void Setup()
    {
        _factory = LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Trace);
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
    public void Mel_ZeroProps()
    {
        _logger.LogInformation("accept-zero");
    }

    [Benchmark]
    public void Mel_OneProp()
    {
        _logger.LogInformation("accept-one {Value}", 42);
    }

    [Benchmark]
    public void Mel_FourProps()
    {
        _logger.LogInformation("accept-four {A} {B} {C} {D}", "alpha", 7, true, 3.14);
    }

    /// <summary>
    /// Active null provider: IsEnabled returns true (so events flow
    /// through) and the formatter callback runs (so the template
    /// format cost is paid). Discards the rendered string. Matches
    /// Serilog null sink and log4net NullAppender behaviour for fair
    /// cross-library comparison.
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

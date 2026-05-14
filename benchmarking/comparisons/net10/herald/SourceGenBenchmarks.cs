#nullable enable

using System;
using System.IO;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Logging;
using MMP.Herald.Events;
using MMP.Herald.Pipeline;
using MMP.Herald.Quick;
using MEL = Microsoft.Extensions.Logging;
using ZLogger;

namespace MMP.Herald.OSS.Benchmarks.Comparisons.HeraldRow;

/// <summary>
/// Source-gen head-to-head. Three libraries that ship a "declare it
/// once, the generator emits the body" path:
///
/// <list type="bullet">
///   <item><b>Herald <c>[HeraldLog]</c></b> — static partial method.
///       Generator emits a body that checks IsEnabled, then dispatches
///       to <c>StructuredLogger.Log</c> with pre-allocated property
///       array.</item>
///   <item><b>ZLogger <c>[ZLoggerMessage]</c></b> — static partial
///       method. Generator emits a body using ZLogger's source-gen
///       writer that targets the UTF-8 bytes pipeline.</item>
///   <item><b>MEL <c>[LoggerMessage]</c></b> — static partial method.
///       Generator emits a body that constructs a struct state and
///       calls <c>ILogger.Log&lt;TState&gt;</c>.</item>
/// </list>
///
/// <para>
/// All three target the same template
/// <c>"User {UserId} purchased {Sku} for {Price} at {Timestamp}"</c>
/// with the same four typed arguments. Sinks are each library's
/// idiomatic null pattern: Herald uses
/// <see cref="QuickLogBuilder.WithNullSink"/>, ZLogger uses
/// <c>AddZLoggerStream(Stream.Null)</c>, MEL uses the active null
/// provider that runs the formatter callback.
/// </para>
///
/// <para>
/// What we expect: source-gen libraries should be at the bottom of
/// the latency-and-allocation chart for their respective frameworks.
/// Comparing the three pins how each library's source-gen path
/// actually shapes up under matched workload.
/// </para>
/// </summary>
[MemoryDiagnoser]
public class SourceGenBenchmarks
{
    private QuickLogResult _herald = null!;
    private MEL.ILoggerFactory _zloggerFactory = null!;
    private MEL.ILogger<SourceGenBenchmarks> _zlogger = null!;
    private MEL.ILoggerFactory _melFactory = null!;
    private MEL.ILogger<SourceGenBenchmarks> _mel = null!;

    [GlobalSetup]
    public void Setup()
    {
        _herald = QuickLogBuilder.Create()
            .WithNullSink()
            .WithMinimumLevel("trace")
            .BuildAndCommit();

        _zloggerFactory = MEL.LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(MEL.LogLevel.Trace);
            builder.AddZLoggerStream(Stream.Null);
        });
        _zlogger = _zloggerFactory.CreateLogger<SourceGenBenchmarks>();

        _melFactory = MEL.LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(MEL.LogLevel.Trace);
            builder.ClearProviders();
            builder.AddProvider(new ActiveNullProvider());
        });
        _mel = _melFactory.CreateLogger<SourceGenBenchmarks>();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _zloggerFactory.Dispose();
        _melFactory.Dispose();
        if (_herald.AsyncResource is { } resource)
            resource.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    [Benchmark(Baseline = true)]
    public void Herald_HeraldLog()
    {
        BenchMessages.Purchase(_herald.Logger, 42, "alpha", 9.99, "2026-05-14T19-30Z");
    }

    [Benchmark]
    public void ZLogger_ZLoggerMessage()
    {
        BenchMessages.PurchaseZ(_zlogger, 42, "alpha", 9.99, "2026-05-14T19-30Z");
    }

    [Benchmark]
    public void Mel_LoggerMessage()
    {
        BenchMessages.PurchaseMel(_mel, 42, "alpha", 9.99, "2026-05-14T19-30Z");
    }

    /// <summary>MEL active-null provider: formatter runs, output discarded.</summary>
    private sealed class ActiveNullProvider : MEL.ILoggerProvider
    {
        public MEL.ILogger CreateLogger(string categoryName) => ActiveNullLogger.Instance;
        public void Dispose() { }
        private sealed class ActiveNullLogger : MEL.ILogger
        {
            public static readonly ActiveNullLogger Instance = new();
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(MEL.LogLevel logLevel) => true;
            public void Log<TState>(MEL.LogLevel logLevel, MEL.EventId eventId,
                TState state, Exception? exception,
                Func<TState, Exception?, string> formatter) { _ = formatter(state, exception); }
        }
    }
}

/// <summary>
/// Source-gen declarations live in a separate class so the generators
/// only have to find one type per library to extend.
/// </summary>
public static partial class BenchMessages
{
    // Herald source-gen via [HeraldLog]
    [HeraldLog(Level = "info", Category = "App",
        Message = "User {userId} purchased {sku} for {price} at {timestamp}")]
    public static partial void Purchase(
        StructuredLogger logger,
        int userId, string sku, double price, string timestamp);

    // ZLogger source-gen via [ZLoggerMessage]
    [ZLoggerMessage(MEL.LogLevel.Information,
        "User {userId} purchased {sku} for {price} at {timestamp}")]
    public static partial void PurchaseZ(
        MEL.ILogger logger,
        int userId, string sku, double price, string timestamp);

    // MEL source-gen via [LoggerMessage]
    [MEL.LoggerMessage(Level = MEL.LogLevel.Information,
        Message = "User {userId} purchased {sku} for {price} at {timestamp}")]
    public static partial void PurchaseMel(
        MEL.ILogger logger,
        int userId, string sku, double price, string timestamp);
}

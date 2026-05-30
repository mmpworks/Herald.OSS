#nullable enable

using System;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Logging;
using MMP.Herald.Addons.MelAdapter;
using MMP.Herald.Events;
using MMP.Herald.Quick;
using MEL = Microsoft.Extensions.Logging;

namespace MMP.Herald.OSS.Benchmarks.Comparisons.HeraldRow;

/// <summary>
/// MEL-adapter overhead: how much does it cost to log through
/// Herald when the call site holds <see cref="MEL.ILogger{T}"/>
/// (the DI default) instead of Herald's native StructuredLogger?
///
/// <para>
/// Three shapes on the same 4-property Info call:
/// </para>
/// <list type="bullet">
///   <item><b>Herald native</b>: calls <c>StructuredLogger.Info</c>
///       directly. The reference number.</item>
///   <item><b>Herald via MEL adapter</b>: wraps StructuredLogger in
///       <see cref="HeraldLoggerProvider"/>, hands out an
///       <c>ILogger&lt;T&gt;</c>, and emits through it. Measures the
///       adapter's per-call tax — state extraction, level mapping,
///       formatter invocation.</item>
///   <item><b>MEL active null provider</b>: the bare MEL pipeline
///       with the active null provider (formatter runs, output
///       discarded). The baseline for "what does ILogger&lt;T&gt;
///       cost without Herald in the picture?"</item>
/// </list>
///
/// <para>
/// The bench answers the architect's question: "does adopting
/// Herald regress my <c>ILogger&lt;T&gt;</c> story?" If the adapter
/// delta is small, callers can keep their existing MEL injection
/// pattern and back it with Herald's pipeline.
/// </para>
/// </summary>
[MemoryDiagnoser]
public class MelAdapterBenchmarks
{
    private QuickLogResult _heraldNative = null!;
    private HeraldLoggerProvider _heraldMelProvider = null!;
    private MEL.ILogger<BenchTarget> _heraldViaMel = null!;
    private MEL.ILoggerFactory _melActiveNullFactory = null!;
    private MEL.ILogger<BenchTarget> _melActiveNull = null!;

    [GlobalSetup]
    public void Setup()
    {
        _heraldNative = QuickLogBuilder.Create()
            .WithNullSink()
            .WithMinimumLevel("trace")
            .BuildAndCommit();

        _heraldMelProvider = new HeraldLoggerProvider(_heraldNative.Logger);
        _heraldViaMel = _heraldMelProvider.CreateLogger<BenchTarget>();

        _melActiveNullFactory = MEL.LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(MEL.LogLevel.Trace);
            builder.ClearProviders();
            builder.AddProvider(new ActiveNullProvider());
        });
        _melActiveNull = _melActiveNullFactory.CreateLogger<BenchTarget>();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _heraldMelProvider.Dispose();
        _melActiveNullFactory.Dispose();
        if (_heraldNative.AsyncResource is { } resource)
            resource.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    [Benchmark(Baseline = true)]
    public void Herald_Native_FourProps()
    {
        _heraldNative.Logger.Information(LogCategory.App,
            "user {Id} purchased {Sku} for {Price} at {Time}",
            42, "alpha", 9.99, "now");
    }

    [Benchmark]
    public void Herald_Via_Mel_Adapter_FourProps()
    {
        _heraldViaMel.LogInformation(
            "user {Id} purchased {Sku} for {Price} at {Time}",
            42, "alpha", 9.99, "now");
    }

    [Benchmark]
    public void Mel_Native_Active_Null_FourProps()
    {
        _melActiveNull.LogInformation(
            "user {Id} purchased {Sku} for {Price} at {Time}",
            42, "alpha", 9.99, "now");
    }

    /// <summary>Bench target — only used to type the generic ILogger&lt;T&gt;.</summary>
    private sealed class BenchTarget { }

    /// <summary>
    /// MEL active-null provider — IsEnabled returns true, formatter
    /// callback runs, output discarded. Matches the MEL comparison
    /// row in <c>AcceptCallBenchmarks</c>.
    /// </summary>
    private sealed class ActiveNullProvider : MEL.ILoggerProvider
    {
        public MEL.ILogger CreateLogger(string categoryName) => ActiveNullLogger.Instance;
        public void Dispose() { }

        private sealed class ActiveNullLogger : MEL.ILogger
        {
            public static readonly ActiveNullLogger Instance = new();
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(MEL.LogLevel logLevel) => true;
            public void Log<TState>(
                MEL.LogLevel logLevel, MEL.EventId eventId,
                TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                _ = formatter(state, exception);
            }
        }
    }
}

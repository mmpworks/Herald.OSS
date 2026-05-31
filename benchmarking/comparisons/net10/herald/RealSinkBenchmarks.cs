#nullable enable

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using MMP.Herald;
using MMP.Herald.Configuration.Runtime;
using MMP.Herald.Events;
using MMP.Herald.Levels;
using MMP.Herald.Output.Rendering;
using MMP.Herald.Pipeline.Kernel;
using MMP.Herald.Quick;
using MMP.Herald.Routing;

namespace MMP.Herald.OSS.Benchmarks.Comparisons.HeraldRow;

/// <summary>
/// Settles the "does the interceptor dispatch gap survive a real sink?" question.
///
/// <para>
/// Three 4-property emit shapes against three sink configurations:
/// </para>
/// <list type="bullet">
///   <item><b>NullSink</b> — kernel-eligible no-op, the structural floor. The
///         current AcceptCall headline measurement.</item>
///   <item><b>MemorySink</b> — kernel-eligible custom sink that increments an
///         atomic counter on every emit. No allocation, no I/O, but real
///         memory traffic. Represents the minimum work a real sink does.</item>
///   <item><b>FileSink</b> — Herald's built-in file sink with text formatting
///         and real disk I/O. Represents the upper bound of common sink cost.</item>
/// </list>
///
/// <para>
/// The interpretation matters: if the per-emit deltas across these three rows
/// are small (sink cost dominates), then any further interceptor-dispatch
/// optimization is rounding error in production. If the deltas are large
/// (interceptor cost is visible even against real sinks), then dispatch
/// optimization translates to real consumer perf gains.
/// </para>
/// </summary>
[MemoryDiagnoser]
public class RealSinkBenchmarks
{
    private QuickLogResult _nullSink = null!;
    private QuickLogResult _memorySink = null!;
    private QuickLogResult _fileSink = null!;
    private string _filePath = null!;
    private CounterKernelSink _counterSink = null!;

    [GlobalSetup]
    public void Setup()
    {
        _nullSink = QuickLogBuilder.Create()
            .WithNullSink()
            .WithMinimumLevel("trace")
            .BuildAndCommit();

        _counterSink = new CounterKernelSink();
        _memorySink = QuickLogBuilder.Create()
            .WithCustomSinkProvider(new CounterKernelSinkProvider(_counterSink))
            .WithMinimumLevel("trace")
            .BuildAndCommit();

        _filePath = Path.Combine(
            Path.GetTempPath(),
            $"herald-bench-{Guid.NewGuid():N}.log");
        _fileSink = QuickLogBuilder.Create()
            .WithFileSink(_filePath)
            .WithMinimumLevel("trace")
            .BuildAndCommit();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        DisposeAsync(_nullSink);
        DisposeAsync(_memorySink);
        DisposeAsync(_fileSink);
        try { File.Delete(_filePath); } catch { /* best effort */ }
    }

    [Benchmark(Baseline = true)]
    public void Herald_FourProps_NullSink()
    {
        _nullSink.Logger.Information(
            LogCategory.App,
            "accept-four {A} {B} {C} {D}",
            "alpha", 7, true, 3.14);
    }

    [Benchmark]
    public void Herald_FourProps_MemorySink()
    {
        _memorySink.Logger.Information(
            LogCategory.App,
            "accept-four {A} {B} {C} {D}",
            "alpha", 7, true, 3.14);
    }

    [Benchmark]
    public void Herald_FourProps_FileSink()
    {
        _fileSink.Logger.Information(
            LogCategory.App,
            "accept-four {A} {B} {C} {D}",
            "alpha", 7, true, 3.14);
    }

    private static void DisposeAsync(QuickLogResult? r)
    {
        if (r?.AsyncResource is { } resource)
            resource.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    /// <summary>
    /// Minimum-work kernel-eligible sink — atomic counter increment, no
    /// allocation, no I/O. Represents what every real sink at least does:
    /// touch its own state per event.
    /// </summary>
    private sealed class CounterKernelSink : ILogger, IKernelSink
    {
        private long _count;

        public long Count => Interlocked.Read(ref _count);

        public void Log(in LogEventBuffer buffer)
            => Interlocked.Increment(ref _count);

        public ValueTask LogAsync(in LogEventBuffer buffer, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _count);
            return ValueTask.CompletedTask;
        }

        public void Log(LogEvent logEvent)
            => Interlocked.Increment(ref _count);

        public ValueTask LogAsync(LogEvent logEvent, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _count);
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>
    /// Provider wiring the counter sink into a Herald pipeline via
    /// <see cref="QuickLogBuilder.WithCustomSinkProvider(ILogSinkProvider)"/>.
    /// Ignores the framework-supplied definition / registries — the
    /// bench wants a pre-constructed sink instance, not a re-resolved one.
    /// </summary>
    private sealed class CounterKernelSinkProvider : ILogSinkProvider
    {
        private readonly CounterKernelSink _sink;

        public CounterKernelSinkProvider(CounterKernelSink sink) => _sink = sink;

        public string SinkKind => "counter";

        public ILogger CreateSink(
            LoggingRuntimeSinkDefinition definition,
            ILogLevelRegistry levelRegistry,
            ILogOutputTransformerRegistry transformerRegistry)
            => _sink;
    }
}

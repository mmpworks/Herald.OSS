#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using MMP.Herald.Configuration;
using MMP.Herald.Configuration.Runtime;
using MMP.Herald.Events;
using MMP.Herald.Levels;
using MMP.Herald.Output.Rendering;
using MMP.Herald.Pipeline.Kernel;
using MMP.Herald.Quick;
using MMP.Herald.Routing;

namespace MMP.Herald.OSS.Benchmarks.Comparisons.HeraldRow;

/// <summary>
/// Hot-reload cutover behaviour with in-flight events. The
/// already-shipped <c>hot-reload-net10</c> bench measures
/// <c>Reload(json)</c> wall time in isolation. This bench adds the
/// integration shape an operator actually cares about: when emits
/// are interleaved with the reload call, are any lost or
/// duplicated?
///
/// <para>
/// Shape: a counting kernel sink wired in via
/// <c>WithCustomSinkProvider</c> override of the "null" kind. The
/// counter is the single source of truth — every event the
/// pipeline accepts increments it. The bench emits N events,
/// calls Reload, then emits N more; if the pipeline drops or
/// duplicates anything across the swap, the post-bench count
/// disagrees with the expected total.
/// </para>
///
/// <para>
/// The reported latency is the wall time of the full sequence —
/// pre-emits + Reload + post-emits — so the per-iteration number
/// includes both the steady-state cost and the cutover. The
/// <c>Reload_Alone</c> baseline isolates the reload's own cost
/// for comparison.
/// </para>
/// </summary>
[MemoryDiagnoser]
public class HotReloadCutoverBenchmarks
{
    private QuickLogResult _herald = null!;
    private CountingKernelSink _counter = null!;
    private string _altConfigA = null!;
    private string _altConfigB = null!;
    private bool _flip;

    private const int EmitsBeforeReload = 4;
    private const int EmitsAfterReload = 4;

    [GlobalSetup]
    public void Setup()
    {
        _counter = new CountingKernelSink();

        _herald = QuickLogBuilder.Create()
            .WithCustomSinkProvider(new CounterOverrideProvider(_counter))
            .WithPipelineStrategy(
                PipelineStrategy.Create().Swappable().FanOut())
            .WithHotReload()
            .WithNullSink()
            .WithMinimumLevel("trace")
            .BuildAndCommit();

        _altConfigA = QuickLogBuilder.Create()
            .WithPipelineStrategy(PipelineStrategy.Create().Swappable().FanOut())
            .WithHotReload()
            .WithNullSink()
            .WithMinimumLevel("debug")
            .Build().ExportConfig();

        _altConfigB = QuickLogBuilder.Create()
            .WithPipelineStrategy(PipelineStrategy.Create().Swappable().FanOut())
            .WithHotReload()
            .WithNullSink()
            .WithMinimumLevel("trace")
            .Build().ExportConfig();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        // Print the final counter state so a reviewer can compare
        // against the expected event count and verify no loss /
        // duplication across reload cutovers. BDN runs many
        // iterations of each benchmark method; the expected total
        // is iterations × (EmitsBeforeReload + EmitsAfterReload).
        Console.WriteLine($"[HotReloadCutover] Counter sink received {_counter.Count} events total.");
        if (_herald.AsyncResource is { } resource)
            resource.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    /// <summary>Baseline: Reload only, no surrounding emits.</summary>
    [Benchmark(Baseline = true)]
    public void Reload_Alone()
    {
        _herald.HotReloadBootstrap!.Reload(_flip ? _altConfigA : _altConfigB);
        _flip = !_flip;
    }

    /// <summary>
    /// Emits-then-reload-then-emits: the realistic cutover shape.
    /// Every emit must land on the counter sink — pre-reload emits
    /// on the old pipeline, post-reload emits on the new pipeline
    /// (which has the same sink wired). If the swap drops or
    /// duplicates anything, the per-iteration accepted count
    /// diverges from the expected 8.
    /// </summary>
    [Benchmark]
    public void Reload_With_Interleaved_Emits()
    {
        for (var i = 0; i < EmitsBeforeReload; i++)
            _herald.Logger.Information(LogCategory.App, "pre-reload {Index}", i);

        _herald.HotReloadBootstrap!.Reload(_flip ? _altConfigA : _altConfigB);
        _flip = !_flip;

        for (var i = 0; i < EmitsAfterReload; i++)
            _herald.Logger.Information(LogCategory.App, "post-reload {Index}", i);
    }

    /// <summary>
    /// Kernel-eligible counting sink. The pipeline can take the
    /// kernel fast path through this sink, so emits add to the
    /// counter without heap LogEvent materialization.
    /// </summary>
    private sealed class CountingKernelSink
        : MMP.Herald.ILogger, IKernelSink
    {
        public long Count;
        public void Log(in LogEventBuffer buffer) => Interlocked.Increment(ref Count);
        public void Log(LogEvent logEvent) => Interlocked.Increment(ref Count);
        public ValueTask LogAsync(LogEvent logEvent, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Count);
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>
    /// Overrides the "null" sink kind so <c>WithNullSink</c> resolves
    /// to the counting sink. Same trick the Utf8 bench uses to inject
    /// a custom kernel-eligible sink without a new builder method.
    /// </summary>
    private sealed class CounterOverrideProvider : ILogSinkProvider
    {
        private readonly MMP.Herald.ILogger _sink;
        public CounterOverrideProvider(MMP.Herald.ILogger sink) => _sink = sink;
        public string SinkKind => Services.KnownSinkKinds.Null;
        public MMP.Herald.ILogger CreateSink(
            LoggingRuntimeSinkDefinition definition,
            ILogLevelRegistry levelRegistry,
            ILogOutputTransformerRegistry transformerRegistry) => _sink;
    }
}

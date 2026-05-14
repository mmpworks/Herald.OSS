#nullable enable

using BenchmarkDotNet.Attributes;
using MMP.Herald.Configuration;
using MMP.Herald.Quick;

namespace MMP.Herald.OSS.Benchmarks.Comparisons.HeraldRow;

/// <summary>
/// Hot-reload cost: time to change pipeline configuration at runtime
/// without a process restart. Two paths in the coordinator:
///
/// <list type="bullet">
///   <item><b>Fast path</b>: only the minimum level changed. The
///       coordinator updates the runtime LogLevelSwitch in place and
///       returns. No pipeline rebuild, no allocation, no in-flight
///       event impact.</item>
///   <item><b>Slow path</b>: anything else changed (strategy, sink
///       set, enricher chain). The coordinator builds a new inner
///       pipeline and atomically swaps it into the SwappableLogger.
///       In-flight events on the old pipeline complete on the old
///       pipeline; new events go to the new one.</item>
/// </list>
///
/// <para>
/// This bench pins both. Competitor comparison comes later;
/// Serilog's <c>LoggingLevelSwitch</c> is the closest analogue
/// (O(1) atomic), but no peer library ships JSON-driven runtime
/// pipeline swap.
/// </para>
///
/// <para>
/// <b>Allocation note.</b> The slow path materially allocates per
/// call — it rebuilds the pipeline. The reported "per-call
/// allocation" represents one full pipeline construction, not
/// per-event allocation in production. The fast path stays
/// allocation-free.
/// </para>
/// </summary>
[MemoryDiagnoser]
public class HotReloadBenchmarks
{
    private QuickLogResult _result = null!;
    private string _configInfoLevel = null!;
    private string _configDebugLevel = null!;
    private string _configWarnLevel = null!;
    private bool _flip;

    [GlobalSetup]
    public void Setup()
    {
        // Build a swappable pipeline that owns a HotReloadBootstrap.
        _result = QuickLogBuilder.Create()
            .WithPipelineStrategy(
                PipelineStrategy.Create().Swappable().FanOut())
            .WithHotReload()
            .WithNullSink()
            .WithMinimumLevel("info")
            .BuildAndCommit();

        // Capture three JSON shapes that differ only in minimum level.
        // Reloading between these triggers the fast path (level-only
        // delta, in-place LogLevelSwitch update).
        _configInfoLevel = QuickLogBuilder.Create()
            .WithPipelineStrategy(PipelineStrategy.Create().Swappable().FanOut())
            .WithHotReload()
            .WithNullSink()
            .WithMinimumLevel("info")
            .Build().ExportConfig();

        _configDebugLevel = QuickLogBuilder.Create()
            .WithPipelineStrategy(PipelineStrategy.Create().Swappable().FanOut())
            .WithHotReload()
            .WithNullSink()
            .WithMinimumLevel("debug")
            .Build().ExportConfig();

        _configWarnLevel = QuickLogBuilder.Create()
            .WithPipelineStrategy(PipelineStrategy.Create().Swappable().FanOut())
            .WithHotReload()
            .WithNullSink()
            .WithMinimumLevel("warn")
            .Build().ExportConfig();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        if (_result.AsyncResource is { } resource)
        {
            resource.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    /// <summary>
    /// Fast path: reload between two configs that differ only in
    /// minimum level. The coordinator detects the level-only delta
    /// and updates the live LogLevelSwitch in place — no rebuild,
    /// no allocation.
    /// </summary>
    [Benchmark]
    public void Herald_HotReload_FastPath_LevelOnly()
    {
        // Alternate between debug and warn each iteration so the
        // coordinator always has a real change to apply.
        var next = _flip ? _configDebugLevel : _configWarnLevel;
        _flip = !_flip;
        _result.HotReloadBootstrap!.Reload(next);
    }

    /// <summary>
    /// Slow path: reload the same config we're already running.
    /// The coordinator still detects this as "not level-only"
    /// (the diff is empty but the coordinator doesn't fast-path
    /// no-op deltas) and rebuilds the inner pipeline. Measures
    /// the floor cost of a structural rebuild.
    /// </summary>
    [Benchmark]
    public void Herald_HotReload_SlowPath_NoChange()
    {
        _result.HotReloadBootstrap!.Reload(_configInfoLevel);
    }
}

#nullable enable

using BenchmarkDotNet.Attributes;
using MMP.Herald.Configuration;
using MMP.Herald.Events;
using MMP.Herald.Quick;

namespace MMP.Herald.OSS.Benchmarks.Comparisons.HeraldRow;

/// <summary>
/// Flight-recorder cost shapes. <c>FlightRecorderLogger</c> sits
/// outside the level filter and captures below-floor events in a
/// ring buffer; on a trigger-level event (default <c>error</c>) the
/// buffer drains to the inner pipeline before the trigger event
/// itself.
///
/// <para>
/// Two shapes that matter:
/// </para>
/// <list type="bullet">
///   <item><b>Buffer-write cost</b>: a below-floor event lands in
///       the ring buffer. Measures the steady-state cost of a
///       "captured but not delivered" emit — the price the pipeline
///       pays for keeping the trail.</item>
///   <item><b>Trigger-flush cost</b>: an at-or-above-trigger event
///       fires the dump. Measures the latency cost of draining a
///       full 200-event buffer to the inner sinks. This is the
///       single-event cost when a real error fires.</item>
/// </list>
///
/// <para>
/// Pipeline shape: <c>WithFlightRecorder(bufferSize: 200,
/// triggerLevel: "error")</c> + <c>WithNullSink()</c> +
/// <c>WithMinimumLevel("warn")</c>. The null sink discards both
/// the trigger event and the dump-flushed events, so the bench
/// isolates the recorder's own cost.
/// </para>
///
/// <para>
/// No peer library ships an equivalent feature. Flight recorder is
/// a Herald-only capability; the benchmark exists so adopters can
/// reason about the cost of turning it on.
/// </para>
/// </summary>
[MemoryDiagnoser]
public class FlightRecorderBenchmarks
{
    private QuickLogResult _result = null!;
    private QuickLogResult _baseline = null!;

    [GlobalSetup]
    public void Setup()
    {
        // Pipeline with the recorder: below-warn events go into the
        // 200-slot ring buffer; error and above trigger the dump.
        _result = QuickLogBuilder.Create()
            .WithPipelineStrategy(
                PipelineStrategy.Create().FlightRecorder().Filtering().FanOut())
            .WithNullSink()
            .WithFlightRecorder(bufferSize: 200, minLevel: null, triggerLevel: "error")
            .WithMinimumLevel("warn")
            .BuildAndCommit();

        // Reference baseline without the recorder, same minimum level.
        // Used to read the recorder's overhead vs an equivalent
        // pipeline that just drops below-warn events.
        _baseline = QuickLogBuilder.Create()
            .WithNullSink()
            .WithMinimumLevel("warn")
            .BuildAndCommit();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        DisposeAsync(_result);
        DisposeAsync(_baseline);
    }

    /// <summary>
    /// Below-floor emit — lands in the ring buffer. Steady-state cost
    /// of "captured but not yet delivered." Bench iterates many of
    /// these per measurement window, so the buffer wraps continuously.
    /// </summary>
    [Benchmark]
    public void Recorder_BufferWrite_DebugBelowFloor()
    {
        _result.Logger.Debug(LogCategory.App, "below-floor {N}", 1);
    }

    /// <summary>
    /// Reference: same below-floor emit on a pipeline without the
    /// recorder. The level filter drops it immediately. Reading the
    /// delta between this and BufferWrite shows the recorder's
    /// steady-state cost when nothing's triggering.
    /// </summary>
    [Benchmark(Baseline = true)]
    public void Baseline_RejectedAtFilter_DebugBelowFloor()
    {
        _baseline.Logger.Debug(LogCategory.App, "below-floor {N}", 1);
    }

    /// <summary>
    /// Trigger emit. The buffer holds events from earlier
    /// BufferWrite iterations (BDN runs iterations back-to-back),
    /// so this measurement includes draining whatever is currently
    /// buffered. The dump latency therefore depends on prior bench
    /// state — not a steady number, but representative of "error
    /// fires; how long does the dump take?"
    /// </summary>
    [Benchmark]
    public void Recorder_TriggerDump_ErrorFlushes()
    {
        _result.Logger.Error(LogCategory.App, "trigger {N}", 1);
    }

    private static void DisposeAsync(QuickLogResult? r)
    {
        if (r?.AsyncResource is { } resource)
            resource.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}

#nullable enable

using System.Collections.Generic;
using MMP.Herald.Configuration;
using MMP.Herald.Failures;
using MMP.Herald.Filters;

namespace MMP.Herald.Pipeline.StepHandlers;

/// <summary>
/// Handler for <see cref="PipelineStep.FanOut"/>. FanOut is the terminal
/// step — the routed sinks sit at the bottom of the chain before any
/// decorator wraps them. No work needed during assembly; the builder is
/// already primed with the sinks.
/// </summary>
internal sealed class FanOutStepHandler : IPipelineStepHandler
{
    public string StepName => PipelineStep.FanOut.Name;
    public void Apply(PipelineStepApplyContext context) { /* terminal */ }
}

/// <summary>
/// Handler for <see cref="PipelineStep.Swappable"/>. Wraps the pipeline in a
/// hot-reloadable container when the policy enables it.
/// </summary>
internal sealed class SwappableStepHandler : IPipelineStepHandler
{
    public string StepName => PipelineStep.Swappable.Name;
    public void Apply(PipelineStepApplyContext context) =>
        context.Builder.WithSwappable(context.Policy.HotReloadEnabled);
}

/// <summary>
/// Handler for <see cref="PipelineStep.Async"/>. Wires the async queue with
/// the configured policy.
/// </summary>
internal sealed class AsyncStepHandler : IPipelineStepHandler
{
    public string StepName => PipelineStep.Async.Name;
    public void Apply(PipelineStepApplyContext context) =>
        context.Builder.WithAsync(
            context.Policy.AsyncPolicy,
            context.FailureSink,
            context.Policy.DropSink);
}

/// <summary>
/// Handler for <see cref="PipelineStep.Rendering"/>. Only emits the
/// decorator when deferred rendering is active — otherwise rendering
/// happened at event-factory time.
/// </summary>
internal sealed class RenderingStepHandler : IPipelineStepHandler
{
    public string StepName => PipelineStep.Rendering.Name;
    public void Apply(PipelineStepApplyContext context)
    {
        if (context.UseDeferredRendering)
        {
            context.Builder.WithDeferredRendering(context.TemplateParser);
        }
    }
}

/// <summary>
/// Handler for <see cref="PipelineStep.Batching"/>. Wires the batching
/// decorator with the configured policy.
/// </summary>
internal sealed class BatchingStepHandler : IPipelineStepHandler
{
    public string StepName => PipelineStep.Batching.Name;
    public void Apply(PipelineStepApplyContext context) =>
        context.Builder.WithBatching(context.Policy.BatchingPolicy, context.FailureSink);
}

/// <summary>
/// Handler for <see cref="PipelineStep.Filtering"/>. Wraps the pipeline in
/// a <see cref="FilteringLogger"/> and registers it with the pipeline
/// accessor so the management API can enumerate it.
/// </summary>
internal sealed class FilteringStepHandler : IPipelineStepHandler
{
    public string StepName => PipelineStep.Filtering.Name;
    public void Apply(PipelineStepApplyContext context)
    {
        // Policy.DropSink carries the bootstrap-provided IPipelineDropSink
        // (normally the LogMetricsRegistry). Threading it here is what makes
        // filter-side rejections land in the Prometheus exposition with a
        // reason tag; without the sink the FilteringLogger silently defaults
        // to NullPipelineDropSink and drops go uncounted, matching the old
        // behaviour before Prompt H.
        var filteringLogger = new FilteringLogger(
            context.Builder.CurrentPipeline,
            context.Filters,
            context.Policy.DropSink);
        context.Builder.SetPipeline(filteringLogger);
        context.PipelineAccessor?.Register(filteringLogger);
    }
}

/// <summary>
/// Handler for <see cref="PipelineStep.PostFiltering"/>. Installs the
/// post-filter batch logger only when the policy defines one.
/// </summary>
internal sealed class PostFilteringStepHandler : IPipelineStepHandler
{
    public string StepName => PipelineStep.PostFiltering.Name;
    public void Apply(PipelineStepApplyContext context)
    {
        var policy = context.Policy.PostFilteringPolicy;
        if (policy is null) return;

        var postFilteringLogger = new PostFilteringBatchLogger(
            context.Builder.CurrentPipeline,
            policy.BatchFilter,
            context.FailureSink,
            policy.MaxBatchSize,
            policy.MaxBatchDelayMs);
        context.Builder.SetPipeline(postFilteringLogger);
        context.Builder.TrackAsyncResource(postFilteringLogger);
        context.PipelineAccessor?.Register(postFilteringLogger);
    }
}

/// <summary>
/// Handler for the <c>eventProcessing</c> pipeline step. Wires the event
/// processing decorator only when the policy ships at least one processor.
/// EventProcessing is a Community-tier step — the logger ships in Core and
/// no license check fires on use.
/// </summary>
internal sealed class EventProcessingStepHandler : IPipelineStepHandler
{
    public string StepName => "eventProcessing";
    public void Apply(PipelineStepApplyContext context)
    {
        var processors = context.Policy.EventProcessors;
        if (processors is not { Count: > 0 }) return;

        // Policy.DropSink carries the bootstrap-provided IPipelineDropSink so
        // any redaction-rule dropEvent lands in the Prometheus exposition with
        // a `redaction` reason tag. Mirrors how FilteringLogger gets its
        // dropSink — without the wire-up drops still happen, they just go
        // uncounted (NullPipelineDropSink fallback).
        var processingLogger = new EventProcessingLogger(
            context.Builder.CurrentPipeline,
            processors,
            context.Policy.DropSink);
        context.Builder.SetPipeline(processingLogger);
        context.PipelineAccessor?.Register(processingLogger);
    }
}

/// <summary>
/// Handler for the <c>flightRecorder</c> pipeline step. Installs the ring
/// buffer when <see cref="LogPipelinePolicy.FlightRecorderPolicy"/> is set;
/// otherwise leaves the step structural so a strategy that lists
/// flightRecorder without configuring it stays a no-op. FlightRecorder is a
/// Community-tier step — game/sim consumers get the recorder for free.
/// </summary>
internal sealed class FlightRecorderStepHandler : IPipelineStepHandler
{
    public string StepName => "flightRecorder";
    public void Apply(PipelineStepApplyContext context)
    {
        var policy = context.Policy.FlightRecorderPolicy;
        if (policy is null) return;

        var recorder = new Addons.GamePerformance.FlightRecorderLogger(
            context.Builder.CurrentPipeline,
            context.LevelRegistry,
            policy.MinimumLevel,
            policy.TriggerLevel,
            policy.BufferSize);
        context.Builder.SetPipeline(recorder);
        context.PipelineAccessor?.Register(recorder);
    }
}

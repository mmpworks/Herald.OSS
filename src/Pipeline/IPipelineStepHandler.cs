#nullable enable

using System.Collections.Generic;
using MMP.Herald.Configuration;
using MMP.Herald.Failures;
using MMP.Herald.Filters;
using MMP.Herald.Levels;
using MMP.Herald.Templating;

namespace MMP.Herald.Pipeline;

/// <summary>
/// A handler that knows how to apply a single named pipeline step to a
/// <see cref="PipelineAssemblyBuilder"/>. Built-in steps (async, batching,
/// filtering, swappable, event-processing, post-filtering, rendering,
/// flight-recorder, fan-out) each ship with their own handler; plugins add
/// their own via <see cref="PipelineStepHandlerRegistry.Register"/>.
///
/// <para>
/// Keeps the pipeline factory's main loop to a single registry lookup
/// instead of the long <c>if</c>-ladder that used to make adding a new step
/// kind mean editing a central file. Matches the "registry over switch"
/// principle the rest of the codebase already follows for sinks, enrichers,
/// and event processors.
/// </para>
/// </summary>
public interface IPipelineStepHandler
{
    /// <summary>
    /// Step name this handler services. Matches <see cref="PipelineStep.Name"/>
    /// case-insensitively via the registry's lookup comparer.
    /// </summary>
    string StepName { get; }

    /// <summary>
    /// Apply this step's decorator (if any) onto the pipeline under
    /// construction. The handler may mutate
    /// <see cref="PipelineStepApplyContext.Builder"/>, register the new
    /// decorator with <see cref="PipelineStepApplyContext.PipelineAccessor"/>,
    /// or short-circuit when the policy says this step is a no-op.
    /// </summary>
    void Apply(PipelineStepApplyContext context);
}

/// <summary>
/// Context carrier passed to every <see cref="IPipelineStepHandler.Apply"/>
/// call. A readonly struct so the dispatch loop pays no allocation per step.
/// </summary>
public readonly struct PipelineStepApplyContext
{
    public PipelineStepApplyContext(
        PipelineStep step,
        PipelineAssemblyBuilder builder,
        LogPipelinePolicy policy,
        IReadOnlyList<ILogFilter> filters,
        ILogFailureSink failureSink,
        bool useDeferredRendering,
        MessageTemplateParser templateParser,
        PipelineAccessor? pipelineAccessor,
        ILogLevelRegistry levelRegistry)
    {
        Step = step;
        Builder = builder;
        Policy = policy;
        Filters = filters;
        FailureSink = failureSink;
        UseDeferredRendering = useDeferredRendering;
        TemplateParser = templateParser;
        PipelineAccessor = pipelineAccessor;
        LevelRegistry = levelRegistry;
    }

    /// <summary>The step being applied.</summary>
    public PipelineStep Step { get; }

    /// <summary>The in-progress pipeline builder.</summary>
    public PipelineAssemblyBuilder Builder { get; }

    /// <summary>The owning pipeline policy, for feature flags and sub-policies.</summary>
    public LogPipelinePolicy Policy { get; }

    /// <summary>Filters resolved from the policy plus the level registry.</summary>
    public IReadOnlyList<ILogFilter> Filters { get; }

    /// <summary>Failure sink for decorators that need to emit diagnostics.</summary>
    public ILogFailureSink FailureSink { get; }

    /// <summary>True when deferred rendering is active (async + deferRendering).</summary>
    public bool UseDeferredRendering { get; }

    /// <summary>Shared template parser — used by rendering handlers.</summary>
    public MessageTemplateParser TemplateParser { get; }

    /// <summary>Optional accessor that tracks built decorators for management API surfaces.</summary>
    public PipelineAccessor? PipelineAccessor { get; }

    /// <summary>Level registry — needed by handlers that wrap a level-aware decorator.</summary>
    public ILogLevelRegistry LevelRegistry { get; }
}

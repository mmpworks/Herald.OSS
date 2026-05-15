#nullable enable

using System;
using System.Collections.Generic;
using MMP.Herald.Configuration;
using MMP.Herald.Enrichers;
using MMP.Herald.Events;
using MMP.Herald.Failures;
using MMP.Herald.Filters;
using MMP.Herald.Levels;
using MMP.Herald.Pipeline.Kernel;
using MMP.Herald.Routing;
using MMP.Herald.Templating;
using MMP.Herald.Time;

namespace MMP.Herald.Pipeline;

/// <summary>
/// Applies top-level pipeline policy declaratively.
/// Uses PipelineAssemblyBuilder to eliminate async/sync branching.
/// </summary>
public sealed class DefaultLogPipelineFactory : ILogPipelineFactory
{
    private readonly ILogScopeProvider _scopeProvider;
    private readonly ILogEnricher _enricher;
    private readonly bool _includeCallerInfo;
    private readonly DestructuringPolicyRegistry? _destructuringPolicies;

    public DefaultLogPipelineFactory(
        ILogScopeProvider? scopeProvider = null,
        ILogEnricher? enricher = null,
        bool includeCallerInfo = false,
        bool includeActivityContext = false,
        DestructuringPolicyRegistry? destructuringPolicies = null)
    {
        _scopeProvider = scopeProvider ?? new AsyncLocalLogScopeProvider();
        _includeCallerInfo = includeCallerInfo;
        _enricher = enricher ?? BuildDefaultEnricher(includeActivityContext);
        _destructuringPolicies = destructuringPolicies;
    }

    private static ILogEnricher BuildDefaultEnricher(bool includeActivityContext)
    {
        var enrichers = new List<ILogEnricher>
        {
            new MachineNameLogEnricher(),
            new ProcessIdLogEnricher(),
            new ThreadIdLogEnricher()
        };

        if (includeActivityContext)
        {
            enrichers.Add(new ActivityEnricher());
        }

        return new CompositeLogEnricher([.. enrichers]);
    }

    // Explicit interface implementation delegates to the accessor-aware overload.
    LoggerComposition ILogPipelineFactory.Create(
        ILogger routedSinks, IDateTimeProvider dateTimeProvider,
        ILogLevelRegistry levelRegistry, ILogFailureSink failureSink,
        LogPipelinePolicy policy) =>
        Create(routedSinks, dateTimeProvider, levelRegistry, failureSink, policy, null);

    public LoggerComposition Create(
        ILogger routedSinks,
        IDateTimeProvider dateTimeProvider,
        ILogLevelRegistry levelRegistry,
        ILogFailureSink failureSink,
        LogPipelinePolicy policy,
        PipelineAccessor? pipelineAccessor) =>
        Create(routedSinks, dateTimeProvider, levelRegistry, failureSink, policy,
            pipelineAccessor, referenceSource: null);

    /// <summary>
    /// Bootstrap-aware overload: when <paramref name="referenceSource"/> is
    /// non-null, the LogEventFactory is constructed with that token so every
    /// chain-path event carries the same provenance stamp the sink-side
    /// gates accept. The StructuredLogger inherits the same token via
    /// <see cref="ILogEventFactory.GenSource"/> and uses it on kernel-path
    /// buffers. Null falls back to the factory's auto-generated token —
    /// preserves test / legacy behaviour.
    /// </summary>
    public LoggerComposition Create(
        ILogger routedSinks,
        IDateTimeProvider dateTimeProvider,
        ILogLevelRegistry levelRegistry,
        ILogFailureSink failureSink,
        LogPipelinePolicy policy,
        PipelineAccessor? pipelineAccessor,
        string? referenceSource)
    {
        ArgumentNullException.ThrowIfNull(routedSinks);
        ArgumentNullException.ThrowIfNull(dateTimeProvider);
        ArgumentNullException.ThrowIfNull(levelRegistry);
        ArgumentNullException.ThrowIfNull(failureSink);
        ArgumentNullException.ThrowIfNull(policy);

        var strategy = policy.Strategy ?? PipelineStrategy.Default();
        var templateParser = new MessageTemplateParser(_destructuringPolicies);
        // Deferred rendering used to require async, but the decorator (RenderingLogger)
        // works fine on a sync pipeline — it just runs on the calling thread when async
        // is off. The win then is rejection-late savings and zero render cost for sinks
        // that don't need the formatted string (JSON, OTLP, null).
        var useDeferredRendering = policy.AsyncPolicy?.DeferRendering ?? false;

        ILogEventFactory eventFactory = useDeferredRendering
            ? new DeferredLogEventFactory(dateTimeProvider, _scopeProvider, _enricher,
                genSource: referenceSource)
            : new LogEventFactory(dateTimeProvider, templateParser, _scopeProvider, _enricher,
                genSource: referenceSource);

        // Sink-chain introspection: if every sink declares a minimum level, the
        // top-level filter can raise its floor to the lowest declared sink min.
        // Events below that floor would have been dropped at the sinks anyway,
        // so gating early in StructuredLogger.IsEnabled avoids event construction
        // cost on the reject path. Only applied when no DynamicLevelPolicy is
        // configured — dynamic policies expect runtime level escalation to
        // propagate, and clamping statically would silently override the switch.
        var effectiveMinimum = ComputeEffectiveMinimum(levelRegistry, policy, routedSinks);

        // Build the chain bottom-up: start from sinks, wrap decorators in reverse strategy order.
        // The last step in the strategy (FanOut) maps to the routed sinks.
        // Each step wraps the current pipeline with its decorator.

        var filters = BuildFilters(levelRegistry, policy, effectiveMinimum);

        ILogger pipeline = routedSinks;
        var builder = new PipelineAssemblyBuilder(pipeline, pipelineAccessor);

        // Walk the strategy in reverse (bottom-up assembly).
        // Each step name dispatches to a handler. Built-in steps have inline handlers;
        // unrecognized steps fall through to the custom decorator lookup.
        var steps = strategy.Steps;
        for (var i = steps.Count - 1; i >= 0; i--)
        {
            var step = steps[i];
            ApplyStep(step, builder, policy, filters, failureSink, useDeferredRendering, templateParser, pipelineAccessor, levelRegistry);
        }

        // Kernel fast path: compile a direct buffer-dispatching kernel when
        // the configuration qualifies. StructuredLogger takes the kernel and
        // uses it on the common call path; anything the kernel can't handle
        // falls through to the decorator chain built above, unchanged.
        var kernel = TryCompileKernel(routedSinks, policy, useDeferredRendering,
            templateParser, out var kernelDiagnostic);

        var built = builder.Build(
            eventFactory, _scopeProvider, _includeCallerInfo,
            levelRegistry, effectiveMinimum,
            kernel, kernel is null ? null : dateTimeProvider);

        return built with { KernelDiagnostic = kernelDiagnostic };
    }

    // Eligibility + compilation gate. Any structural mismatch returns null
    // and the caller keeps the existing decorator chain. No behavior change
    // when ineligible. Always populates <paramref name="diagnostic"/> so the
    // caller can surface kernel-eligibility state on QuickLogResult even
    // when the kernel ultimately falls back to chain mode.
    private LogKernel? TryCompileKernel(
        ILogger routedSinks,
        LogPipelinePolicy policy,
        bool useDeferredRendering,
        MessageTemplateParser templateParser,
        out KernelDiagnostic diagnostic)
    {
        if (useDeferredRendering)
        {
            const string reason = "deferred rendering enabled";
            KernelIntrospection.RecordRejection(reason);
            diagnostic = new KernelDiagnostic(KernelEligible: false, RejectionReason: reason,
                LegacySinks: System.Array.Empty<LegacySinkInfo>());
            return null;
        }

        if (routedSinks is not SafeCompositeLogger composite)
        {
            var reason = $"routedSinks is {routedSinks.GetType().Name}, expected SafeCompositeLogger";
            KernelIntrospection.RecordRejection(reason);
            diagnostic = new KernelDiagnostic(KernelEligible: false, RejectionReason: reason,
                LegacySinks: System.Array.Empty<LegacySinkInfo>());
            return null;
        }
        var children = composite.Children;

        // Partition sinks so the eligibility check below sees a uniform
        // IKernelSink list. Sinks that already implement IKernelSink pass
        // through; legacy sinks get wrapped in MaterializingKernelSink so
        // the kernel can still fan out to them. The wrap costs more per
        // emit than a native kernel sink, but it lets a single legacy sink
        // co-exist with kernel-native sinks instead of vetoing the entire
        // pipeline back to the chain path.
        var kernelReady = PartitionSinks(children, templateParser, out var legacySinks);

        // Enricher classification — three outcomes:
        //   (a) no enrichers at all (NullLogEnricher or empty Composite) → eligible, no kernel enricher list
        //   (b) composite where every inner enricher implements IKernelEnricher → eligible, pass list to compiler
        //   (c) anything else (unknown enricher type, or composite with non-kernel enrichers) → forces chain
        var enrichers = ClassifyEnrichers(_enricher);
        if (enrichers is null)
        {
            const string reason = "enrichers present (not all IKernelEnricher)";
            KernelIntrospection.RecordRejection(reason);
            diagnostic = new KernelDiagnostic(KernelEligible: false, RejectionReason: reason,
                LegacySinks: legacySinks);
            return null;
        }

        var rejection = KernelEligibility.DescribeRejection(policy, kernelReady, enrichersPresent: false);
        KernelIntrospection.RecordRejection(rejection);
        if (rejection is not null)
        {
            diagnostic = new KernelDiagnostic(KernelEligible: false, RejectionReason: rejection,
                LegacySinks: legacySinks);
            return null;
        }

        var kernelDecorators = ExtractKernelDecorators(policy.CustomDecorators);
        diagnostic = new KernelDiagnostic(KernelEligible: true, RejectionReason: null,
            LegacySinks: legacySinks);
        return KernelCompiler.CompileFanOut(kernelReady, enrichers, kernelDecorators);
    }

    // Wrap every non-IKernelSink child in MaterializingKernelSink so the
    // resulting list is uniformly kernel-ready. Records the wrapped indexes
    // for diagnostics (LegacySinkInfo). Native-kernel sinks pass through by
    // reference — no wrapper allocation in the common case.
    private static IReadOnlyList<ILogger> PartitionSinks(
        IReadOnlyList<ILogger> children,
        MessageTemplateParser templateParser,
        out IReadOnlyList<LegacySinkInfo> legacySinks)
    {
        if (children.Count == 0)
        {
            legacySinks = System.Array.Empty<LegacySinkInfo>();
            return children;
        }

        List<LegacySinkInfo>? legacy = null;
        ILogger[]? rewritten = null;

        for (var i = 0; i < children.Count; i++)
        {
            var child = children[i];
            if (child is IKernelSink) continue;

            rewritten ??= MaterializeChildArray(children);
            rewritten[i] = new MaterializingKernelSink(child, templateParser);
            legacy ??= new List<LegacySinkInfo>();
            legacy.Add(new LegacySinkInfo(i, child.GetType().Name));
        }

        legacySinks = legacy is null
            ? System.Array.Empty<LegacySinkInfo>()
            : legacy;

        return (IReadOnlyList<ILogger>?)rewritten ?? children;
    }

    private static ILogger[] MaterializeChildArray(IReadOnlyList<ILogger> children)
    {
        var copy = new ILogger[children.Count];
        for (var i = 0; i < children.Count; i++) copy[i] = children[i];
        return copy;
    }

    // Called only after eligibility has approved the decorator list — every
    // entry is guaranteed to implement IKernelDecorator here. Returns null
    // when there are no custom decorators at all so the compiler can skip
    // the wrapping pass.
    private static System.Collections.Generic.IReadOnlyList<IKernelDecorator>? ExtractKernelDecorators(
        System.Collections.Generic.IReadOnlyList<IConfigurablePipelineDecorator>? decorators)
    {
        if (decorators is null or { Count: 0 }) return null;

        var result = new System.Collections.Generic.List<IKernelDecorator>(decorators.Count);
        foreach (var decorator in decorators)
        {
            result.Add((IKernelDecorator)decorator);
        }
        return result;
    }

    // Returns the list of IKernelEnrichers to inline, an empty array when no
    // enrichers are configured, or null when at least one configured enricher
    // does not participate in the kernel (forces chain fallback).
    private static System.Collections.Generic.IReadOnlyList<IKernelEnricher>? ClassifyEnrichers(ILogEnricher enricher)
    {
        if (enricher is NullLogEnricher) return System.Array.Empty<IKernelEnricher>();

        if (enricher is CompositeLogEnricher composite)
        {
            if (composite.Count == 0) return System.Array.Empty<IKernelEnricher>();

            var kernelEnrichers = new System.Collections.Generic.List<IKernelEnricher>(composite.Count);
            foreach (var inner in composite.Inner)
            {
                if (inner is not IKernelEnricher kernelEnricher) return null;
                kernelEnrichers.Add(kernelEnricher);
            }
            return kernelEnrichers;
        }

        // Not null, not composite — an unknown enricher implementation. Only
        // accept it when it itself implements IKernelEnricher.
        return enricher is IKernelEnricher solo
            ? new[] { solo }
            : null;
    }

    // The effective minimum is the stricter of the policy floor and any floor
    // the sink chain reports. The sink chain reports null when at least one
    // sink is opaque to introspection (e.g., a predicate that could accept any
    // level); in that case we fall back to the policy minimum unchanged.
    private static LogLevel ComputeEffectiveMinimum(
        ILogLevelRegistry levelRegistry,
        LogPipelinePolicy policy,
        ILogger routedSinks)
    {
        if (policy.DynamicLevelPolicy is not null) return policy.MinimumLevel;
        if (routedSinks is not ISinkChainLevelIntrospection introspection) return policy.MinimumLevel;

        var sinkChainMin = introspection.GetMinimumLevel(levelRegistry);
        if (sinkChainMin is null) return policy.MinimumLevel;

        return levelRegistry.IsAtOrAbove(sinkChainMin, policy.MinimumLevel)
            ? sinkChainMin
            : policy.MinimumLevel;
    }

    // Single dispatch: registry lookup first, custom-decorator fallthrough second.
    // The long if-ladder that used to live here now lives in each handler's Apply.
    private static void ApplyStep(
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
        var handler = PipelineStepHandlerRegistry.Resolve(step.Name);
        if (handler is not null)
        {
            var context = new PipelineStepApplyContext(
                step, builder, policy, filters, failureSink,
                useDeferredRendering, templateParser, pipelineAccessor, levelRegistry);
            handler.Apply(context);
            return;
        }

        // Fall through: unknown step name → plugin-supplied custom decorator.
        if (policy.CustomDecorators is null) return;

        var decorator = FindDecorator(policy.CustomDecorators, step.Name);
        if (decorator is null) return;

        var wrapped = decorator.CreateDecorator(builder.CurrentPipeline, pipelineAccessor);
        builder.SetPipeline(wrapped);
    }

    private static IConfigurablePipelineDecorator? FindDecorator(
        IReadOnlyList<IConfigurablePipelineDecorator> decorators, string stepName)
    {
        foreach (var d in decorators)
        {
            if (string.Equals(d.StepName, stepName, StringComparison.OrdinalIgnoreCase))
                return d;
        }
        return null;
    }

    private static List<ILogFilter> BuildFilters(
        ILogLevelRegistry levelRegistry,
        LogPipelinePolicy policy,
        LogLevel effectiveMinimum)
    {
        ILogFilter levelFilter = policy.DynamicLevelPolicy is not null
            ? new SwitchableLevelFilter(
                levelRegistry,
                policy.DynamicLevelPolicy.GlobalLevelSwitch,
                policy.DynamicLevelPolicy.CategoryLevelSwitches)
            : new LevelFilter(levelRegistry, effectiveMinimum);

        var filters = new List<ILogFilter> { levelFilter };

        if (policy.SamplingFilter is not null)
        {
            filters.Add(policy.SamplingFilter);
        }

        return filters;
    }
}

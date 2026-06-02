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
        PipelineAccessor? pipelineAccessor)
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
            ? new DeferredLogEventFactory(dateTimeProvider, _scopeProvider, _enricher)
            : new LogEventFactory(dateTimeProvider, templateParser, _scopeProvider, _enricher);

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

        // Auto-register disposable sinks. Without this, sync IDisposable
        // sinks (file sinks, stream-backed writers) never see their
        // Dispose() because the pipeline-result disposal chain only
        // walks pipeline decorators that were registered via
        // TrackAsyncResource. Walking SafeCompositeLogger.Children
        // covers fan-out sinks; a single-sink pipeline is handled by
        // the direct check.
        RegisterSinkDisposables(builder, routedSinks);

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
        // falls through to the decorator chain built above, unchanged. The
        // failureSink is threaded into the compiled kernel so a throwing
        // sink reports through the same channel SafeCompositeLogger uses on
        // the chain path.
        var kernel = TryCompileKernel(routedSinks, policy, useDeferredRendering, failureSink,
            out var kernelDiagnostic);

        var built = builder.Build(
            eventFactory, _scopeProvider, _includeCallerInfo,
            levelRegistry, effectiveMinimum,
            kernel, kernel is null ? null : dateTimeProvider,
            // Per-pipeline external-event-injection consent rides on the policy
            // (built from the JSON config). The StructuredLogger reads it at its
            // Log(LogEvent) port — the only consent boundary. See the ADR:
            // docs/design/external-event-injection-switch.md.
            allowExternalEventInjection: policy.AllowExternalEventInjection);

        return built with { KernelDiagnostic = kernelDiagnostic };
    }

    // Walk routedSinks and register every IDisposable / IAsyncDisposable
    // sink with the assembly builder so the pipeline result's
    // DisposeAsync reaches them. SafeCompositeLogger is the fan-out
    // wrapper; its Children are the real sinks. A single-sink pipeline
    // arrives here as the sink itself (no composite wrapping). Async-
    // disposable sinks land on TrackAsyncResource so they drain; sync-
    // only sinks land on TrackSyncResource for plain Dispose() release.
    private static void RegisterSinkDisposables(PipelineAssemblyBuilder builder, ILogger routedSinks)
    {
        if (routedSinks is SafeCompositeLogger composite)
        {
            foreach (var child in composite.Children)
            {
                RegisterOneSink(builder, child);
            }
        }
        else
        {
            RegisterOneSink(builder, routedSinks);
        }
    }

    private static void RegisterOneSink(PipelineAssemblyBuilder builder, ILogger sink)
    {
        // Async-first so a sink that implements both gets the drain
        // semantics. Most "sink implements both" cases are wrappers
        // (e.g. file writers around buffered streams) where the async
        // path is the correct release path.
        if (sink is IAsyncDisposable async)
        {
            builder.TrackAsyncResource(async);
        }
        else if (sink is IDisposable sync)
        {
            builder.TrackSyncResource(sync);
        }
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
        ILogFailureSink failureSink,
        out KernelDiagnostic diagnostic)
    {
        var routedChildren = routedSinks is SafeCompositeLogger c ? c.Children : null;
        var advisories = BuildAdvisories(policy, routedChildren);

        if (useDeferredRendering)
        {
            const string reason = "deferred rendering enabled";
            KernelIntrospection.RecordRejection(reason);
            diagnostic = new KernelDiagnostic(KernelEligible: false, RejectionReason: reason, Advisories: advisories);
            return null;
        }

        if (routedSinks is not SafeCompositeLogger composite)
        {
            var reason = $"routedSinks is {routedSinks.GetType().Name}, expected SafeCompositeLogger";
            KernelIntrospection.RecordRejection(reason);
            diagnostic = new KernelDiagnostic(KernelEligible: false, RejectionReason: reason, Advisories: advisories);
            return null;
        }
        var children = composite.Children;

        // Enricher classification — three outcomes:
        //   (a) no enrichers at all (NullLogEnricher or empty Composite) → eligible, no kernel enricher list
        //   (b) composite where every inner enricher implements IKernelEnricher → eligible, pass list to compiler
        //   (c) anything else (unknown enricher type, or composite with non-kernel enrichers) → forces chain
        var enrichers = ClassifyEnrichers(_enricher);
        if (enrichers is null)
        {
            const string reason = "enrichers present (not all IKernelEnricher)";
            KernelIntrospection.RecordRejection(reason);
            diagnostic = new KernelDiagnostic(KernelEligible: false, RejectionReason: reason, Advisories: advisories);
            return null;
        }

        // Strict per-sink IKernelSink check. Every built-in Herald.OSS sink
        // implements IKernelSink; custom sinks that skip it fall back to
        // the chain path. The diagnostic carries the rejection reason so
        // adopters can find which sink to upgrade.
        var rejection = KernelEligibility.DescribeRejection(policy, children, enrichersPresent: false);
        KernelIntrospection.RecordRejection(rejection);
        if (rejection is not null)
        {
            diagnostic = new KernelDiagnostic(KernelEligible: false, RejectionReason: rejection, Advisories: advisories);
            return null;
        }

        var kernelDecorators = ExtractKernelDecorators(policy.CustomDecorators);
        diagnostic = new KernelDiagnostic(KernelEligible: true, RejectionReason: null, Advisories: advisories);
        return KernelCompiler.CompileFanOut(children, enrichers, kernelDecorators, failureSink);
    }

    // Build the non-blocking advisory list. Today the only advisory: any
    // routed sink implements INetworkSink AND the strategy has no enabled
    // Async step. In that configuration every emit blocks the producer
    // thread on the sink's I/O. Adopters who want non-blocking delivery
    // wrap their pipeline with WithAsync(). The advisory names the sink
    // so the operator can find it.
    private static IReadOnlyList<string> BuildAdvisories(
        LogPipelinePolicy policy,
        IReadOnlyList<ILogger>? routedChildren)
    {
        if (routedChildren is null or { Count: 0 }) return System.Array.Empty<string>();
        if (IsAsyncDeliveryConfigured(policy)) return System.Array.Empty<string>();

        List<string>? advisories = null;
        for (var i = 0; i < routedChildren.Count; i++)
        {
            var sink = routedChildren[i];
            if (sink is not MMP.Herald.Sinks.INetworkSink) continue;

            advisories ??= new List<string>();
            advisories.Add(
                $"sink {i} ({sink.GetType().Name}) is network-bound but the pipeline " +
                "has no Async step — every emit blocks the producer thread on the sink's I/O. " +
                "Wrap with .WithPipelineStrategy(PipelineStrategy.Create().Swappable().Async().FanOut()) " +
                "or remove the INetworkSink marker if the sink is genuinely non-blocking.");
        }

        return (IReadOnlyList<string>?)advisories ?? System.Array.Empty<string>();
    }

    private static bool IsAsyncDeliveryConfigured(LogPipelinePolicy policy)
    {
        if (policy.AsyncPolicy is { IsEnabled: true }) return true;
        var steps = policy.Strategy?.Steps;
        if (steps is null) return false;
        for (var i = 0; i < steps.Count; i++)
        {
            if (string.Equals(steps[i].Name, "async", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
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

        // AND-chain: append every configured filter (sampling + throttling + adaptive +
        // any custom). EffectiveFilters folds the legacy single SamplingFilter slot into
        // the list, so this one path handles both the new multi-filter config and old
        // single-slot callers. The FilteringLogger applies them in order; all must Allow.
        var configured = policy.EffectiveFilters;
        if (configured is { Count: > 0 })
        {
            filters.AddRange(configured);
        }

        return filters;
    }
}

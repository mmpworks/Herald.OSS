#nullable enable

using System.Collections.Generic;
using MMP.Herald.Configuration;

namespace MMP.Herald.Pipeline.Kernel;

/// <summary>
/// Decides whether a given pipeline configuration is a match for the kernel
/// fast path. The kernel only covers the subset of shapes where every
/// optimization is known to be safe — any mismatch falls back to the
/// decorator chain silently. No user-facing API, no regression.
///
/// <para>
/// Coverage expanded over multiple phases:
/// </para>
/// <list type="bullet">
///   <item>Phase 1: <c>Minimal</c> strategy (swappable + filtering + fanOut).</item>
///   <item>Phase 2: <c>FilterEarly</c> / <c>Default</c> strategies when their optional
///         steps (Async / Rendering / Batching) carry disabled policies —
///         structurally equivalent to Minimal at that point.</item>
///   <item>Still required for phase 2: no <see cref="LogPipelinePolicy.DynamicLevelPolicy"/>,
///         no sampling filter, no event processors, no custom decorators,
///         no hot reload, no enrichers, every routed sink implements
///         <see cref="IKernelSink"/>.</item>
/// </list>
/// </summary>
public static class KernelEligibility
{
    // Steps the kernel can handle directly or trivially ignore.
    // Anything in this set is either a no-op in the kernel path (swappable),
    // handled explicitly by the kernel (filtering, fanOut), or tolerated when
    // its configured policy is disabled (async, rendering, batching) —
    // the per-policy check lives below in IsStepTolerated.
    private static readonly HashSet<string> KnownSteps =
        new(System.StringComparer.OrdinalIgnoreCase)
        {
            "swappable",
            "filtering",
            "fanOut",
            "eventProcessing",
            "async",
            "rendering",
            "batching",
            "flightRecorder",
            "postFiltering",
        };

    public static bool IsEligible(
        LogPipelinePolicy policy,
        IReadOnlyList<ILogger> routedSinks,
        bool enrichersPresent) =>
        DescribeRejection(policy, routedSinks, enrichersPresent) is null;

    /// <summary>
    /// Return the first eligibility rule this configuration fails, or
    /// <c>null</c> if every rule passes. Useful for diagnostics: the kernel
    /// auto-activates on eligible configs, so a user wondering "why didn't
    /// my config use the kernel?" can call this to find out.
    /// </summary>
    public static string? DescribeRejection(
        LogPipelinePolicy policy,
        IReadOnlyList<ILogger> routedSinks,
        bool enrichersPresent)
    {
        ArgumentNullOrNot(policy);
        ArgumentNullOrNot(routedSinks);

        if (policy.DynamicLevelPolicy is not null) return "dynamic level policy configured";
        if (policy.EffectiveFilters is { Count: > 0 }) return "sampling/throttling/adaptive filter configured";
        if (policy.EventProcessors is { Count: > 0 }) return "event processors configured";
        if (HasNonKernelCustomDecorator(policy.CustomDecorators))
            return "custom decorators configured (not all implement IKernelDecorator)";
        if (policy.HotReloadEnabled) return "hot reload enabled";
        if (enrichersPresent) return "enrichers present";

        var strategy = policy.Strategy ?? PipelineStrategy.Default();
        foreach (var step in strategy.Steps)
        {
            if (!KnownSteps.Contains(step.Name))
                return $"unknown strategy step: {step.Name}";
            if (!IsStepTolerated(step.Name, policy))
                return $"strategy step {step.Name} not tolerated under current policy";
        }

        for (var i = 0; i < routedSinks.Count; i++)
        {
            if (routedSinks[i] is not IKernelSink)
                return $"sink {i} ({routedSinks[i].GetType().Name}) does not implement IKernelSink";
        }

        return null;
    }

    // For steps that only qualify when a related policy is disabled. Returning
    // true means "this step is in the strategy but the kernel can safely skip
    // it because the feature it drives is off". Returning false forces the
    // chain fallback.
    private static bool IsStepTolerated(string stepName, LogPipelinePolicy policy)
    {
        switch (stepName.ToLowerInvariant())
        {
            case "async":
                return policy.AsyncPolicy is null or { IsEnabled: false };

            case "batching":
                return policy.BatchingPolicy is null or { IsEnabled: false };

            case "rendering":
                // Deferred rendering requires the RenderingLogger decorator to
                // run downstream — kernel path skips all decorators, so deferred
                // pipelines stay on the chain.
                return policy.AsyncPolicy is null or { DeferRendering: false };

            case "postfiltering":
                // The PostFiltering step handler is a no-op unless
                // PostFilteringPolicy is set. Kernel tolerates the step when
                // no policy is configured.
                return policy.PostFilteringPolicy is null;

            case "flightrecorder":
                // The FlightRecorder step handler installs a ring-buffer
                // decorator only when FlightRecorderPolicy is set. Kernel
                // tolerates the step when no policy is configured — the
                // configured case forces the chain path because the recorder
                // wraps the pipeline and is not kernel-aware.
                return policy.FlightRecorderPolicy is null;

            default:
                return true;
        }
    }

    // A custom decorator list disqualifies only when at least one entry does
    // NOT implement IKernelDecorator. A pipeline with decorators that all
    // implement IKernelDecorator stays kernel-eligible; the compiler will
    // weave them into the emitted kernel.
    private static bool HasNonKernelCustomDecorator(
        IReadOnlyList<IConfigurablePipelineDecorator>? decorators)
    {
        if (decorators is null or { Count: 0 }) return false;
        foreach (var decorator in decorators)
        {
            if (decorator is not IKernelDecorator) return true;
        }
        return false;
    }

    private static void ArgumentNullOrNot<T>(T value) where T : class
    {
        System.ArgumentNullException.ThrowIfNull(value);
    }
}

#nullable enable

using System;
using System.Collections.Generic;
using MMP.Herald.Configuration.Runtime;
using MMP.Herald.Failures;
using MMP.Herald.Filters;
using MMP.Herald.Levels;
using MMP.Herald.Output.Rendering;
using MMP.Herald.Pipeline;
using MMP.Herald.Predicates;
using MMP.Herald.Routing.Loopback;

namespace MMP.Herald.Routing;

/// <summary>
/// Declarative sink router for the core logging system.
/// </summary>
public sealed class DefaultLogSinkRouterFactory : ILogSinkRouterFactory
{
    private readonly ILogSinkFactory _sinkFactory;
    private readonly ILogPredicateCompiler _predicateCompiler;
    private readonly ILogFailureSink _failureSink;

    public DefaultLogSinkRouterFactory(
        ILogSinkFactory sinkFactory,
        ILogPredicateCompiler predicateCompiler,
        ILogFailureSink? failureSink = null)
    {
        _sinkFactory = sinkFactory ?? throw new ArgumentNullException(nameof(sinkFactory));
        _predicateCompiler = predicateCompiler ?? throw new ArgumentNullException(nameof(predicateCompiler));
        _failureSink = failureSink ?? NullLogFailureSink.Instance;
    }

    // Explicit interface implementation delegates to the accessor-aware overload.
    ILogger ILogSinkRouterFactory.Create(
        LoggingRuntimeConfiguration runtimeConfiguration,
        ILogLevelRegistry levelRegistry,
        ILogOutputTransformerRegistry transformerRegistry) =>
        Create(runtimeConfiguration, levelRegistry, transformerRegistry, null);

    public ILogger Create(
        LoggingRuntimeConfiguration runtimeConfiguration,
        ILogLevelRegistry levelRegistry,
        ILogOutputTransformerRegistry transformerRegistry,
        PipelineAccessor? pipelineAccessor)
    {
        ArgumentNullException.ThrowIfNull(runtimeConfiguration);
        ArgumentNullException.ThrowIfNull(levelRegistry);
        ArgumentNullException.ThrowIfNull(transformerRegistry);

        var sinksByName = new Dictionary<string, ILogger>(StringComparer.OrdinalIgnoreCase);

        // Resolve the pipeline name once so loopback registration keys
        // do not collide across pipelines that happen to share a sink
        // name. Falls back to a stable sentinel so legacy paths that
        // do not set PipelineName still work — the registry treats
        // "default" as a real key like any other.
        var pipelineName = string.IsNullOrWhiteSpace(runtimeConfiguration.PipelineName)
            ? "default"
            : runtimeConfiguration.PipelineName!;

        // Refresh the per-pipeline holder sets so stale entries from a
        // previous build do not point at disposed wrappers. The
        // registries are process-wide; clearing first is what makes a
        // commit-rebuild safe. Both holders re-register one entry per
        // sink during the wrap loop below.
        SinkRunStateRegistry.ClearPipeline(pipelineName);
        SinkOverridesRegistry.ClearPipeline(pipelineName);

        foreach (var sinkDefinition in runtimeConfiguration.Sinks)
        {
            var inner = _sinkFactory.Create(
                sinkDefinition,
                levelRegistry,
                transformerRegistry);

            // Loopback wrap is the outermost layer: it sees post-retry events,
            // which matches the operator's question ("what would this sink
            // actually emit?"). The interceptor always wraps so a future
            // PATCH to runState can flip disabled/live/test without
            // rebuilding the pipeline.
            sinksByName[sinkDefinition.Name] = WrapWithLoopback(
                inner, sinkDefinition, runtimeConfiguration, pipelineName, levelRegistry);
        }

        var routedSinks = new List<ILogger>();

        foreach (var route in runtimeConfiguration.Routes)
        {
            if (!sinksByName.TryGetValue(route.SinkName, out var sink))
            {
                throw new KeyNotFoundException(
                    $"No sink named '{route.SinkName}' exists for the configured route.");
            }

            // Skip the FilteringLogger wrapper when the predicate is trivially
            // true — the wrapper would be a no-op call per event. Preserving
            // the sink reference verbatim is what makes kernel eligibility
            // work: the kernel can dispatch directly to IKernelSink.
            if (route.Predicate is Predicates.PredicateSpec.True)
            {
                routedSinks.Add(sink);
                continue;
            }

            // Phase-4 kernel optimisation: compile a bare level-at-or-above
            // predicate into a LevelFilteredKernelSink, which implements
            // IKernelSink directly. Keeps level-based route filters on the
            // fast path when the underlying sink is also IKernelSink;
            // predicates beyond this shape fall through to the generic
            // CompiledPredicateFilter wrap (unchanged behaviour).
            if (route.Predicate is Predicates.PredicateSpec.LevelAtOrAbove levelAtOrAbove
                && sink is Pipeline.Kernel.IKernelSink
                && levelRegistry.GetByKeyOrNull(levelAtOrAbove.LevelKey) is { } minLevel)
            {
                routedSinks.Add(new Pipeline.Kernel.LevelFilteredKernelSink(sink, levelRegistry, minLevel));
                continue;
            }

            var compiledPredicate = _predicateCompiler.Compile(route.Predicate, levelRegistry);

            routedSinks.Add(new FilteringLogger(
                sink,
                [new CompiledPredicateFilter(compiledPredicate)]));
        }

        var composite = new SafeCompositeLogger(routedSinks, _failureSink);
        pipelineAccessor?.Register(composite);
        return composite;
    }

    /// <summary>
    /// Wrap one sink with the loopback interceptor. Always runs — the
    /// interceptor's Disabled fast path is one Volatile read so the
    /// runtime cost on a sink that isn't using loopback is negligible,
    /// and the holder needs to exist either way for the management API
    /// to flip runState without a rebuild.
    /// </summary>
    private static ILogger WrapWithLoopback(
        ILogger inner,
        LoggingRuntimeSinkDefinition def,
        LoggingRuntimeConfiguration cfg,
        string pipelineName,
        ILogLevelRegistry levelRegistry)
    {
        var holder = new SinkRunStateHolder(def.RunState);
        SinkRunStateRegistry.Register(pipelineName, def.Name, holder);

        // Per-sink runtime overrides: minLevel + tee flags. Seeded
        // from the static config but mutable from the management API
        // without rebuilding the pipeline. The interceptor reads
        // these on every event.
        var overrides = new SinkOverridesHolder(
            initialMinLevel: def.MinLevel,
            initialTeeLiveToFile: def.TeeLiveToFile,
            initialTeeLiveToUrl: def.TeeLiveToUrl);
        SinkOverridesRegistry.Register(pipelineName, def.Name, overrides);

        LoopbackFileWriter? file = null;
        if (!string.IsNullOrWhiteSpace(cfg.TestLoopbackLogDir))
        {
            try
            {
                file = new LoopbackFileWriter(
                    logDir: cfg.TestLoopbackLogDir!,
                    suffix: def.TestOutFileSuffix ?? string.Empty,
                    sinkName: def.Name,
                    writeAsNdjson: cfg.LoopbackUseNdjson,
                    entriesPerFile: cfg.LoopbackEntriesPerFile);
            }
            catch
            {
                // Bad path / permissions → file leg is just unavailable.
                // The bus + URL legs still work.
                file = null;
            }
        }

        LoopbackUrlPoster? url = null;
        if (!string.IsNullOrWhiteSpace(cfg.TestLoopbackUrl))
        {
            try
            {
                // Per-sink URL substitution: a pipeline-level URL can
                // contain {pipelineName} and {sinkName} placeholders so
                // operators can route every sink to a single endpoint
                // template (e.g. <host>/api/loopback/ingest/{pipelineName}/{sinkName})
                // and the receiver still knows which sink each post
                // belongs to. Substitution happens once at wrap time —
                // the URL never re-renders per event.
                var resolved = cfg.TestLoopbackUrl!
                    .Replace("{pipelineName}", pipelineName, StringComparison.OrdinalIgnoreCase)
                    .Replace("{sinkName}", def.Name, StringComparison.OrdinalIgnoreCase);
                url = new LoopbackUrlPoster(resolved);
            }
            catch
            {
                // Malformed URL → URL leg is just unavailable.
                url = null;
            }
        }

        return LoopbackInterceptor.Wrap(
            innerSink: inner,
            state: holder,
            overrides: overrides,
            levels: levelRegistry,
            pipelineName: pipelineName,
            sinkName: def.Name,
            file: file,
            url: url);
    }
}


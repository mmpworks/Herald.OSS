#nullable enable

using System;
using System.Collections.Generic;
using MMP.Herald.Configuration;
using MMP.Herald.Configuration.Runtime;
using MMP.Herald.Expansions;
using MMP.Herald.Failures;
using MMP.Herald.Levels;
using MMP.Herald.Metrics;
using MMP.Herald.Output.Rich;
using MMP.Herald.Output.Rendering;
using MMP.Herald.Pipeline;
using MMP.Herald.Routing;
using MMP.Herald.Time;

namespace MMP.Herald.Bootstrap;

/// <summary>
/// Coordinates bootstrap of the configured logging system.
/// </summary>
public sealed class LoggingBootstrap
{
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly LoggingRuntimeConfiguration _runtimeConfiguration;
    private readonly ILogLevelRegistryFactory _levelRegistryFactory;
    private readonly ILogOutputTransformerRegistryFactory _transformerRegistryFactory;
    private readonly ILogLevelOutputExpansionRegistryFactory _expansionRegistryFactory;
    private readonly ILogSinkRouterFactory _sinkRouterFactory;
    private readonly ILogPipelineFactory _pipelineFactory;
    private readonly ILogPipelinePolicyFactory _pipelinePolicyFactory;
    private readonly ConfigurableLogLevelDumpRenderer _levelDumpRenderer;
    private readonly IRenderedLogOutputWriter _richConsoleWriter;
    private readonly ILogFailureSink _failureSink;
    private readonly LogMetricsRegistry? _metricsRegistry;
    private readonly IReadOnlyList<ILogSinkProvider>? _additionalSinkProviders;

    private readonly Pipeline.Kernel.IRegistrarStore _registrarStore;
    private readonly bool _provenanceGateEnabled;

    public LoggingBootstrap(
        IDateTimeProvider dateTimeProvider,
        LoggingRuntimeConfiguration runtimeConfiguration,
        ILogLevelRegistryFactory levelRegistryFactory,
        ILogOutputTransformerRegistryFactory transformerRegistryFactory,
        ILogLevelOutputExpansionRegistryFactory expansionRegistryFactory,
        ILogSinkRouterFactory sinkRouterFactory,
        ILogPipelineFactory pipelineFactory,
        ILogPipelinePolicyFactory pipelinePolicyFactory,
        ConfigurableLogLevelDumpRenderer levelDumpRenderer,
        IRenderedLogOutputWriter richConsoleWriter,
        ILogFailureSink? failureSink = null,
        LogMetricsRegistry? metricsRegistry = null,
        IReadOnlyList<ILogSinkProvider>? additionalSinkProviders = null,
        Pipeline.Kernel.IRegistrarStore? registrarStore = null,
        bool provenanceGateEnabled = true)
    {
        _dateTimeProvider = dateTimeProvider;
        _runtimeConfiguration = runtimeConfiguration;
        _levelRegistryFactory = levelRegistryFactory;
        _transformerRegistryFactory = transformerRegistryFactory;
        _expansionRegistryFactory = expansionRegistryFactory;
        _sinkRouterFactory = sinkRouterFactory;
        _pipelineFactory = pipelineFactory;
        _pipelinePolicyFactory = pipelinePolicyFactory;
        _levelDumpRenderer = levelDumpRenderer;
        _richConsoleWriter = richConsoleWriter;
        _failureSink = failureSink ?? NullLogFailureSink.Instance;
        _metricsRegistry = metricsRegistry;
        _additionalSinkProviders = additionalSinkProviders;
        _registrarStore = registrarStore ?? Pipeline.Kernel.NullRegistrarStore.Instance;
        _provenanceGateEnabled = provenanceGateEnabled;
    }

    public LoggingBootstrapResult Bootstrap(
        IReadOnlyDictionary<string, object?>? defaultContext = null,
        PipelineAccessor? pipelineAccessor = null,
        string? referenceSource = null)
    {
        var levelRegistry = _levelRegistryFactory.Create();
        var expansionRegistry = _expansionRegistryFactory.Create();
        var transformerRegistry = _transformerRegistryFactory.Create(expansionRegistry);
        var policy = _pipelinePolicyFactory.Create();

        // Off-switch: when the operator opted out via WithoutSinkInjectionGate,
        // skip the reference-token generation and pass null to the router so
        // sinks aren't wrapped. Events still get stamped (the factory auto-
        // generates its own token internally) but no gate validates, so
        // anyone with a sink reference can call sink.Log directly. Same
        // shape as pre-Phase-1; the gate is opt-out, not opt-in.
        if (!_provenanceGateEnabled)
        {
            return BootstrapWithoutGate(defaultContext, pipelineAccessor,
                levelRegistry, expansionRegistry, transformerRegistry, policy);
        }

        // Persistence first: if a snapshot exists, use the persisted
        // reference token so derived keys issued before the restart stay
        // valid. The hot-reload path also passes a referenceSource override —
        // hot reload wins over snapshot (it's already running with that
        // token, no point re-loading).
        var snapshot = referenceSource is null ? _registrarStore.Load() : null;

        // Generate this pipeline's reference token once. Both concrete factories
        // receive the same string instance — sink-side gates seed their accepted
        // list with it, and the LogEventFactory stamps it on every event the
        // pipeline produces. The chain-path stamping (via factory) and the
        // kernel-path stamping (via StructuredLogger reading factory.GenSource)
        // therefore share the exact string instance, so the gate's hot-path
        // ReferenceEquals check succeeds without falling through to string
        // equality. Generic factories that don't recognize the concrete
        // overloads fall back to their auto-generated tokens — gating is off
        // for those pipelines.
        //
        // Hot reload passes the existing reference token in so the rebuilt
        // pipeline's gates accept the same derived keys the registrar already
        // issued — without that, every reload silently revokes every external
        // registration.
        referenceSource ??= snapshot?.ReferenceToken ?? System.Guid.NewGuid().ToString("N");

        SinkRouterResult? routerResult = null;
        ILogger routedSinks;
        if (_sinkRouterFactory is DefaultLogSinkRouterFactory concreteRouter)
        {
            routerResult = concreteRouter.CreateWithGates(
                _runtimeConfiguration, levelRegistry, transformerRegistry,
                pipelineAccessor, referenceSource);
            routedSinks = routerResult.Composite;
        }
        else
        {
            routedSinks = _sinkRouterFactory.Create(
                _runtimeConfiguration, levelRegistry, transformerRegistry);
        }

        var composition = _pipelineFactory is DefaultLogPipelineFactory concretePipeline
            ? concretePipeline.Create(routedSinks, _dateTimeProvider, levelRegistry, _failureSink, policy, pipelineAccessor, referenceSource)
            : _pipelineFactory.Create(routedSinks, _dateTimeProvider, levelRegistry, _failureSink, policy);

        var logger = composition.Logger;

        if (defaultContext is not null)
        {
            logger = logger.WithContext(defaultContext);
        }

        if (_runtimeConfiguration.DumpRegisteredLevelsToConsole)
        {
            var dump = _levelDumpRenderer.Render(levelRegistry);
            _richConsoleWriter.Write(dump);
        }

        // Build the external-source registrar from the alias→gate map the
        // router produced. The registrar lets external callers inject events
        // into specific sinks without forging the pipeline's reference token —
        // they call result.RegisterExternalSource(rawKey, timestamp, aliases),
        // get back a derived key, and stamp it on events they construct.
        // Null when the router didn't gate (test factory or non-default
        // router) — gating is opt-in by virtue of using the default router.
        Pipeline.Kernel.ExternalSourceRegistrar? registrar = null;
        if (routerResult?.GatesByAlias is { } gates)
        {
            registrar = new Pipeline.Kernel.ExternalSourceRegistrar(
                referenceSource, gates, _registrarStore);

            // Replay persisted registrations onto the new gates. Each
            // Register call also writes through to the store; the first
            // replay re-confirms the snapshot, subsequent registrations
            // append. Snapshot entries that name aliases the current
            // config doesn't include get skipped — the operator likely
            // renamed or removed those sinks between boots.
            if (snapshot is not null)
            {
                foreach (var entry in snapshot.Registrations)
                {
                    var validAliases = FilterKnownAliases(entry.SinkAliases, gates);
                    if (validAliases.Length == 0) continue;
                    registrar.Register(entry.RawKey, entry.Timestamp, validAliases);
                }
            }
            else
            {
                // No prior snapshot — write the fresh state once so the
                // store has the reference token even if no external
                // sources are registered yet. Lets a hot reload after
                // the first boot find the token it needs.
                _registrarStore.Save(new Pipeline.Kernel.RegistrarSnapshot(
                    Pipeline.Kernel.RegistrarSnapshot.CurrentSchemaVersion,
                    referenceSource,
                    System.Array.Empty<Pipeline.Kernel.RegistrarPersistedEntry>()));
            }
        }

        HotReloadableLoggingBootstrap? hotReloadBootstrap = null;

        if (composition.SwappableLogger is not null)
        {
            hotReloadBootstrap = new HotReloadableLoggingBootstrap(
                composition.SwappableLogger,
                _dateTimeProvider,
                composition.AsyncResource,
                policy.DynamicLevelPolicy?.GlobalLevelSwitch,
                defaultContext: defaultContext,
                failureSink: _failureSink,
                additionalSinkProviders: _additionalSinkProviders,
                externalSourceRegistrar: registrar,
                structuredLogger: composition.Logger,
                provenanceGateEnabled: _provenanceGateEnabled);
        }

        return new LoggingBootstrapResult(
            Logger: logger,
            AsyncResource: composition.AsyncResource,
            LevelRegistry: levelRegistry,
            FailureSink: _failureSink,
            DynamicLevelPolicy: policy.DynamicLevelPolicy,
            SwappableLogger: composition.SwappableLogger,
            HotReloadBootstrap: hotReloadBootstrap,
            MetricsRegistry: _metricsRegistry,
            PipelineAccessor: pipelineAccessor,
            MinimumLevel: policy.MinimumLevel,
            ExternalSourceRegistrar: registrar,
            GatesByAlias: routerResult?.GatesByAlias);
    }

    // Off-switch path: no reference token, no gates wrapping sinks, no
    // registrar. The router's plain Create() path runs, sinks are passed
    // through verbatim. Anyone with a sink reference can call sink.Log
    // directly — same shape Herald had before the gate shipped. Operators
    // pick this when they don't care about in-process injection (single-
    // process, single-tenant, no plugin surface).
    private LoggingBootstrapResult BootstrapWithoutGate(
        IReadOnlyDictionary<string, object?>? defaultContext,
        PipelineAccessor? pipelineAccessor,
        ILogLevelRegistry levelRegistry,
        ILogLevelOutputExpansionRegistry expansionRegistry,
        ILogOutputTransformerRegistry transformerRegistry,
        LogPipelinePolicy policy)
    {
        var routedSinks = _sinkRouterFactory is DefaultLogSinkRouterFactory concreteRouter
            ? concreteRouter.Create(_runtimeConfiguration, levelRegistry, transformerRegistry, pipelineAccessor)
            : _sinkRouterFactory.Create(_runtimeConfiguration, levelRegistry, transformerRegistry);

        var composition = _pipelineFactory is DefaultLogPipelineFactory concretePipeline
            ? concretePipeline.Create(routedSinks, _dateTimeProvider, levelRegistry, _failureSink, policy, pipelineAccessor)
            : _pipelineFactory.Create(routedSinks, _dateTimeProvider, levelRegistry, _failureSink, policy);

        var logger = composition.Logger;
        if (defaultContext is not null) logger = logger.WithContext(defaultContext);

        if (_runtimeConfiguration.DumpRegisteredLevelsToConsole)
        {
            var dump = _levelDumpRenderer.Render(levelRegistry);
            _richConsoleWriter.Write(dump);
        }

        HotReloadableLoggingBootstrap? hotReloadBootstrap = null;
        if (composition.SwappableLogger is not null)
        {
            hotReloadBootstrap = new HotReloadableLoggingBootstrap(
                composition.SwappableLogger,
                _dateTimeProvider,
                composition.AsyncResource,
                policy.DynamicLevelPolicy?.GlobalLevelSwitch,
                defaultContext: defaultContext,
                failureSink: _failureSink,
                additionalSinkProviders: _additionalSinkProviders,
                externalSourceRegistrar: null,
                structuredLogger: composition.Logger,
                provenanceGateEnabled: _provenanceGateEnabled);
        }

        return new LoggingBootstrapResult(
            Logger: logger,
            AsyncResource: composition.AsyncResource,
            LevelRegistry: levelRegistry,
            FailureSink: _failureSink,
            DynamicLevelPolicy: policy.DynamicLevelPolicy,
            SwappableLogger: composition.SwappableLogger,
            HotReloadBootstrap: hotReloadBootstrap,
            MetricsRegistry: _metricsRegistry,
            PipelineAccessor: pipelineAccessor,
            MinimumLevel: policy.MinimumLevel,
            ExternalSourceRegistrar: null,
            GatesByAlias: null);
    }

    // Filter a snapshot's named aliases to the set actually present in the
    // current pipeline. Used during replay to skip aliases that disappeared
    // between boots — saves throwing from Register on a missing-alias error
    // that the operator can't easily fix.
    private static string[] FilterKnownAliases(
        IReadOnlyList<string> aliases,
        IReadOnlyDictionary<string, Pipeline.Kernel.GenSourceGatedSink> gates)
    {
        var keep = new System.Collections.Generic.List<string>(aliases.Count);
        foreach (var alias in aliases)
        {
            if (gates.ContainsKey(alias)) keep.Add(alias);
        }
        return keep.ToArray();
    }
}
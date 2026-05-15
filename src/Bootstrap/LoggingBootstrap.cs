#nullable enable

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
        IReadOnlyList<ILogSinkProvider>? additionalSinkProviders = null)
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
    }

    public LoggingBootstrapResult Bootstrap(
        IReadOnlyDictionary<string, object?>? defaultContext = null,
        PipelineAccessor? pipelineAccessor = null)
    {
        var levelRegistry = _levelRegistryFactory.Create();
        var expansionRegistry = _expansionRegistryFactory.Create();
        var transformerRegistry = _transformerRegistryFactory.Create(expansionRegistry);
        var policy = _pipelinePolicyFactory.Create();

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
                structuredLogger: composition.Logger);
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
            KernelDiagnostic: composition.KernelDiagnostic);
    }
}

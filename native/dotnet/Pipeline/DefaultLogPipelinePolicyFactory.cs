#nullable enable

using System.Collections.Generic;
using MMP.Herald.Configuration;
using MMP.Herald.Filters;
using MMP.Herald.Levels;
using MMP.Herald.Metrics;

namespace MMP.Herald.Pipeline;

/// <summary>
/// Default pipeline policy.
/// </summary>
public sealed class DefaultLogPipelinePolicyFactory : ILogPipelinePolicyFactory
{
    private readonly LogLevel _minimumLevel;
    private readonly AsyncLogPolicy _asyncPolicy;
    private readonly BatchingPolicy _batchingPolicy;
    private readonly DynamicLevelPolicy? _dynamicLevelPolicy;
    private readonly ILogFilter? _samplingFilter;
    private readonly PostFilteringPolicy? _postFilteringPolicy;
    private readonly bool _hotReloadEnabled;
    private readonly IReadOnlyList<ILogEventProcessor>? _eventProcessors;
    private readonly PipelineStrategy? _strategy;
    private readonly IReadOnlyList<IConfigurablePipelineDecorator>? _customDecorators;
    private readonly IPipelineDropSink? _dropSink;

    public DefaultLogPipelinePolicyFactory(
        LogLevel minimumLevel,
        AsyncLogPolicy asyncPolicy,
        BatchingPolicy? batchingPolicy = null,
        DynamicLevelPolicy? dynamicLevelPolicy = null,
        ILogFilter? samplingFilter = null,
        PostFilteringPolicy? postFilteringPolicy = null,
        bool hotReloadEnabled = false,
        IReadOnlyList<ILogEventProcessor>? eventProcessors = null,
        PipelineStrategy? strategy = null,
        IReadOnlyList<IConfigurablePipelineDecorator>? customDecorators = null,
        IPipelineDropSink? dropSink = null) {
        _minimumLevel = minimumLevel;
        _asyncPolicy = asyncPolicy;
        _batchingPolicy = batchingPolicy ?? BatchingPolicy.Disabled;
        _dynamicLevelPolicy = dynamicLevelPolicy;
        _samplingFilter = samplingFilter;
        _postFilteringPolicy = postFilteringPolicy;
        _hotReloadEnabled = hotReloadEnabled;
        _eventProcessors = eventProcessors;
        _strategy = strategy;
        _customDecorators = customDecorators;
        _dropSink = dropSink;
    }

    public LogPipelinePolicy Create() {
        return new LogPipelinePolicy(
            MinimumLevel: _minimumLevel,
            AsyncPolicy: _asyncPolicy,
            BatchingPolicy: _batchingPolicy,
            DynamicLevelPolicy: _dynamicLevelPolicy,
            SamplingFilter: _samplingFilter,
            PostFilteringPolicy: _postFilteringPolicy,
            HotReloadEnabled: _hotReloadEnabled,
            EventProcessors: _eventProcessors,
            Strategy: _strategy,
            CustomDecorators: _customDecorators,
            DropSink: _dropSink);
    }
}

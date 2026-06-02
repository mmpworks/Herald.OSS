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
    private readonly IReadOnlyList<ILogFilter>? _filters;
    private readonly PostFilteringPolicy? _postFilteringPolicy;
    private readonly bool _hotReloadEnabled;
    private readonly IReadOnlyList<ILogEventProcessor>? _eventProcessors;
    private readonly PipelineStrategy? _strategy;
    private readonly IReadOnlyList<IConfigurablePipelineDecorator>? _customDecorators;
    private readonly IPipelineDropSink? _dropSink;
    private readonly bool _allowExternalEventInjection;

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
        IPipelineDropSink? dropSink = null,
        // The AND-chain filter list (sampling + throttling + adaptive + custom). Prefer
        // this over the single samplingFilter slot; the slot stays for back-compat with
        // callers that pass one filter, and the policy's EffectiveFilters folds either.
        IReadOnlyList<ILogFilter>? filters = null,
        // Per-pipeline external-event-injection consent. Trailing optional so the
        // many positional callers of this ctor keep compiling; the JSON-driven
        // bootstrap passes it explicitly. See docs/design/external-event-injection-switch.md.
        bool allowExternalEventInjection = false) {
        _minimumLevel = minimumLevel;
        _asyncPolicy = asyncPolicy;
        _batchingPolicy = batchingPolicy ?? BatchingPolicy.Disabled;
        _dynamicLevelPolicy = dynamicLevelPolicy;
        _samplingFilter = samplingFilter;
        _filters = filters;
        _postFilteringPolicy = postFilteringPolicy;
        _hotReloadEnabled = hotReloadEnabled;
        _eventProcessors = eventProcessors;
        _strategy = strategy;
        _customDecorators = customDecorators;
        _dropSink = dropSink;
        _allowExternalEventInjection = allowExternalEventInjection;
    }

    public LogPipelinePolicy Create() {
        return new LogPipelinePolicy(
            MinimumLevel: _minimumLevel,
            AsyncPolicy: _asyncPolicy,
            BatchingPolicy: _batchingPolicy,
            DynamicLevelPolicy: _dynamicLevelPolicy,
            SamplingFilter: _samplingFilter,
            Filters: _filters,
            PostFilteringPolicy: _postFilteringPolicy,
            HotReloadEnabled: _hotReloadEnabled,
            EventProcessors: _eventProcessors,
            Strategy: _strategy,
            CustomDecorators: _customDecorators,
            DropSink: _dropSink,
            AllowExternalEventInjection: _allowExternalEventInjection);
    }
}

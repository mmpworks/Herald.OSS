#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MMP.Herald.Events;
using MMP.Herald.Failures;
using MMP.Herald.Levels;
using MMP.Herald.Metrics;
using MMP.Herald.Routing;

namespace MMP.Herald.Filters;
/// <summary>
/// Gatekeeper that forwards events only when all filters allow them.
/// Immutable after construction. To change filters, mutate the QuickLogBuilder
/// and call result.RebuildFrom(builder).
/// </summary>
public sealed class FilteringLogger : ILogger, Pipeline.IComponentMetadata, ISinkChainLevelIntrospection
{
    private readonly ILogger _next;
    private readonly IReadOnlyList<ILogFilter> _filters;
    private readonly IPipelineDropSink _dropSink;
    // Parallel array of reason tags indexed by _filters position. Computed once
    // at construction so the reject path is a single indexed read — no per-event
    // type check. Keeps the hot accepted path allocation-free.
    private readonly string[] _filterReasons;

    public FilteringLogger(ILogger next, IReadOnlyList<ILogFilter> filters)
        : this(next, filters, dropSink: null) { }

    public FilteringLogger(ILogger next, IReadOnlyList<ILogFilter> filters, IPipelineDropSink? dropSink)
    {
        _next = next;
        _filters = filters;
        _dropSink = dropSink ?? NullPipelineDropSink.Instance;
        _filterReasons = new string[filters.Count];
        for (var i = 0; i < filters.Count; i++)
        {
            _filterReasons[i] = ClassifyReason(filters[i]);
        }
    }

    public void Log(LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);

        // Cognitive complexity note: the reject path is hoisted out of the
        // hot loop so the accepted path stays a straight-line branch. Index
        // walk mirrors _filterReasons so the reason tag is a single array
        // read rather than another pattern match.
        for (var i = 0; i < _filters.Count; i++)
        {
            if (!_filters[i].Allow(logEvent))
            {
                _dropSink.RecordDrop(sinkName: null, _filterReasons[i]);
                // Mirror the drop on the rejected-event broadcaster so the
                // dashboard's LiveLogCapture can render the dropped entry
                // dimmed alongside accepted entries. The publish is a single
                // delegate-null branch when no subscriber is wired.
                RejectedEventBroadcaster.Publish(logEvent, _filterReasons[i]);
                return;
            }
        }

        _next.Log(logEvent);
    }

    // Filters are synchronous predicates, so the async path reuses them as-is.
    // Only the forward into _next changes, so downstream async sinks keep their
    // non-blocking path when this filter admits the event.
    public ValueTask LogAsync(LogEvent logEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(logEvent);

        for (var i = 0; i < _filters.Count; i++)
        {
            if (!_filters[i].Allow(logEvent))
            {
                _dropSink.RecordDrop(sinkName: null, _filterReasons[i]);
                RejectedEventBroadcaster.Publish(logEvent, _filterReasons[i]);
                return ValueTask.CompletedTask;
            }
        }

        return _next.LogAsync(logEvent, cancellationToken);
    }

    // Maps the runtime filter type to a canonical DropReasons string. Unknown
    // plugin filter types fall through to DropReasons.Filter so the metric
    // still advances with an honest catch-all label rather than disappearing.
    private static string ClassifyReason(ILogFilter filter) => filter switch
    {
        LevelFilter => DropReasons.Level,
        SwitchableLevelFilter => DropReasons.Level,
        SamplingFilter => DropReasons.Sampling,
        CompositeSamplingFilter => DropReasons.Sampling,
        ConsistentSamplingFilter => DropReasons.Sampling,
        PriorityWindowSamplingFilter => DropReasons.Sampling,
        Addons.MetricExtraction.AdaptiveSamplingFilter => DropReasons.Sampling,
        ThrottlingFilter => DropReasons.Throttling,
        Predicates.PredicateFilter => DropReasons.Predicate,
        Predicates.CompiledPredicateFilter => DropReasons.Predicate,
        _ => DropReasons.Filter,
    };

    // -- Inspection --

    /// <summary>The filters applied by this gatekeeper.</summary>
    public IReadOnlyList<ILogFilter> Filters => _filters;

    /// <summary>The downstream logger events pass to after filtering.</summary>
    public ILogger Inner => _next;

    /// <summary>The minimum level from the LevelFilter or SwitchableLevelFilter, if present.</summary>
    public string? MinimumLevelKey
    {
        get
        {
            foreach (var f in _filters)
            {
                if (f is LevelFilter lf) return lf.MinimumLevel.Key;
                if (f is SwitchableLevelFilter sf) return sf.GlobalSwitch.MinimumLevel.Key;
            }
            return null;
        }
    }

    // Chain-level introspection: a LevelFilter / SwitchableLevelFilter on this
    // gate is the authoritative floor for this node. If neither is present,
    // the floor comes from whatever lies behind this gate. Sampling / throttling
    // / predicate filters are orthogonal to level and intentionally skipped.
    public LogLevel? GetMinimumLevel(ILogLevelRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        foreach (var f in _filters)
        {
            if (f is LevelFilter lf) return lf.MinimumLevel;
            if (f is SwitchableLevelFilter sf) return sf.GlobalSwitch.MinimumLevel;
        }

        return (_next as ISinkChainLevelIntrospection)?.GetMinimumLevel(registry);
    }

    /// <summary>
    /// Build a config schema showing ALL possible filter predicates.
    /// Each filter type has an enable toggle + its config fields.
    /// Active filters show their live values; inactive ones show defaults.
    /// </summary>
    public System.Collections.Generic.IReadOnlyList<Routing.SinkConfigField> BuildFilterSchema()
    {
        var fields = new System.Collections.Generic.List<Routing.SinkConfigField>();

        // Find active filters by type
        LevelFilter? levelFilter = null;
        SwitchableLevelFilter? switchableFilter = null;
        SamplingFilter? samplingFilter = null;
        ThrottlingFilter? throttlingFilter = null;
        Addons.MetricExtraction.AdaptiveSamplingFilter? adaptiveFilter = null;

        foreach (var f in _filters)
        {
            switch (f)
            {
                case LevelFilter lf: levelFilter = lf; break;
                case SwitchableLevelFilter sf: switchableFilter = sf; break;
                case Addons.MetricExtraction.AdaptiveSamplingFilter asf: adaptiveFilter = asf; break;
                case SamplingFilter sf2: samplingFilter = sf2; break;
                case ThrottlingFilter tf: throttlingFilter = tf; break;
            }
        }

        // ── Level Filter (always present — not optional) ──
        fields.Add(Routing.SinkConfigField.String("minimumLevel",
            switchableFilter?.GlobalSwitch.MinimumLevel.Key ?? levelFilter?.MinimumLevel.Key ?? "",
            "Minimum level",
            "Events below this level are dropped. All sinks downstream only see events at or above this level."));
        if (switchableFilter is not null)
        {
            fields.Add(Routing.SinkConfigField.Bool("hasCategoryOverrides", switchableFilter.CategoryMap is not null,
                "Category overrides active",
                "Specific log categories have different minimum levels. Example: 'Combat' at debug, global at warn."));
        }

        // ── Sampling Filter ──
        fields.Add(Routing.SinkConfigField.Bool("enableSampling", samplingFilter is not null, "Sampling Filter",
            "Randomly drops a percentage of events to reduce volume. Keep 1 in every N events.",
            group: "sampling"));
        fields.Add(Routing.SinkConfigField.Int("sampleRate", samplingFilter?.SampleRate ?? 10, "Sample rate (1 in N)",
            "Keep 1 in every N events. rate=10 means ~10% pass through. Set to 1 to keep all.",
            group: "sampling"));

        // ── Throttling Filter ──
        fields.Add(Routing.SinkConfigField.Bool("enableThrottling", throttlingFilter is not null, "Throttling Filter",
            "Rate limits events to a maximum count per time window. Excess events are dropped.",
            group: "throttling"));
        fields.Add(Routing.SinkConfigField.Int("maxEventsPerWindow", throttlingFilter?.MaxEventsPerWindow ?? 1000, "Max events per window",
            "Maximum events allowed in each time window. Events beyond this are dropped.",
            group: "throttling"));
        fields.Add(Routing.SinkConfigField.Int("windowDurationMs", throttlingFilter is not null ? (int)throttlingFilter.WindowDuration.TotalMilliseconds : 1000, "Window duration (ms)",
            "Time window in milliseconds over which the event count is tracked.",
            group: "throttling"));

        // ── Adaptive Sampling ──
        fields.Add(Routing.SinkConfigField.Bool("enableAdaptiveSampling", adaptiveFilter is not null, "Adaptive Sampling",
            "Auto-adjusts sampling rate based on error volume. Captures everything during error spikes.",
            group: "adaptiveSampling"));
        fields.Add(Routing.SinkConfigField.Int("normalSampleRate", adaptiveFilter?.NormalSampleRate ?? 10, "Normal sample rate (1 in N)",
            "Under normal conditions, keep 1 in every N events.",
            group: "adaptiveSampling"));
        fields.Add(Routing.SinkConfigField.Int("errorThreshold", adaptiveFilter?.ErrorThreshold ?? 5, "Error threshold",
            "Error count within the window that triggers full capture (sample rate 1).",
            group: "adaptiveSampling"));
        if (adaptiveFilter is not null)
        {
            fields.Add(Routing.SinkConfigField.Bool("isEscalated", adaptiveFilter.IsEscalated, "Currently escalated",
                "Whether the sampler has detected an error spike and is capturing all events.",
                group: "adaptiveSampling"));
        }

        return fields;
    }

    // -- IComponentMetadata --
    internal static readonly Configuration.PipelineStepRules StepRules = new(
        PreferBefore: ["fanOut"]);
    string Pipeline.IComponentMetadata.ComponentName => "filtering";
    string Pipeline.IComponentMetadata.DisplayName => "Level Filtering";
    string Pipeline.IComponentMetadata.Description => "Applies minimum level and category filters to drop unwanted events.";
    string Pipeline.IComponentMetadata.Help => "Evaluates each event against the filter chain. All filters must pass for the event to continue. Position matters: before Async (FilterEarly) saves queue capacity; after Async (Default) allows the Flight Recorder to buffer filtered events.";
    Pipeline.VendorInfo Pipeline.IComponentMetadata.Vendor => Pipeline.VendorInfo.MMP;
    Configuration.PipelineStepRules Pipeline.IComponentMetadata.Rules => StepRules;
    internal static readonly System.Collections.Generic.IReadOnlyList<Routing.SinkConfigField> DefaultSchema =
    [
        Routing.SinkConfigField.String("minimumLevel", "", "Minimum level",
            "Events below this level are dropped. All sinks downstream only see events at or above this level."),
        Routing.SinkConfigField.Bool("enableSampling", false, "Sampling Filter",
            "Randomly drops a percentage of events to reduce volume. Keep 1 in every N events.",
            group: "sampling"),
        Routing.SinkConfigField.Int("sampleRate", 10, "Sample rate (1 in N)",
            "Keep 1 in every N events. rate=10 means ~10% pass through. Set to 1 to keep all.",
            group: "sampling"),
        Routing.SinkConfigField.Bool("enableThrottling", false, "Throttling Filter",
            "Rate limits events to a maximum count per time window. Excess events are dropped.",
            group: "throttling"),
        Routing.SinkConfigField.Int("maxEventsPerWindow", 1000, "Max events per window",
            "Maximum events allowed in each time window. Events beyond this are dropped.",
            group: "throttling"),
        Routing.SinkConfigField.Int("windowDurationMs", 1000, "Window duration (ms)",
            "Time window in milliseconds over which the event count is tracked.",
            group: "throttling"),
        Routing.SinkConfigField.Bool("enableAdaptiveSampling", false, "Adaptive Sampling",
            "Auto-adjusts sampling rate based on error volume. Captures everything during error spikes.",
            group: "adaptiveSampling"),
        Routing.SinkConfigField.Int("normalSampleRate", 10, "Normal sample rate (1 in N)",
            "Under normal conditions, keep 1 in every N events.",
            group: "adaptiveSampling"),
        Routing.SinkConfigField.Int("errorThreshold", 5, "Error threshold",
            "Error count within the window that triggers full capture (sample rate 1).",
            group: "adaptiveSampling"),
    ];
    System.Collections.Generic.IReadOnlyList<Routing.SinkConfigField> Pipeline.IComponentMetadata.ConfigurationSchema => BuildFilterSchema();
}
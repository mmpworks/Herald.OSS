#nullable enable

using System.Collections.Generic;
using MMP.Herald.Configuration.Json;
using MMP.Herald.Predicates;

namespace MMP.Herald.Quick;

// Internal read-only accessors exposed to the serializer registry (see
// MMP.Herald.Quick.Serializers). These surface just the private fields the
// per-step and per-sink serializers need to render JSON config without
// widening the public API of QuickLogBuilder.
//
// C# 12 note: kept as explicit property accessors for .NET 8 compatibility.
// Revisit on C# 14 — primary-constructor / field-backed properties may let us
// collapse these into the main class without the separate partial file.
public sealed partial class QuickLogBuilder
{
    // -- Console sink state --
    internal bool IncludeConsole => _includeConsole;
    internal string? ConsoleMinLevel => _consoleMinLevel;

    // -- Null sink state --
    internal bool IncludeNullSink => _includeNullSink;
    internal string? NullSinkMinLevel => _nullSinkMinLevel;

    // -- File sink state --
    internal string? LogFilePath => _logFilePath;
    internal string? LogFileMinLevel => _logFileMinLevel;
    internal string LogFileKind => _logFileKind;
    internal JsonFileRollingConfig? LogFileRolling => _logFileRolling;

    // -- Network sinks --
    internal IReadOnlyList<NetworkSinkConfig> NetworkSinksView => _networkSinks;

    // -- Async step state --
    internal bool AsyncEnabled => _asyncEnabled;
    internal int AsyncCapacity => _asyncCapacity;
    internal string AsyncDropStrategy => _asyncDropStrategy;
    internal bool AsyncDeferRendering => _asyncDeferRendering;

    // -- Batching step state --
    internal bool BatchingEnabled => _batchingEnabled;
    internal int BatchMaxSize => _batchMaxSize;
    internal int BatchDelayMs => _batchDelayMs;

    // -- Filtering step state --
    internal string MinimumLevelValue => _minimumLevel;
    internal bool DynamicLevelsEnabled => _dynamicLevelsEnabled;
    internal IReadOnlyDictionary<string, string>? CategoryLevelOverridesView => _categoryLevelOverrides;
    internal int SamplingRateValue => _samplingRate;

    /// <summary>
    /// The accumulated sampling/throttling/adaptive rules (null when none configured).
    /// Test + serializer surface for the multi-filter seam: lets a round-trip test assert
    /// that WithSampling/WithThrottling/WithAdaptiveSampling produced the expected
    /// JsonSamplingRule shapes before Build composes them into the CompositeSamplingFilter.
    /// </summary>
    internal System.Collections.Generic.IReadOnlyList<Configuration.Json.JsonSamplingRule>? SamplingRulesView => _samplingRules;

    // -- Swappable (hot reload) step state --
    internal int HotReloadDebounceMsValue => _hotReloadDebounceMs;

    // -- Flight-recorder step state --
    internal bool FlightRecorderEnabled => _flightRecorderEnabled;
    internal int FlightRecorderBufferSize => _flightRecorderBufferSize;
    internal string? FlightRecorderMinLevel => _flightRecorderMinLevel;
    internal string? FlightRecorderTriggerLevel => _flightRecorderTriggerLevel;

    // -- Post-filtering step state --
    internal bool PostFilteringEnabled => _postFilteringEnabled;
    internal PredicateSpec? PostFilteringTrigger => _postFilteringTrigger;
    internal PredicateSpec? PostFilteringNormal => _postFilteringNormal;
    internal PredicateSpec? PostFilteringEscalated => _postFilteringEscalated;
    internal int PostFilteringMaxBatchSize => _postFilteringMaxBatchSize;
    internal int PostFilteringMaxBatchDelayMs => _postFilteringMaxBatchDelayMs;

    // -- Helpers shared by serializers --

    /// <summary>
    /// Default route predicate for "normal" sinks. When channels are configured,
    /// events carrying a "channel" context key are excluded so they do not
    /// duplicate into the default output.
    /// </summary>
    internal PredicateSpec ComputeDefaultRoutePredicate() =>
        Channels.Items.Count > 0
            ? new PredicateSpec.Not(new PredicateSpec.ContextHasKey("channel"))
            : new PredicateSpec.True();

    /// <summary>Bridge sink naming — exposed for the bridge serializer.</summary>
    internal static string ComputeBridgeSinkKind(int index) => BridgeSinkKind(index);
}

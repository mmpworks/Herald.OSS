#nullable enable

using System;
using System.Collections.Generic;
using MMP.Herald.Events;
using MMP.Herald.Levels;
using MMP.Herald.Filters;

namespace MMP.Herald.Addons.GamePerformance;

/// <summary>
/// Pipeline decorator that buffers recent below-minimum events in a ring buffer.
/// When a trigger-level event (default: error) arrives, the buffer contents
/// flush to the sink pipeline before the trigger event itself, giving you
/// the full debug trail leading up to the error.
///
/// Sits outside the FilteringLogger in the pipeline:
///   AsyncLogger -> FlightRecorderLogger -> FilteringLogger -> SinkRouter
///
/// Events at or above minimum level pass through normally.
/// Events below minimum level go into the ring buffer instead of being discarded.
/// When a trigger event fires, the buffer drains to the inner pipeline.
///
/// Usage via QuickLogBuilder:
///   .WithFlightRecorder(bufferSize: 200, triggerLevel: "error")
/// </summary>
public sealed class FlightRecorderLogger : ILogger, MMP.Herald.Pipeline.IComponentMetadata
{
    private readonly ILogger _inner;
    private readonly ILogLevelRegistry _levelRegistry;
    private readonly LogLevel _minimumLevel;
    private readonly LogLevel _triggerLevel;
    private readonly int _bufferSize;
    private readonly object _sync = new();
    private readonly LinkedList<LogEvent> _buffer = new();

    public FlightRecorderLogger(
        ILogger inner,
        ILogLevelRegistry levelRegistry,
        LogLevel minimumLevel,
        LogLevel triggerLevel,
        int bufferSize = 200)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _levelRegistry = levelRegistry ?? throw new ArgumentNullException(nameof(levelRegistry));
        _minimumLevel = minimumLevel ?? throw new ArgumentNullException(nameof(minimumLevel));
        _triggerLevel = triggerLevel ?? throw new ArgumentNullException(nameof(triggerLevel));

        if (bufferSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(bufferSize), "Buffer size must be positive.");

        _bufferSize = bufferSize;
    }

    public void Log(LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);

        var isAboveMinimum = _levelRegistry.IsAtOrAbove(logEvent.Level, _minimumLevel);
        var isTrigger = _levelRegistry.IsAtOrAbove(logEvent.Level, _triggerLevel);

        if (isTrigger)
        {
            FlushBufferThenForward(logEvent);
            return;
        }

        if (isAboveMinimum)
        {
            // Normal event above minimum - pass through directly
            _inner.Log(logEvent);
            return;
        }

        // Below minimum - buffer it instead of discarding
        BufferEvent(logEvent);
    }

    private void BufferEvent(LogEvent logEvent)
    {
        lock (_sync)
        {
            _buffer.AddLast(logEvent);

            while (_buffer.Count > _bufferSize)
            {
                _buffer.RemoveFirst();
            }
        }
    }

    private void FlushBufferThenForward(LogEvent triggerEvent)
    {
        List<LogEvent> snapshot;

        lock (_sync)
        {
            snapshot = new List<LogEvent>(_buffer);
            _buffer.Clear();
        }

        // Replay buffered events in chronological order
        foreach (var buffered in snapshot)
        {
            _inner.Log(buffered);
        }

        // Then the trigger event itself
        _inner.Log(triggerEvent);
    }

    // -- IComponentMetadata (single source of truth) --

    internal static readonly HeraldEdition MinEdition = HeraldEdition.Community;
    /// <summary>Pipeline placement rules: must be before filtering to capture filtered events.</summary>
    internal static readonly Configuration.PipelineStepRules StepRules = new(
        RequiresAfter: ["filtering"],
        PreferBefore: ["filtering"],
        PreferAfter: ["batching"],
        MoreInfo: new System.Collections.Generic.Dictionary<string, string> {
            ["requiresAfter"] = "The Flight Recorder buffers events that would be filtered out. Without Filtering after it, there's nothing to buffer against.",
            ["preferBefore"] = "The recorder must see events before they are filtered to capture the full context leading up to failures.",
            ["preferAfter"] = "Placing after Batching means it captures batch boundaries for post-mortem analysis."
        });

    string Pipeline.IComponentMetadata.ComponentName => "flightRecorder";
    string Pipeline.IComponentMetadata.DisplayName => "Flight Recorder";
    string Pipeline.IComponentMetadata.Description => "Buffers recent events for flight-recorder-style dump on error.";
    string Pipeline.IComponentMetadata.Help => "Keeps a ring buffer of below-minimum events. On trigger (error), flushes buffer before the trigger. Like an aircraft black box. MUST be placed before Level Filtering to capture events that would otherwise be dropped.";
    Pipeline.VendorInfo Pipeline.IComponentMetadata.Vendor => Pipeline.VendorInfo.MMP;
    internal static readonly System.Collections.Generic.IReadOnlyList<Routing.SinkConfigField> DefaultSchema =
    [
        Routing.SinkConfigField.Int("bufferSize", Configuration.FlightRecorderCommunityShape.BufferSize, "Ring buffer capacity",
            "Maximum number of events the circular buffer holds. When full, oldest events are overwritten. On error trigger, the entire buffer is flushed to show what happened leading up to the failure. Community is locked to 200; Pro/Enterprise unlocks arbitrary sizes."),
        Routing.SinkConfigField.String("minimumLevel", "", "Minimum level (optional)",
            "Events below this level go into the buffer instead of passing through. Leave blank to inherit the pipeline's minimum — the recorder then captures everything the pipeline would otherwise drop. Setting a value here requires Pro/Enterprise."),
        Routing.SinkConfigField.String("triggerLevel", "error", "Trigger level",
            "Events at or above this level cause the buffer to drain to the inner pipeline before the trigger event is delivered. Default 'error' is Community; any other trigger requires Pro/Enterprise."),
    ];
    System.Collections.Generic.IReadOnlyList<Routing.SinkConfigField> Pipeline.IComponentMetadata.ConfigurationSchema =>
    [
        DefaultSchema[0] with { DefaultValue = _bufferSize },
        DefaultSchema[1] with { DefaultValue = _minimumLevel.Key },
        DefaultSchema[2] with { DefaultValue = _triggerLevel.Key },
    ];
    Configuration.PipelineStepRules Pipeline.IComponentMetadata.Rules => StepRules;
    HeraldEdition Pipeline.IComponentMetadata.MinimumEdition => MinEdition;
}

#nullable enable

using System;
using System.Collections.Generic;
using MMP.Herald.Events;
using MMP.Herald.Metrics;

namespace MMP.Herald.Pipeline;

/// <summary>
/// Pipeline decorator that applies a chain of event processors between
/// filtering and sinks. Immutable after construction. To change processors,
/// mutate the QuickLogBuilder and call result.RebuildFrom(builder).
/// <para>
/// A processor may return <c>null</c> to drop the event entirely. When that
/// happens the decorator records one drop with <see cref="DropReasons.Redaction"/>
/// against the optional <see cref="IPipelineDropSink"/> and short-circuits the
/// chain — no later processor and no downstream sink sees the event. The
/// <c>redaction</c> reason is the only producer of dropped events from this
/// decorator today, so the tag is hard-coded at this layer.
/// </para>
/// </summary>
public sealed class EventProcessingLogger : ILogger, IComponentMetadata
{
    private readonly ILogger _next;
    private readonly IReadOnlyList<ILogEventProcessor> _processors;
    private readonly IPipelineDropSink _dropSink;

    public EventProcessingLogger(
        ILogger next,
        IReadOnlyList<ILogEventProcessor> processors)
        : this(next, processors, dropSink: null) { }

    public EventProcessingLogger(
        ILogger next,
        IReadOnlyList<ILogEventProcessor> processors,
        IPipelineDropSink? dropSink) {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _processors = processors ?? throw new ArgumentNullException(nameof(processors));
        _dropSink = dropSink ?? NullPipelineDropSink.Instance;
    }

    public void Log(LogEvent logEvent) {
        ArgumentNullException.ThrowIfNull(logEvent);

        var processed = logEvent;

        // Cognitive complexity note: the drop branch is inside the loop because
        // any processor may decide to drop, not just the last one. Returning
        // early on null keeps the post-loop forward path branch-free.
        foreach (var processor in _processors)
        {
            var result = processor.Process(processed);
            if (result is null)
            {
                _dropSink.RecordDrop(sinkName: null, DropReasons.Redaction);
                return;
            }
            processed = result;
        }

        _next.Log(processed);
    }

    // -- Inspection --

    /// <summary>The event processors applied to each event.</summary>
    public IReadOnlyList<ILogEventProcessor> Processors => _processors;

    /// <summary>The downstream logger processed events pass to.</summary>
    public ILogger Inner => _next;

    // -- IComponentMetadata --
    internal static readonly Configuration.PipelineStepRules StepRules = new(
        PreferBefore: ["fanOut"]);
    string IComponentMetadata.ComponentName => "eventProcessing";
    string IComponentMetadata.DisplayName => "Event Processing";
    string IComponentMetadata.Description => "Runs event processors that transform or enrich events before delivery.";
    string IComponentMetadata.Help => "Applies ILogEventProcessor chain sequentially. Processors can modify properties, redact PII, extract metrics, validate schemas.";
    VendorInfo IComponentMetadata.Vendor => VendorInfo.MMP;
    Configuration.PipelineStepRules IComponentMetadata.Rules => StepRules;
    internal static readonly System.Collections.Generic.IReadOnlyList<Routing.SinkConfigField> DefaultSchema =
    [
        Routing.SinkConfigField.Int("processorCount", 0, "Number of registered processors",
            "How many ILogEventProcessor implementations are chained. Processors run in registration order and can transform properties, redact PII, extract metrics, or drop events by returning null."),
    ];
    System.Collections.Generic.IReadOnlyList<Routing.SinkConfigField> IComponentMetadata.ConfigurationSchema =>
    [
        DefaultSchema[0] with { DefaultValue = _processors.Count },
    ];
}

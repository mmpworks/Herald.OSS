#nullable enable

using MMP.Herald.Events;

namespace MMP.Herald.Pipeline;

/// <summary>
/// Transforms a log event after it passes all filters but before it reaches sinks.
/// Event processors form a chain: each receives the output of the previous processor.
///
/// Use cases:
/// - Event-level redaction (transform property values before any sink sees them)
/// - Attribute enrichment or removal post-filter
/// - Metric extraction from events
/// - TraceId/SpanId injection
/// - Compiled redaction with pre-built delegates
///
/// This is Herald's equivalent of structlog's processor pipeline, operating on
/// source data rather than rendered output.
///
/// <para>
/// <b>Drop semantics.</b> A processor may return <c>null</c> to drop the event
/// from the pipeline. The host (<see cref="EventProcessingLogger"/>) records the
/// drop with a <c>redaction</c> reason tag and short-circuits the chain — no
/// downstream processor or sink sees the event. Returning <c>null</c> is the
/// canonical way for redaction rules to express "this event must not be emitted."
/// </para>
/// </summary>
public interface ILogEventProcessor
{
    /// <summary>
    /// Transform the event, or return <c>null</c> to drop it from the pipeline.
    /// Most processors return a non-null value (the original event or a modified
    /// copy); only processors that intentionally short-circuit emission return
    /// <c>null</c>.
    /// </summary>
    LogEvent? Process(LogEvent logEvent);
}

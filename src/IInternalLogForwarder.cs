#nullable enable

using MMP.Herald.Events;

namespace MMP.Herald;

/// <summary>
/// Internal Herald-to-Herald forwarding port. Implemented by pipeline entry
/// points (today only <see cref="Pipeline.StructuredLogger"/>) that can accept a
/// pre-built event another Herald pipeline already built and enriched.
///
/// <para>
/// <b>Why this exists.</b> A forwarder that holds an <see cref="ILogger"/> slot
/// — <see cref="Pipeline.PipelineBridge"/> is the canonical case — cannot tell,
/// from the static type alone, whether the slot holds a foreign non-Herald
/// <see cref="ILogger"/> or a sibling Herald pipeline's
/// <see cref="Pipeline.StructuredLogger"/> (which a hub-and-spoke setup passes
/// via <c>result.Logger</c>). Calling <see cref="ILogger.Log(LogEvent)"/> on a
/// <see cref="Pipeline.StructuredLogger"/> routes through the explicit
/// <c>ILogger.Log</c> member, which is the PUBLIC external-event-injection
/// consent boundary. With consent OFF (the default) a legitimate Herald-to-Herald
/// forward would be dropped and mis-reported as an external injection.
/// </para>
///
/// <para>
/// A forwarder that knows it is doing internal Herald-to-Herald forwarding
/// pattern-tests its target for this interface and calls
/// <see cref="ForwardPrebuilt(LogEvent)"/> when present — bypassing the consent
/// gate, correctly, because the event originated inside a Herald pipeline. A
/// genuinely external non-Herald <see cref="ILogger"/> target does not implement
/// this interface, so the forwarder falls back to normal
/// <see cref="ILogger.Log(LogEvent)"/> dispatch.
/// </para>
///
/// <para>
/// <b>Intentionally internal.</b> This is a Herald-internal trust signal, not a
/// public extension point. Keeping it <c>internal</c> means a third-party
/// <see cref="ILogger"/> cannot opt itself into gate-bypass by implementing the
/// interface — only Herald's own pipeline entry points carry it.
/// </para>
/// </summary>
internal interface IInternalLogForwarder
{
    /// <summary>
    /// Accept a pre-built event from a sibling Herald pipeline, bypassing the
    /// external-event-injection consent gate. The event is already finalised
    /// (built, stamped, enriched, rendered) by the originating pipeline.
    /// </summary>
    void ForwardPrebuilt(LogEvent e);
}

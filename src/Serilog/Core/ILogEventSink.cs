#nullable enable

using MMP.Herald.Serilog.Events;

namespace MMP.Herald.Serilog.Core;

/// <summary>
/// Serilog-compatible sink contract. A consumer implements this interface so their
/// sink compiles against <c>MMP.Herald.Serilog</c> without change after a package swap.
///
/// <para>
/// Usage scope: <strong>source-compiled, user-authored sinks only.</strong> A pre-compiled
/// NuGet sink (Seq, MSSql, Datadog) targets <c>Serilog.Core.ILogEventSink</c> from the
/// Serilog assembly — it will not satisfy this interface due to the strong-name identity
/// boundary. That is a deliberate hard-wall, not a bug.
/// </para>
///
/// <para>
/// Wire path: <c>WriteTo.Sink(ILogEventSink)</c> wraps the implementation in a
/// <c>SerilogSinkAdapter</c> and registers it on the Herald heap-sink path. Each event
/// arrives as a <see cref="LogEvent"/> — the P1 mirror that exposes the same surface as
/// <c>Serilog.Events.LogEvent</c>.
/// </para>
/// </summary>
public interface ILogEventSink
{
    /// <summary>
    /// Emit the provided log event to the sink.
    /// </summary>
    /// <param name="logEvent">
    /// The mirrored Serilog-shaped event. Never null when called via the Herald
    /// adapter, but implementors should guard defensively for direct-call safety.
    /// </param>
    void Emit(LogEvent logEvent);
}

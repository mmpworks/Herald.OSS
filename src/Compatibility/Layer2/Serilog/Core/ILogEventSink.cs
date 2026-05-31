#nullable enable

// Layer-2 mirror of Serilog.Core.ILogEventSink.
// Uses Serilog.Events.LogEvent (the L2 wrapper type) so consumer sink classes
// compiled against this assembly see the correct type in their method signatures.
//
// Layer-1 twin: MMP.Herald.Serilog.Core.ILogEventSink

namespace Serilog.Core;

/// <summary>
/// Serilog-compatible sink contract exported under the <c>Serilog</c> assembly identity.
/// A consumer's <c>class MySink : Serilog.Core.ILogEventSink</c> compiles unchanged
/// after swapping the Serilog package for this assembly.
/// </summary>
public interface ILogEventSink
{
    /// <summary>Emit the provided log event to the sink.</summary>
    void Emit(Events.LogEvent logEvent);
}

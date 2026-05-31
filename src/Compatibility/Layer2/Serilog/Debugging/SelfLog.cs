#nullable enable

// Layer-2 mirror of Serilog.Debugging.SelfLog.
// Forwards all static methods to the Layer-1 MMP.Herald.Serilog.Debugging.SelfLog.
// The Layer-1 implementation holds the actual writer state and is called by the
// sink adapter when it swallows an exception in normal (non-audit) mode.
//
// Layer-1 twin: MMP.Herald.Serilog.Debugging.SelfLog

using System;
using L1SelfLog = MMP.Herald.Serilog.Debugging.SelfLog;

namespace Serilog.Debugging;

/// <summary>
/// Receives diagnostic messages from Serilog infrastructure such as failed sinks.
/// Exported under the <c>Serilog</c> assembly identity.
///
/// <para>
/// Usage: <c>SelfLog.Enable(Console.Error.WriteLine);</c> — any swallowed sink
/// exception produces a formatted message on the writer instead of disappearing.
/// </para>
/// </summary>
public static class SelfLog
{
    /// <summary>
    /// Enable self-logging. Swallowed sink errors will be written to <paramref name="writer"/>.
    /// </summary>
    public static void Enable(Action<string> writer) => L1SelfLog.Enable(writer);

    /// <summary>Disable self-logging. Swallowed errors are silently dropped again.</summary>
    public static void Disable() => L1SelfLog.Disable();
}

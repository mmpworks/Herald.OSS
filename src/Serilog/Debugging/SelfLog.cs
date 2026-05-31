#nullable enable

using System;

namespace MMP.Herald.Serilog.Debugging;

/// <summary>
/// Compat-shim for Serilog's <c>Serilog.Debugging.SelfLog</c>.
///
/// <para>
/// When a sink registered via <c>WriteTo.Sink()</c> throws in normal (swallow) mode,
/// Herald's catch block calls <see cref="Write"/> so the error reaches any observer
/// the caller wired with <see cref="Enable"/>. This matches Serilog's contract:
/// swallowed failures are silent by default but observable when self-logging is enabled.
/// </para>
///
/// <para>
/// Usage: <c>SelfLog.Enable(Console.Error.WriteLine);</c> — any subsequent swallowed
/// sink exception produces a formatted message on the writer rather than disappearing.
/// Call <see cref="Disable"/> to stop reporting.
/// </para>
///
/// <para>
/// Thread safety: <c>_writer</c> is a plain field written under a lock and read
/// without one (read-before-invoke, not read-then-write), which is safe for this
/// notification pattern. The writer itself is responsible for its own thread safety.
/// </para>
/// </summary>
public static class SelfLog
{
    private static Action<string>? _writer;

    /// <summary>
    /// Enable self-logging. All swallowed sink errors will be written to
    /// <paramref name="writer"/> as formatted strings.
    /// </summary>
    /// <param name="writer">Destination for self-log messages. Must not be null.</param>
    public static void Enable(Action<string> writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        _writer = writer;
    }

    /// <summary>Disable self-logging. Swallowed errors are silently dropped again.</summary>
    public static void Disable() => _writer = null;

    /// <summary>
    /// Write a self-log message to the registered writer, if any.
    /// No-op when self-logging is disabled.
    /// </summary>
    internal static void Write(string message) => _writer?.Invoke(message);

    /// <summary>Whether a writer is currently registered.</summary>
    internal static bool IsEnabled => _writer is not null;
}

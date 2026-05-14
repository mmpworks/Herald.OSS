#nullable enable

using MMP.Herald.Events;
using MMP.Herald.Pipeline.Kernel;

namespace MMP.Herald.Formatting;
/// <summary>
/// Converts an event to a textual representation.
/// </summary>
public interface ILogFormatter
{
    /// <summary>
    /// Format a heap-allocated <see cref="LogEvent"/>. Used by
    /// <see cref="Pipeline.WriterLogger"/> on the legacy path when the
    /// pipeline is not kernel-eligible (e.g. AsyncLogger is in the chain).
    /// </summary>
    string Format(LogEvent logEvent);

    /// <summary>
    /// Format from a stack-allocated <see cref="LogEventBuffer"/>. The
    /// default implementation materialises to <see cref="LogEvent"/> so
    /// existing formatters work unchanged; kernel-aware formatters (the
    /// built-in <see cref="PlainTextFormatter"/> and
    /// <see cref="JsonFormatter"/>) override this to read buffer fields
    /// directly and skip the per-event <see cref="LogEvent"/> allocation.
    ///
    /// When the kernel dispatches, context is always empty (the kernel
    /// branch only fires when no per-call context and no default context
    /// merge are configured — see <c>StructuredLogger.Log</c>). Buffer
    /// formatters can therefore safely skip context emission.
    /// </summary>
    string Format(in LogEventBuffer buffer) => Format(buffer.ToLogEvent());
}

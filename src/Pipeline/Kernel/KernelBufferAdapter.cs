#nullable enable

using MMP.Herald.Events;
using MMP.Herald.Templating;

namespace MMP.Herald.Pipeline.Kernel;

/// <summary>
/// Helper for sinks implementing <see cref="IKernelSink"/> that need a
/// fully-materialised <see cref="LogEvent"/> with a rendered
/// <see cref="LogEvent.Message"/>. Kernel-path buffers always carry an
/// empty <c>Message</c> because the accept path skips the render to
/// stay zero-allocation; sinks that read the rendered text take that
/// cost at their own boundary by calling
/// <see cref="MaterializeAndRender"/>.
///
/// <para>
/// Use this helper when porting an existing legacy sink to
/// <see cref="IKernelSink"/>: the typical migration is a one-line
/// <c>Log(in LogEventBuffer)</c> method that calls
/// <c>MaterializeAndRender</c> and forwards to the existing
/// <c>Log(LogEvent)</c> body.
/// </para>
///
/// <para>
/// Sinks that do NOT read <see cref="LogEvent.Message"/> — pure
/// structured / JSON / OTLP outputs — should skip the helper, call
/// <see cref="LogEventBuffer.ToLogEvent"/> directly (or consume the
/// buffer in place), and avoid the per-event render cost.
/// </para>
/// </summary>
public static class KernelBufferAdapter
{
    // Shared parser: stateless once constructed, and the internal
    // parse-strategy carries its own cache, so every sink in the
    // process amortises template parsing through one instance.
    private static readonly MessageTemplateParser SharedRenderer = new();

    /// <summary>
    /// Materialise the buffer into a heap <see cref="LogEvent"/> and
    /// render the <see cref="LogEvent.Message"/> from the template
    /// when it is empty. The returned event is safe to hand to legacy
    /// <c>ILogger.Log(LogEvent)</c> consumers — they see the same
    /// rendered text the chain path would have produced.
    /// </summary>
    public static LogEvent MaterializeAndRender(in LogEventBuffer buffer)
    {
        var heap = buffer.ToLogEvent();
        if (!string.IsNullOrEmpty(heap.Message)) return heap;

        var rendered = SharedRenderer.Render(heap.MessageTemplate, heap.Properties);
        return heap with { Message = rendered.Message };
    }
}

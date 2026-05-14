#nullable enable

using MMP.Herald.Output.Rich;

namespace MMP.Herald.Output.Rendering;
/// <summary>
/// Transforms a log event into rendered output for a specific alias.
/// </summary>
public interface ILogOutputTransformer
{
    RenderedLogOutput Transform(LogRenderContext context);

    /// <summary>
    /// Kernel-path overload. Default implementation materialises the
    /// stack-allocated <see cref="LogEventBuffer"/> into a heap
    /// <see cref="MMP.Herald.Events.LogEvent"/> and forwards to the
    /// legacy <see cref="Transform(LogRenderContext)"/> path. Kernel-aware
    /// transformers override this to read fields directly from the
    /// buffer and skip the materialisation. Same backward-compat shape
    /// the formatter interface uses.
    /// </summary>
    RenderedLogOutput Transform(in LogRenderBufferContext context)
        => Transform(new LogRenderContext(
            context.Event.ToLogEvent(),
            context.Alias,
            context.LevelRegistry));
}

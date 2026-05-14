#nullable enable

using MMP.Herald.Levels;
using MMP.Herald.Output.Aliases;
using MMP.Herald.Pipeline.Kernel;

namespace MMP.Herald.Output.Rendering;

/// <summary>
/// Stack-scoped render context. Mirrors <see cref="LogRenderContext"/> but
/// carries a <see cref="LogEventBuffer"/> instead of a heap
/// <see cref="MMP.Herald.Events.LogEvent"/>.
///
/// Passed to <see cref="ILogOutputTransformer.Transform(in LogRenderBufferContext)"/>
/// on the kernel path. Transformers that override that overload read
/// event fields directly from the buffer without forcing a
/// <see cref="MMP.Herald.Events.LogEvent"/> materialisation; transformers
/// that don't override inherit a default implementation that materialises
/// once and forwards to the legacy <see cref="ILogOutputTransformer.Transform(LogRenderContext)"/>
/// path — same backward-compat pattern the formatter interface uses.
///
/// The ref-struct constraint prevents the context from escaping to a
/// field or async state machine, which is what makes the referenced
/// <see cref="LogEventBuffer"/> safe to hold — escape analysis can
/// prove the buffer stays on the caller's stack for the lifetime of
/// the transform call.
/// </summary>
public readonly ref struct LogRenderBufferContext
{
    /// <summary>The stack-allocated event being rendered.</summary>
    public readonly LogEventBuffer Event;

    /// <summary>The output alias this render is targeting.</summary>
    public readonly LogOutputAlias Alias;

    /// <summary>Level registry, for rank/display-name resolution.</summary>
    public readonly ILogLevelRegistry LevelRegistry;

    public LogRenderBufferContext(
        in LogEventBuffer @event,
        LogOutputAlias alias,
        ILogLevelRegistry levelRegistry)
    {
        Event = @event;
        Alias = alias;
        LevelRegistry = levelRegistry;
    }
}

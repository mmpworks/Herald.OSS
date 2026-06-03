#nullable enable

using System;
using System.IO;
using MMP.Herald.Output.Rendering;
using MMP.Herald.Output.Rich;
using MMP.Herald.Serilog.Events;

namespace MMP.Herald.Serilog.Formatting;

/// <summary>
/// Bridges a user-supplied <see cref="ITextFormatter"/> into Herald's
/// <see cref="ILogOutputTransformer"/> contract.
///
/// <para>
/// Receives the native Herald <see cref="MMP.Herald.Events.LogEvent"/>
/// via <see cref="ILogOutputTransformer.Transform(MMP.Herald.LogRenderContext)"/>,
/// wraps it in the Serilog-shaped mirror (<see cref="LogEvent"/>), calls
/// <see cref="ITextFormatter.Format"/> with a <see cref="StringWriter"/>, then
/// returns the resulting text as a <see cref="RenderedLogOutput"/>.
/// </para>
///
/// <para>
/// The plain-text output is what the console writer ultimately writes.
/// ANSI styling and the Herald rich-format pipeline are intentionally
/// bypassed — the user formatter owns the final representation.
/// </para>
///
/// <para>
/// AOT note: no reflection; all projection happens inside the P1 mirror
/// constructor and <see cref="LogEventValueProjector"/> which already
/// carries its own IL2026 suppression.
/// </para>
/// </summary>
internal sealed class TextFormatterOutputTransformer : ILogOutputTransformer
{
    private readonly ITextFormatter _formatter;

    internal TextFormatterOutputTransformer(ITextFormatter formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);
        _formatter = formatter;
    }

    /// <summary>
    /// Transform: wrap native event in Serilog mirror, call user formatter,
    /// return plain-text output.
    /// </summary>
    public RenderedLogOutput Transform(MMP.Herald.LogRenderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Wrap native LogEvent in the Serilog mirror (P1 shape).
        var mirror = WrapNativeEvent(context.Event);

        // Let the user formatter write into a StringWriter.
        var writer = new StringWriter();
        _formatter.Format(mirror, writer);
        var text = writer.ToString();

        return RenderedLogOutput.FromPlainText(text);
    }

    // C# 12 note: kept this form for .NET 9+ compatibility.
    // The internal ctor on Serilog.Events.LogEvent takes a native event.
    // Upgrade note for C# 14: if the mirror gains a static factory the
    // call site here could be simplified.
    private static LogEvent WrapNativeEvent(MMP.Herald.Events.LogEvent native)
    {
        // LogEventValueProjector is the declared factory for the mirror
        // (Guard 1 on the LogEvent class). Use the internal ctor directly
        // because this assembly is in the same package and
        // InternalsVisibleTo is wired.
        return new LogEvent(native);
    }
}

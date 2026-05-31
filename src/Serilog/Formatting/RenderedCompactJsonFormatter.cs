#nullable enable

using System;
using System.IO;
using MMP.Herald.Serilog.Events;

namespace MMP.Herald.Serilog.Formatting;

/// <summary>
/// Extends <see cref="CompactJsonFormatter"/> with the <c>@m</c> (rendered message)
/// CLEF field.
///
/// <para>
/// The <c>@m</c> field holds the fully rendered message — message-template holes
/// replaced by their property values (e.g. <c>"Hello World"</c> for template
/// <c>"Hello {Name}"</c> with <c>Name = "World"</c>). Real Serilog's
/// <c>RenderedCompactJsonFormatter</c> adds this field on top of the base CLEF set.
/// </para>
///
/// <para>
/// <b>DRY.</b> All CLEF writing is delegated to <see cref="CompactJsonFormatter.WriteClef"/>
/// with the pre-rendered message string passed in. No separate JSON writer is introduced.
/// </para>
/// </summary>
public sealed class RenderedCompactJsonFormatter : ITextFormatter
{
    /// <inheritdoc/>
    public void Format(LogEvent logEvent, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        ArgumentNullException.ThrowIfNull(output);

        var view            = new LogEventSerilogEventView(logEvent);
        var renderedMessage = logEvent.RenderMessage();

        CompactJsonFormatter.WriteClef(view, logEvent.Exception, output, renderedMessage);
    }
}

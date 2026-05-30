#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MMP.Herald.Serilog.Events;

namespace MMP.Herald.Serilog.Formatting;

/// <summary>
/// Serilog-compatible formatter that renders log events using an output-template
/// string, implementing <see cref="ITextFormatter"/>.
///
/// <para>
/// The default template matches Serilog's own default:
/// <c>[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}</c>.
/// </para>
///
/// <para>
/// Internally bridges the P1 <see cref="LogEvent"/> mirror to
/// <see cref="ISerilogEventView"/> via the private
/// <see cref="LogEventSerilogEventView"/> adapter, then delegates rendering to
/// <see cref="SerilogOutputTemplateRenderer.Render"/>.
/// </para>
/// </summary>
public sealed class MessageTemplateTextFormatter : ITextFormatter
{
    /// <summary>
    /// The default Serilog output template.
    /// Matches Serilog 4.x's <c>DefaultConsoleOutputTemplate</c> exactly.
    /// </summary>
    public const string DefaultOutputTemplate =
        "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}";

    private readonly string _outputTemplate;

    /// <param name="outputTemplate">
    /// The output-template string, or <see langword="null"/> to use
    /// <see cref="DefaultOutputTemplate"/>.
    /// </param>
    public MessageTemplateTextFormatter(string? outputTemplate = null)
        => _outputTemplate = outputTemplate ?? DefaultOutputTemplate;

    /// <inheritdoc/>
    public void Format(LogEvent logEvent, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        ArgumentNullException.ThrowIfNull(output);

        var view     = new LogEventSerilogEventView(logEvent);
        var rendered = SerilogOutputTemplateRenderer.Render(_outputTemplate, view);
        output.Write(rendered);
    }
}

// ── Bridge: LogEvent mirror → ISerilogEventView ──────────────────────────────

// Internal adapter so MessageTemplateTextFormatter can hand the P1 mirror to
// SerilogOutputTemplateRenderer without making LogEvent implement the view interface.
//
// TemplateHoleNames is derived by parsing the MessageTemplate string through the same
// SerilogOutputTemplateParser that the renderer uses, so hole names are always
// consistent with what the renderer sees.  The parse result is cached on first access
// because ISerilogEventView.TemplateHoleNames may be read multiple times (e.g. by the
// {Properties} residual renderer).
internal sealed class LogEventSerilogEventView : ISerilogEventView
{
    private readonly LogEvent _evt;
    private IReadOnlyList<string>? _holeNames;

    internal LogEventSerilogEventView(LogEvent evt) => _evt = evt;

    public DateTimeOffset Timestamp => _evt.Timestamp;

    public LogEventLevel Level => _evt.Level;

    public string MessageTemplate => _evt.MessageTemplate;

    // RenderedMessage: pre-rendered form of the message (holes filled with values).
    // LogEvent.RenderMessage() provides the equivalent of Serilog's MessageTemplate.Render().
    public string RenderedMessage => _evt.RenderMessage();

    public IReadOnlyDictionary<string, LogEventPropertyValue> Properties => _evt.Properties;

    public Exception? Exception => _evt.Exception;

    // Parse the message-template string lazily and cache the hole names.
    // Uses SerilogOutputTemplateParser.Parse so the hole names are identical
    // to what the renderer extracts from the same template string — no secondary
    // parser divergence (ISerilogEventView HIGH-1 pre-mortem note).
    public IReadOnlyList<string> TemplateHoleNames
    {
        get
        {
            if (_holeNames is not null) return _holeNames;

            var tokens = SerilogOutputTemplateParser.Parse(_evt.MessageTemplate);
            _holeNames = tokens
                .OfType<HoleToken>()
                .Select(h => h.Name)
                .ToList();
            return _holeNames;
        }
    }
}

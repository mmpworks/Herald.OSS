#nullable enable

using System;
using System.Globalization;
using System.IO;
using MMP.Herald.Serilog.Events;

namespace MMP.Herald.Serilog.Formatting;

/// <summary>
/// Serilog Compact Log Event Format (CLEF) formatter.
///
/// <para>
/// Produces one JSON object per line with the standard CLEF field set:
/// <c>@t</c> (ISO-8601 UTC timestamp), <c>@mt</c> (message template),
/// <c>@l</c> (level — omitted for Information per Serilog convention),
/// <c>@x</c> (exception — omitted when absent), and all event properties
/// as top-level fields.
/// </para>
///
/// <para>
/// <b>DRY strategy.</b> Rather than hand-rolling a second JSON writer, this
/// formatter reuses <see cref="SerilogTokenRenderers.RenderValue"/> for
/// property-value serialisation (JSON-style, literal=false) and
/// <see cref="LogEventSerilogEventView"/> to bridge the P1 mirror to the
/// view interface the token renderers already consume.
/// </para>
///
/// <para>
/// <b>Field/value parity with real Serilog.</b> Not byte-identical — two
/// independent JSON writers will not agree on exact formatting (see R-3
/// design decision) — but the field presence and value representation are
/// oracle-verified to match <c>Serilog.Formatting.Compact.CompactJsonFormatter</c>.
/// </para>
///
/// <para>
/// <b>Timestamp.</b> CLEF specifies UTC ISO-8601; the <c>@t</c> field always
/// uses <c>DateTimeOffset.UtcNow</c>-normalised output via the mirror's
/// <c>Timestamp</c> property converted to UTC before formatting.
/// </para>
/// </summary>
public sealed class CompactJsonFormatter : ITextFormatter
{
    /// <inheritdoc/>
    public void Format(LogEvent logEvent, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        ArgumentNullException.ThrowIfNull(output);

        // Bridge to ISerilogEventView so property enumeration and rendering
        // share the same path the output-template renderer uses.
        var view = new LogEventSerilogEventView(logEvent);

        WriteClef(view, logEvent.Exception, output, renderedMessage: null);
    }

    // ── Shared CLEF writer ────────────────────────────────────────────────────

    // Renders one CLEF JSON line to output. When renderedMessage is non-null the
    // caller is RenderedCompactJsonFormatter and the @m field is included.
    internal static void WriteClef(
        ISerilogEventView view,
        Exception? exception,
        TextWriter output,
        string? renderedMessage)
    {
        output.Write('{');

        // @t — UTC ISO-8601 timestamp.
        // CLEF convention: UTC, 'O' round-trip format, matches Serilog's output.
        output.Write("\"@t\":\"");
        output.Write(view.Timestamp.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        output.Write('"');

        // @mt — message template (holes intact).
        output.Write(",\"@mt\":\"");
        WriteJsonString(output, view.MessageTemplate);
        output.Write('"');

        // @m — rendered message (RenderedCompactJsonFormatter only).
        if (renderedMessage is not null)
        {
            output.Write(",\"@m\":\"");
            WriteJsonString(output, renderedMessage);
            output.Write('"');
        }

        // @l — level. Omitted for Information (Serilog convention).
        if (view.Level != LogEventLevel.Information)
        {
            output.Write(",\"@l\":\"");
            output.Write(ClefLevelMoniker(view.Level));
            output.Write('"');
        }

        // @x — exception. Omitted when absent.
        if (exception is not null)
        {
            output.Write(",\"@x\":\"");
            WriteJsonString(output, exception.ToString());
            output.Write('"');
        }

        // Event properties as top-level fields.
        // Each property key becomes a JSON field name; value is rendered JSON-style.
        foreach (var kv in view.Properties)
        {
            output.Write(",\"");
            WriteJsonString(output, kv.Key);
            output.Write("\":");
            using var valueWriter = new StringWriter();
            SerilogTokenRenderers.RenderValue(kv.Value, valueWriter, literal: false, json: true);
            output.Write(valueWriter.ToString());
        }

        output.Write('}');
        output.WriteLine();
    }

    // ── CLEF level moniker ─────────────────────────────────────────────────────

    // Maps the Serilog LogEventLevel to the CLEF @l string.
    // Serilog.Formatting.Compact uses these exact strings (oracle-verified).
    // Upgrade note for C# 14: a switch expression with CallerArgumentExpression
    // could simplify this if nameof gains enum support. Kept for C# 12 compat.
    private static string ClefLevelMoniker(LogEventLevel level) => level switch
    {
        LogEventLevel.Verbose     => "Verbose",
        LogEventLevel.Debug       => "Debug",
        LogEventLevel.Warning     => "Warning",
        LogEventLevel.Error       => "Error",
        LogEventLevel.Fatal       => "Fatal",
        _                         => "Information",
    };

    // ── JSON string escaping ───────────────────────────────────────────────────

    // Writes a string value to output with JSON special-character escaping.
    // Escapes: " → \", \ → \\, control chars 0x00-0x1F → \uXXXX, and
    // the extended set \b \f \n \r \t as per the JSON spec (RFC 8259 §7).
    //
    // Cognitive complexity note: linear character scan, one switch for the
    // small set of named escapes, fall-through to \uXXXX for remaining controls.
    internal static void WriteJsonString(TextWriter output, string value)
    {
        if (string.IsNullOrEmpty(value))
            return;

        foreach (var c in value)
        {
            switch (c)
            {
                case '"':  output.Write("\\\""); break;
                case '\\': output.Write("\\\\"); break;
                case '\b': output.Write("\\b");  break;
                case '\f': output.Write("\\f");  break;
                case '\n': output.Write("\\n");  break;
                case '\r': output.Write("\\r");  break;
                case '\t': output.Write("\\t");  break;
                default:
                    if (c < 0x20)
                        output.Write($"\\u{(int)c:x4}");
                    else
                        output.Write(c);
                    break;
            }
        }
    }
}

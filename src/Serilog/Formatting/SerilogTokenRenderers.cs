#nullable enable

// Task 4: {Message}, {Timestamp}, {NewLine}, {Exception} token renderers.
// All rendering rules oracle-pinned against real Serilog 4.3.1 via SerilogFormattingOracle.
// OD-3/S-2: {Timestamp} renders LOCAL time (DateTimeOffset.ToLocalTime()), matching Serilog's default.

using System;
using System.Globalization;
using System.IO;
using System.Text;
using MMP.Herald.Serilog.Events;

namespace MMP.Herald.Serilog.Formatting;

/// <summary>
/// Static renderer methods for each built-in Serilog output-template token.
///
/// <para>
/// All rendering decisions are oracle-pinned against real Serilog 4.3.1.
/// The oracle is <c>SerilogFormattingOracle.RenderOutputTemplate</c> in the test
/// project; if the oracle and these methods diverge, the oracle wins.
/// </para>
///
/// <para>
/// <b>OD-3/S-2:</b> <c>{Timestamp}</c> uses <see cref="DateTimeOffset.ToLocalTime"/>
/// to match Serilog's default behaviour (local time, not UTC).
/// </para>
/// </summary>
public static class SerilogTokenRenderers
{
    // ── Default Serilog timestamp format ─────────────────────────────────────
    // Oracle-verified: real Serilog's MessageTemplateTextFormatter with no format
    // specifier renders the timestamp as "MM/dd/yyyy HH:mm:ss".
    private const string DefaultTimestampFormat = "MM/dd/yyyy HH:mm:ss";

    // ── Dispatch entry point ─────────────────────────────────────────────────

    /// <summary>
    /// Renders a single <see cref="HoleToken"/> from an output template to
    /// <paramref name="output"/>.
    ///
    /// <para>
    /// Named tokens (<c>Message</c>, <c>Timestamp</c>, <c>Level</c>,
    /// <c>NewLine</c>, <c>Exception</c>) are handled directly; all other names
    /// are treated as event-property references.
    /// </para>
    /// </summary>
    public static void RenderToken(
        HoleToken hole,
        ISerilogEventView evt,
        TextWriter output)
    {
        // Cognitive complexity note: flat switch on a small fixed set of
        // well-known names. Property fall-through is the only open-ended branch.
        switch (hole.Name)
        {
            case "Message":
                RenderMessage(hole.Format, evt, output);
                break;

            case "Timestamp":
                RenderTimestamp(hole.Format, evt.Timestamp, output);
                break;

            case "Level":
                // SerilogLevelMoniker maps a Herald level key to the Serilog display form.
                // The level key is derived from the LogEventLevel enum name (lower-case).
                output.Write(SerilogLevelMoniker.Render(LevelKeyFrom(evt.Level), hole.Format));
                break;

            case "NewLine":
                output.Write(Environment.NewLine);
                break;

            case "Exception":
                RenderException(evt.Exception, output);
                break;

            case "Properties":
                // Task 5 — residual properties renderer. Not implemented here.
                break;

            default:
                RenderProperty(hole, evt, output);
                break;
        }
    }

    // ── {Message} renderer ───────────────────────────────────────────────────

    /// <summary>
    /// Renders the <c>{Message}</c> token with the given format specifier.
    ///
    /// <list type="table">
    ///   <item><term>(none)</term><description>Use the pre-rendered message — string scalars are quoted.</description></item>
    ///   <item><term><c>l</c></term><description>Literal: string scalars are NOT quoted.</description></item>
    ///   <item><term><c>j</c></term><description>JSON value rendering (non-literal).</description></item>
    ///   <item><term><c>lj</c></term><description>Literal message text with JSON-style structured values. The canonical Serilog specifier.</description></item>
    /// </list>
    /// </summary>
    public static void RenderMessage(
        string? format,
        ISerilogEventView evt,
        TextWriter output)
    {
        // Normalise the format specifier for comparison.
        var spec = format?.ToLowerInvariant() ?? string.Empty;

        // Default (no specifier): the pre-rendered message already has string quotes
        // from the Herald/Serilog render pass. Return it as-is.
        if (spec.Length == 0)
        {
            output.Write(evt.RenderedMessage);
            return;
        }

        // For all specifiers that change how property values appear, re-render
        // the message template by walking its holes and substituting values from
        // the Properties dictionary with the appropriate formatting.
        bool literal = spec.Contains('l');  // :l or :lj → no outer quotes on string scalars
        bool json    = spec.Contains('j');  // :j or :lj → use JSON-style value rendering

        ReRenderMessage(evt, output, literal: literal, json: json);
    }

    // Walk the message template holes and render each property value with the
    // requested formatting.  Static text segments are emitted verbatim.
    //
    // Cognitive complexity note: two nested concerns (template walking + value
    // formatting) are kept separate by extracting RenderValue.
    private static void ReRenderMessage(
        ISerilogEventView evt,
        TextWriter output,
        bool literal,
        bool json)
    {
        var template = evt.MessageTemplate;

        if (string.IsNullOrEmpty(template))
            return;

        // Walk the template string the same way Serilog does: scan for {Name}
        // holes, emit literal text verbatim, substitute property values.
        var i = 0;
        while (i < template.Length)
        {
            char c = template[i];

            // {{ → literal {
            if (c == '{' && i + 1 < template.Length && template[i + 1] == '{')
            {
                output.Write('{');
                i += 2;
                continue;
            }

            // }} → literal }
            if (c == '}' && i + 1 < template.Length && template[i + 1] == '}')
            {
                output.Write('}');
                i += 2;
                continue;
            }

            // Potential hole: { ... }
            if (c == '{')
            {
                int close = template.IndexOf('}', i + 1);
                if (close < 0)
                {
                    // Malformed — emit the brace as literal text.
                    output.Write('{');
                    i++;
                    continue;
                }

                var holeContent = template.Substring(i + 1, close - (i + 1));

                // Strip capture-mode prefix (@/$) — they affect the stored value shape
                // (already handled by the projector) but not the rendering name lookup here.
                var name = holeContent.TrimStart('@', '$');

                // Strip any alignment/format suffix from the name.
                var commaIdx = name.IndexOf(',');
                var colonIdx = name.IndexOf(':');
                int nameEnd = name.Length;
                if (commaIdx >= 0) nameEnd = Math.Min(nameEnd, commaIdx);
                if (colonIdx >= 0) nameEnd = Math.Min(nameEnd, colonIdx);
                name = name.Substring(0, nameEnd).Trim();

                if (evt.Properties.TryGetValue(name, out var value))
                {
                    RenderValue(value, output, literal: literal, json: json);
                }
                else
                {
                    // Unresolved hole: emit the original token text.
                    output.Write('{');
                    output.Write(holeContent);
                    output.Write('}');
                }

                i = close + 1;
                continue;
            }

            output.Write(c);
            i++;
        }
    }

    // ── Value renderer ───────────────────────────────────────────────────────

    /// <summary>
    /// Renders a single <see cref="LogEventPropertyValue"/> to <paramref name="output"/>
    /// according to the <paramref name="literal"/> and <paramref name="json"/> flags.
    ///
    /// <para>
    /// <b>literal = true (":l" / ":lj"):</b> string scalars are written without
    /// surrounding double-quote characters, matching Serilog's literal rendering.
    /// </para>
    /// <para>
    /// <b>json = true (":j" / ":lj"):</b> complex value nodes (StructureValue,
    /// SequenceValue, DictionaryValue) are rendered in JSON style.
    /// Non-JSON (default) rendering uses Serilog's text-display style.
    /// </para>
    /// </summary>
    public static void RenderValue(
        LogEventPropertyValue value,
        TextWriter output,
        bool literal,
        bool json)
    {
        switch (value)
        {
            case ScalarValue scalar:
                RenderScalar(scalar.Value, output, literal);
                break;

            case StructureValue structure:
                if (json)
                    RenderStructureJson(structure, output);
                else
                    RenderStructureDisplay(structure, output);
                break;

            case SequenceValue sequence:
                if (json)
                    RenderSequenceJson(sequence, output, literal);
                else
                    RenderSequenceDisplay(sequence, output, literal);
                break;

            case DictionaryValue dict:
                if (json)
                    RenderDictionaryJson(dict, output, literal);
                else
                    RenderDictionaryDisplay(dict, output, literal);
                break;

            default:
                output.Write(value.ToString());
                break;
        }
    }

    // ── Scalar ───────────────────────────────────────────────────────────────

    // Render a scalar value. When literal=true, string scalars are emitted without
    // surrounding double-quote characters.
    private static void RenderScalar(object? value, TextWriter output, bool literal)
    {
        if (value is null)
        {
            output.Write("null");
            return;
        }

        if (value is string str)
        {
            if (literal)
            {
                output.Write(str);
            }
            else
            {
                // Default (quoted) rendering: surround the string in double quotes.
                // This matches Serilog's scalar rendering of string values.
                output.Write('"');
                output.Write(str);
                output.Write('"');
            }
            return;
        }

        // Non-string scalars: use the invariant-culture string representation.
        if (value is IFormattable formattable)
            output.Write(formattable.ToString(null, CultureInfo.InvariantCulture));
        else
            output.Write(value.ToString());
    }

    // ── StructureValue ───────────────────────────────────────────────────────

    // JSON-style structure rendering: {"Name":value,...}
    // Oracle-verified: matches real Serilog's {Message:j} output for destructured objects.
    // Compact format (no spaces after ':' or ',') matches Serilog's JSON rendering exactly.
    private static void RenderStructureJson(StructureValue structure, TextWriter output)
    {
        output.Write('{');
        var first = true;

        foreach (var prop in structure.Properties)
        {
            if (!first) output.Write(',');
            first = false;

            output.Write('"');
            output.Write(prop.Name);
            output.Write("\":");
            // Inner values rendered with json=true, literal=false (JSON strings are quoted).
            RenderValue(prop.Value, output, literal: false, json: true);
        }

        output.Write('}');
    }

    // Text/display-style structure rendering: TypeTag { Name: value, ... }
    private static void RenderStructureDisplay(StructureValue structure, TextWriter output)
    {
        if (!string.IsNullOrEmpty(structure.TypeTag))
        {
            output.Write(structure.TypeTag);
            output.Write(' ');
        }

        output.Write('{');
        var first = true;

        foreach (var prop in structure.Properties)
        {
            if (!first) output.Write(", ");
            first = false;

            output.Write(prop.Name);
            output.Write(": ");
            RenderValue(prop.Value, output, literal: false, json: false);
        }

        output.Write('}');
    }

    // ── SequenceValue ────────────────────────────────────────────────────────

    // JSON-style sequence: [elem,elem,...] — compact, no spaces.
    private static void RenderSequenceJson(
        SequenceValue sequence,
        TextWriter output,
        bool literal)
    {
        output.Write('[');
        var first = true;

        foreach (var elem in sequence.Elements)
        {
            if (!first) output.Write(',');
            first = false;
            RenderValue(elem, output, literal: literal, json: true);
        }

        output.Write(']');
    }

    // Display-style sequence: [elem, elem, ...]  (same brackets, different inner rendering)
    private static void RenderSequenceDisplay(
        SequenceValue sequence,
        TextWriter output,
        bool literal)
    {
        output.Write('[');
        var first = true;

        foreach (var elem in sequence.Elements)
        {
            if (!first) output.Write(", ");
            first = false;
            RenderValue(elem, output, literal: literal, json: false);
        }

        output.Write(']');
    }

    // ── DictionaryValue ──────────────────────────────────────────────────────

    // JSON-style dictionary: {"key":value,...} — compact, no spaces.
    private static void RenderDictionaryJson(
        DictionaryValue dict,
        TextWriter output,
        bool literal)
    {
        output.Write('{');
        var first = true;

        foreach (var kv in dict.Elements)
        {
            if (!first) output.Write(',');
            first = false;

            // Keys are always scalar; render them as quoted strings in JSON.
            RenderValue(kv.Key, output, literal: false, json: true);
            output.Write(':');
            RenderValue(kv.Value, output, literal: literal, json: true);
        }

        output.Write('}');
    }

    // Display-style dictionary: [(key: value), ...]
    private static void RenderDictionaryDisplay(
        DictionaryValue dict,
        TextWriter output,
        bool literal)
    {
        output.Write('[');
        var first = true;

        foreach (var kv in dict.Elements)
        {
            if (!first) output.Write(", ");
            first = false;
            output.Write('(');
            RenderValue(kv.Key, output, literal: true, json: false);
            output.Write(": ");
            RenderValue(kv.Value, output, literal: literal, json: false);
            output.Write(')');
        }

        output.Write(']');
    }

    // ── {Timestamp} renderer ─────────────────────────────────────────────────

    /// <summary>
    /// Renders the timestamp token.
    ///
    /// <para>
    /// <b>OD-3/S-2 decision:</b> the timestamp is converted to local time via
    /// <see cref="DateTimeOffset.ToLocalTime"/> before formatting, matching
    /// Serilog's default behaviour. The <see cref="CanonicalEventSpec"/> stores a
    /// <c>+05:00</c>-offset timestamp so UTC-vs-local divergence is caught in CI
    /// (MED-1 pre-mortem guard).
    /// </para>
    /// </summary>
    public static void RenderTimestamp(
        string? format,
        DateTimeOffset timestamp,
        TextWriter output)
    {
        // OD-3/S-2: render LOCAL time, not UTC.
        var local = timestamp.ToLocalTime();
        var fmt = string.IsNullOrEmpty(format) ? DefaultTimestampFormat : format;
        output.Write(local.ToString(fmt, CultureInfo.InvariantCulture));
    }

    // ── {Exception} renderer ─────────────────────────────────────────────────

    /// <summary>
    /// Renders the exception token.
    ///
    /// <list type="table">
    ///   <item><term>Exception present</term>
    ///     <description><c>exception.ToString()</c> followed by <see cref="Environment.NewLine"/>.</description></item>
    ///   <item><term>Exception absent</term>
    ///     <description>Empty string — NOT "null", NOT whitespace. Oracle-verified.</description></item>
    /// </list>
    /// </summary>
    public static void RenderException(Exception? exception, TextWriter output)
    {
        if (exception is null)
            return;

        output.Write(exception.ToString());
        output.Write(Environment.NewLine);
    }

    // ── Named property renderer ───────────────────────────────────────────────

    // Renders a named property that is not a built-in token name.
    // If the property is present in the event, renders its value using the
    // hole's format specifier. If absent, renders nothing (Serilog convention).
    private static void RenderProperty(
        HoleToken hole,
        ISerilogEventView evt,
        TextWriter output)
    {
        if (!evt.Properties.TryGetValue(hole.Name, out var value))
            return;  // Missing property → render nothing (matches Serilog behaviour)

        if (value is ScalarValue scalar)
        {
            // Apply a hole-level format specifier to IFormattable scalars.
            if (!string.IsNullOrEmpty(hole.Format) && scalar.Value is IFormattable formattable)
            {
                output.Write(formattable.ToString(hole.Format, CultureInfo.InvariantCulture));
                return;
            }
        }

        // Default: render with standard literal=false, json=false display.
        RenderValue(value, output, literal: false, json: false);
    }

    // ── Level key helper ──────────────────────────────────────────────────────

    // Map the Serilog LogEventLevel enum to the Herald level key string.
    // Upgrade note for C# 14: if nameof or enum introspection improves, this
    // switch could be simplified. Kept for .NET 9 / C# 12 compatibility.
    private static string LevelKeyFrom(LogEventLevel level) => level switch
    {
        LogEventLevel.Verbose     => "verbose",
        LogEventLevel.Debug       => "debug",
        LogEventLevel.Information => "information",
        LogEventLevel.Warning     => "warning",
        LogEventLevel.Error       => "error",
        LogEventLevel.Fatal       => "fatal",
        _                         => "information",
    };
}

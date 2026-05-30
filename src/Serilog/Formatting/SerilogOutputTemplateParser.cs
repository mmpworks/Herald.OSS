#nullable enable

using System;
using System.Collections.Generic;
using System.Text;

namespace MMP.Herald.Serilog.Formatting;

/// <summary>
/// Parses a Serilog output-template string into a flat list of
/// <see cref="SerilogOutputTemplateToken"/> values.
///
/// <para>
/// Parsing rules (matching Serilog's own scanner):
/// <list type="bullet">
///   <item><c>{{</c> → literal <c>{</c>; <c>}}</c> → literal <c>}</c></item>
///   <item>
///     <c>{Name,Alignment:Format}</c> — the first <c>,</c> is the alignment
///     delimiter, the first <c>:</c> after the name/alignment section is the
///     format delimiter.  Additional colons inside the format string (e.g.
///     <c>HH:mm:ss</c>) are part of the format value and are not treated as
///     delimiters.
///   </item>
///   <item>A hole that has no closing <c>}</c> degrades to literal text.</item>
/// </list>
/// </para>
/// </summary>
public static class SerilogOutputTemplateParser
{
    /// <summary>
    /// Parse <paramref name="template"/> and return the ordered token list.
    /// Returns an empty list for a null or empty template.
    /// </summary>
    public static IReadOnlyList<SerilogOutputTemplateToken> Parse(string? template)
    {
        if (string.IsNullOrEmpty(template))
            return Array.Empty<SerilogOutputTemplateToken>();

        var tokens = new List<SerilogOutputTemplateToken>();
        // Lazily created; cleared after each flush so we reuse one instance.
        StringBuilder? text = null;
        int i = 0;

        while (i < template.Length)
        {
            char c = template[i];

            // {{ → literal {
            if (c == '{' && i + 1 < template.Length && template[i + 1] == '{')
            {
                (text ??= new StringBuilder()).Append('{');
                i += 2;
                continue;
            }

            // }} → literal }
            if (c == '}' && i + 1 < template.Length && template[i + 1] == '}')
            {
                (text ??= new StringBuilder()).Append('}');
                i += 2;
                continue;
            }

            // Potential hole: { ... }
            if (c == '{')
            {
                int start = i + 1;
                int close = template.IndexOf('}', start);

                // Malformed — no closing brace; treat the opening { as literal text.
                if (close < 0)
                {
                    (text ??= new StringBuilder()).Append('{');
                    i++;
                    continue;
                }

                // Flush any accumulated literal text before emitting the hole token.
                FlushText(ref text, tokens);

                var holeContent = template.Substring(start, close - start);
                tokens.Add(ParseHole(holeContent));
                i = close + 1;
                continue;
            }

            // Plain character → accumulate literal text.
            (text ??= new StringBuilder()).Append(c);
            i++;
        }

        FlushText(ref text, tokens);
        return tokens;
    }

    // -------------------------------------------------------------------------
    // Hole parsing
    // -------------------------------------------------------------------------

    // Parse the interior of a hole (everything between the outer braces).
    // Grammar: Name [ ',' Alignment ] [ ':' Format ]
    // where Alignment is the FIRST comma section and Format is everything
    // after the FIRST colon that follows the name/alignment.
    private static HoleToken ParseHole(string content)
    {
        string name;
        int alignment = 0;
        string? format = null;

        int commaIdx = content.IndexOf(',');
        int colonIdx = content.IndexOf(':');

        // Both present: comma must come before the first colon to be an alignment marker.
        bool hasAlignment = commaIdx >= 0 && (colonIdx < 0 || commaIdx < colonIdx);

        if (hasAlignment)
        {
            name = content.Substring(0, commaIdx);
            var afterComma = content.Substring(commaIdx + 1);

            // The format section starts at the first ':' in what follows the comma.
            int colonInRest = afterComma.IndexOf(':');
            if (colonInRest >= 0)
            {
                int.TryParse(afterComma.Substring(0, colonInRest), out alignment);
                // Everything after that first colon is the format — including any
                // subsequent colons (e.g. the mm:ss in HH:mm:ss).
                format = afterComma.Substring(colonInRest + 1);
            }
            else
            {
                int.TryParse(afterComma, out alignment);
            }
        }
        else if (colonIdx >= 0)
        {
            // No alignment, but there is a format specifier.
            name = content.Substring(0, colonIdx);
            // Capture everything after the first colon as the format string.
            format = content.Substring(colonIdx + 1);
        }
        else
        {
            // Name only — no alignment, no format.
            name = content;
        }

        return new HoleToken(
            name.Trim(),
            alignment,
            string.IsNullOrEmpty(format) ? null : format);
    }

    // Flush the accumulated StringBuilder into a TextToken (if non-empty).
    private static void FlushText(ref StringBuilder? sb, List<SerilogOutputTemplateToken> tokens)
    {
        if (sb is { Length: > 0 })
        {
            tokens.Add(new TextToken(sb.ToString()));
            sb.Clear();
        }
    }
}

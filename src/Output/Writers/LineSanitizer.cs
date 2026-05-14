#nullable enable

using System;
using System.Text;
using MMP.Herald.Pooling;

namespace MMP.Herald.Output.Writers;

/// <summary>
/// Escapes control characters in a log line before it is written to a
/// text file sink. Prevents log-forging (CRLF injection) where an attacker-
/// controlled property value contains a newline that would otherwise be
/// written verbatim and appear as a separate log entry on replay.
///
/// Json formatters already handle this via <c>JsonEscaper</c>, and the WAL
/// in <c>DurableBufferLogger</c> already escapes newlines; this helper gives
/// the plain-text file writers the same guarantee.
///
/// Escapes performed:
/// <list type="bullet">
/// <item><c>\n</c> -> <c>\\n</c></item>
/// <item><c>\r</c> -> <c>\\r</c></item>
/// <item><c>\t</c> -> <c>\\t</c></item>
/// <item>Other C0 controls (0x00-0x1F) -> <c>\\xNN</c> (two hex digits)</item>
/// </list>
///
/// Characters above 0x7F are left alone so UTF-8 text remains intact.
/// The helper only allocates when an escape is actually needed.
/// </summary>
public static class LineSanitizer
{
    /// <summary>
    /// Returns <paramref name="line"/> with control characters escaped.
    /// When no escape is needed, returns the same reference for zero allocation.
    /// </summary>
    public static string EscapeControlCharacters(string line) {
        ArgumentNullException.ThrowIfNull(line);

        if (!NeedsEscaping(line))
        {
            return line;
        }

        // Rent from the shared pool so a high-volume file sink does not
        // generate a fresh StringBuilder per escaped line. The pool is the
        // same one JsonEscaper uses, which keeps gen-0 pressure flat under
        // sustained escaping load.
        var sb = StringBuilderPool.Rent();
        foreach (var c in line)
        {
            AppendEscaped(sb, c);
        }
        return StringBuilderPool.ReturnAndGetString(sb);
    }

    private static bool NeedsEscaping(string line) {
        // Fast path: scan once, return on first control char. Most production
        // log lines have no controls in property values at all.
        foreach (var c in line)
        {
            if (IsControl(c))
            {
                return true;
            }
        }
        return false;
    }

    private static bool IsControl(char c) {
        // C0 control range 0x00..0x1F plus DEL 0x7F.
        return c < 0x20 || c == 0x7F;
    }

    private static void AppendEscaped(StringBuilder sb, char c) {
        switch (c)
        {
            case '\n':
                sb.Append("\\n");
                return;
            case '\r':
                sb.Append("\\r");
                return;
            case '\t':
                sb.Append("\\t");
                return;
        }

        if (IsControl(c))
        {
            sb.Append("\\x");
            sb.Append(((int)c).ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
            return;
        }

        sb.Append(c);
    }
}

#nullable enable

using MMP.Herald.Pooling;

namespace MMP.Herald.Services;

/// <summary>
/// Fast single-pass JSON string escaper with zero-alloc fast path.
/// When no escaping is needed, returns the original string reference.
/// When escaping is needed, uses a pooled StringBuilder for the result.
///
/// <para>
/// RFC 8259 §7 requires every byte in <c>U+0000..U+001F</c> and <c>U+007F</c>
/// to be escaped in JSON strings. The five shorthand forms (<c>\b \f \n \r \t</c>)
/// exist for the common ones; everything else in the C0 range plus DEL
/// becomes <c>\u00XX</c> with lowercase hex digits. Emitting any of those
/// bytes literally produces JSON that <see cref="System.Text.Json.JsonDocument"/>
/// and most language parsers reject — the upstream caller's property value
/// can therefore corrupt an otherwise well-formed line.
/// </para>
/// </summary>
public static class JsonEscaper
{
    /// <summary>
    /// Escape a string for safe inclusion in a JSON value.
    /// Returns the original string unchanged when no escaping is needed (zero alloc).
    /// </summary>
    public static string Escape(string value) {
        if (!NeedsEscaping(value))
        {
            return value;
        }

        var sb = StringBuilderPool.Rent();

        foreach (var ch in value)
        {
            // Structural escapes come first — they're the hot cases and their
            // shorthand forms are cheaper than \u00XX. Anything else in the C0
            // range plus DEL falls into the \u00XX path. Characters above 0x7F
            // pass through: the file encoding (UTF-8) is responsible for them.
            switch (ch)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"':  sb.Append("\\\""); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (ch <= '\u001F' || ch == '\u007F')
                    {
                        // Four lowercase hex digits, matching System.Text.Json
                        // output so downstream diffs against its writer are
                        // byte-identical on this subset.
                        sb.Append("\\u");
                        sb.Append(HexDigit((ch >> 12) & 0xF));
                        sb.Append(HexDigit((ch >> 8) & 0xF));
                        sb.Append(HexDigit((ch >> 4) & 0xF));
                        sb.Append(HexDigit(ch & 0xF));
                    }
                    else
                    {
                        sb.Append(ch);
                    }
                    break;
            }
        }

        return StringBuilderPool.ReturnAndGetString(sb);
    }

    /// <summary>
    /// Check if a string contains characters that require JSON escaping.
    /// </summary>
    public static bool NeedsEscaping(string value) {
        foreach (var ch in value)
        {
            // Structural: backslash, quote. Plus the full C0 range (which
            // already covers \r, \n, \t, \b, \f as special cases) and DEL.
            if (ch is '\\' or '"' || ch <= '\u001F' || ch == '\u007F')
            {
                return true;
            }
        }

        return false;
    }

    private static char HexDigit(int nibble) =>
        (char)(nibble < 10 ? '0' + nibble : 'a' + (nibble - 10));
}

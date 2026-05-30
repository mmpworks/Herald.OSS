#nullable enable

using System.IO;
using System.Text;

namespace MMP.Herald.Serilog.Formatting;

/// <summary>
/// Composing output-template renderer — the entry point for P3 Tasks 2–6.
///
/// <para>
/// Parses an output-template string, dispatches each token to the appropriate
/// token renderer, applies alignment, and concatenates the result.
/// </para>
///
/// <para>
/// <b>Task 4 (SerilogTokenRenderers) is live.</b> All well-known tokens —
/// <c>Level</c>, <c>Message</c>, <c>Timestamp</c>, <c>NewLine</c>,
/// <c>Exception</c>, and named-property holes — are dispatched through
/// <see cref="SerilogTokenRenderers.RenderToken"/>. The only remaining stub is
/// <c>Properties</c> (residual-properties renderer, Task 5).
/// </para>
///
/// <para>
/// <b>ApplyAlignment contract.</b> Positive alignment = right-align (left-pad with
/// spaces). Negative = left-align (right-pad with spaces). Zero = no-op. Values
/// wider than the requested width are returned unmodified — no truncation.
/// </para>
/// </summary>
public static class SerilogOutputTemplateRenderer
{
    // ── Public surface ────────────────────────────────────────────────────────

    /// <summary>
    /// Render <paramref name="outputTemplate"/> against <paramref name="evt"/> and
    /// return the formatted string (no trailing newline added by this method).
    ///
    /// <para>
    /// All well-known tokens are live via <see cref="SerilogTokenRenderers.RenderToken"/>.
    /// The <c>Properties</c> residual token is stubbed pending Task 5.
    /// </para>
    /// </summary>
    public static string Render(string outputTemplate, ISerilogEventView evt)
    {
        var tokens = SerilogOutputTemplateParser.Parse(outputTemplate);
        var sb = new StringBuilder();
        using var writer = new StringWriter(sb);
        foreach (var token in tokens)
        {
            if (token is TextToken tt)
            {
                writer.Write(tt.Text);
            }
            else if (token is HoleToken ht)
            {
                var raw = RenderHole(ht, evt);
                writer.Write(ApplyAlignment(raw, ht.Alignment));
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Apply Serilog-style alignment padding to <paramref name="value"/>.
    ///
    /// <list type="bullet">
    ///   <item>Positive alignment → right-align (left-pad with spaces).</item>
    ///   <item>Negative alignment → left-align (right-pad with spaces).</item>
    ///   <item>Zero → no-op.</item>
    ///   <item>Value wider than the requested width → returned as-is (no truncation).</item>
    /// </list>
    ///
    /// <para>This method is <c>public</c> so that alignment-only tests can target
    /// it directly without constructing a full event or template.</para>
    /// </summary>
    public static string ApplyAlignment(string value, int alignment)
    {
        if (alignment == 0)
            return value;

        if (alignment > 0)
            return value.PadLeft(alignment);   // right-align: left-pad

        return value.PadRight(-alignment);     // left-align: right-pad
    }

    // ── Internal dispatch ─────────────────────────────────────────────────────

    // Dispatch a single hole token to the appropriate renderer.
    //
    // Cognitive complexity note: all well-known tokens delegate to SerilogTokenRenderers
    // (Task 4) which owns the per-token rendering logic. This method is intentionally thin —
    // it only captures the rendered string and applies alignment. The Properties token
    // delegates to SerilogPropertiesRenderer (Task 5) when it lands.
    private static string RenderHole(HoleToken hole, ISerilogEventView evt)
    {
        using var sw = new StringWriter();

        if (hole.Name == "Properties")
        {
            // TODO(Task 5): delegate to SerilogPropertiesRenderer.Render(hole, evt, sw)
            // Until Task 5 lands, emit nothing (residual-properties path not yet live).
        }
        else
        {
            // SerilogTokenRenderers handles: Level, Message, Timestamp, NewLine, Exception,
            // and all named-property holes. Properties is the only stub remaining.
            SerilogTokenRenderers.RenderToken(hole, evt, sw);
        }

        return sw.ToString();
    }
}

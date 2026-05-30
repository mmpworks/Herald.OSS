#nullable enable

using System.Collections.Generic;
using System.IO;
using MMP.Herald.Serilog.Events;

namespace MMP.Herald.Serilog.Formatting;

/// <summary>
/// Renders the <c>{Properties}</c> output-template token in Serilog-compatible format.
///
/// <para>
/// <b>Exclusion rule (critical).</b> Serilog's <c>{Properties}</c> token emits only
/// the properties that are NOT already named as holes in the message template. Properties
/// bound to holes are already visible in the <c>{Message}</c> token; repeating them in
/// <c>{Properties}</c> would produce duplicate output. The exclusion set comes from
/// <see cref="ISerilogEventView.TemplateHoleNames"/>.
/// </para>
///
/// <para>
/// <b>Output format (oracle-pinned).</b> Real Serilog renders:
/// <list type="bullet">
///   <item><c>{ Key: value, Key2: value2 }</c> — space after <c>{</c>, colon+space between
///     key and value, comma+space between properties, space before <c>}</c>.</item>
///   <item><c>{  }</c> when there are no residual properties (two spaces, matching Serilog's
///     constant prefix+suffix).</item>
///   <item>String scalars are double-quoted; numerics and null are bare.</item>
///   <item>The <c>:j</c> format specifier renders JSON-style: <c>{"Key":"value"}</c>.</item>
/// </list>
/// All format decisions are pinned against real Serilog 4.3.1 via
/// <c>SerilogPropertiesRendererTests</c>.
/// </para>
/// </summary>
public static class SerilogPropertiesRenderer
{
    // ── Public entry point ────────────────────────────────────────────────────

    /// <summary>
    /// Render the <c>{Properties}</c> token for <paramref name="evt"/> into
    /// <paramref name="output"/>.
    /// </summary>
    /// <param name="evt">The event being rendered.</param>
    /// <param name="format">
    /// The format specifier from the output-template hole, or <c>null</c> for the
    /// default format. Serilog supports <c>j</c> for JSON output.
    /// </param>
    /// <param name="output">Destination writer.</param>
    public static void Render(
        ISerilogEventView evt,
        string? format,
        TextWriter output)
    {
        // Collect message-template hole names into a set for O(1) exclusion.
        // OrdinalIgnoreCase matches Serilog's property-lookup behaviour: property
        // names are case-insensitive keys in the event dictionary.
        var usedInTemplate = new HashSet<string>(
            evt.TemplateHoleNames,
            System.StringComparer.OrdinalIgnoreCase);

        // Residual = properties NOT bound to a message-template hole.
        // Preserves insertion order (IReadOnlyDictionary iteration order).
        var residual = new System.Collections.Generic.List<KeyValuePair<string, LogEventPropertyValue>>();
        foreach (var kv in evt.Properties)
        {
            if (!usedInTemplate.Contains(kv.Key))
                residual.Add(kv);
        }

        bool useJson = string.Equals(format, "j", System.StringComparison.OrdinalIgnoreCase);

        if (useJson)
            RenderJson(residual, output);
        else
            RenderDefault(residual, output);
    }

    // ── Default format: { Key: value, Key2: value2 } ─────────────────────────

    // Renders the oracle-pinned default format.
    // Structure: "{ " + (key + ": " + value) joined by ", " + " }"
    // Empty case: "{  }" — prefix "{ " + suffix " }" with nothing between.
    //
    // Cognitive complexity note: two output.Write calls set up the fixed wrapper;
    // the loop handles zero or more properties with a leading-separator pattern.
    private static void RenderDefault(
        System.Collections.Generic.List<KeyValuePair<string, LogEventPropertyValue>> residual,
        TextWriter output)
    {
        output.Write("{ ");

        bool first = true;
        foreach (var kv in residual)
        {
            if (!first) output.Write(", ");
            first = false;

            output.Write(kv.Key);
            output.Write(": ");
            RenderValue(kv.Value, output);
        }

        output.Write(" }");
    }

    // ── JSON format: {"Key":"value"} ──────────────────────────────────────────

    // Renders the :j format — JSON object without extra spaces.
    // Empty case: "{}" (no spaces).
    private static void RenderJson(
        System.Collections.Generic.List<KeyValuePair<string, LogEventPropertyValue>> residual,
        TextWriter output)
    {
        output.Write('{');

        bool first = true;
        foreach (var kv in residual)
        {
            if (!first) output.Write(',');
            first = false;

            output.Write('"');
            output.Write(kv.Key);
            output.Write('"');
            output.Write(':');
            RenderValueJson(kv.Value, output);
        }

        output.Write('}');
    }

    // ── Value rendering ───────────────────────────────────────────────────────

    // Render a value in default format:
    //   - String scalars: double-quoted ("value")
    //   - Other scalars: ToString() or "null"
    //   - Structured/sequence/dictionary: delegate to ToString() (Serilog's default
    //     for non-scalar values in the default rendering path)
    private static void RenderValue(LogEventPropertyValue value, TextWriter output)
    {
        if (value is ScalarValue sv)
        {
            if (sv.Value is string s)
            {
                output.Write('"');
                output.Write(s);
                output.Write('"');
            }
            else
            {
                output.Write(sv.Value?.ToString() ?? "null");
            }
            return;
        }

        // Non-scalar values: use their ToString() representation, which matches
        // Serilog's default rendering for StructureValue, SequenceValue, DictionaryValue.
        output.Write(value.ToString() ?? "null");
    }

    // Render a value in JSON format:
    //   - String scalars: double-quoted JSON string
    //   - Numeric scalars: bare
    //   - Null: null
    //   - Others: fallback to string representation
    private static void RenderValueJson(LogEventPropertyValue value, TextWriter output)
    {
        if (value is ScalarValue sv)
        {
            if (sv.Value is null)
            {
                output.Write("null");
            }
            else if (sv.Value is string s)
            {
                output.Write('"');
                output.Write(s);
                output.Write('"');
            }
            else if (sv.Value is bool b)
            {
                output.Write(b ? "true" : "false");
            }
            else
            {
                // Numeric and other scalar types: bare representation.
                output.Write(sv.Value.ToString() ?? "null");
            }
            return;
        }

        output.Write(value.ToString() ?? "null");
    }
}

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using MMP.Herald.Output.Rich;

namespace MMP.Herald.Output.Rendering;

/// <summary>
/// Output processor that applies redaction rules to rendered fragments.
/// Matches property names in the render context and transforms their
/// rendered values according to the configured redaction mode.
///
/// Usage:
///   var redactor = new RedactionProcessor(
///   [
///       new RedactionRule("password", RedactionMode.Remove),
///       new RedactionRule("email", RedactionMode.Mask),
///       new RedactionRule("ssn", RedactionMode.Hash),
///       new RedactionRule("sessionId", RedactionMode.Mask, maskChar: 'X', visibleChars: 4)
///   ]);
///
///   var transformer = new PipelinedTransformer(baseTransformer, [redactor]);
/// </summary>
public sealed class RedactionProcessor : ILogOutputProcessor
{
    private readonly Dictionary<string, RedactionRule> _rules;

    public RedactionProcessor(IReadOnlyList<RedactionRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        _rules = new Dictionary<string, RedactionRule>(StringComparer.OrdinalIgnoreCase);

        foreach (var rule in rules)
        {
            _rules[rule.PropertyName] = rule;
        }
    }

    public RenderedLogOutput Process(RenderedLogOutput output, LogRenderContext context)
    {
        if (_rules.Count == 0 || context.Event.Properties.Count == 0)
            return output;

        // Build a set of property values that need redaction
        var redactions = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var property in context.Event.Properties)
        {
            if (!_rules.TryGetValue(property.Name, out var rule)) continue;

            var originalValue = property.ResolvedValue?.ToString() ?? "null";
            var redactedValue = ApplyRedaction(originalValue, rule);
            redactions[originalValue] = redactedValue;
        }

        if (redactions.Count == 0)
            return output;

        // Replace matching text in fragments
        var newFragments = new List<RenderedLogFragment>(output.Fragments.Count);

        foreach (var fragment in output.Fragments)
        {
            var text = fragment.Text;

            foreach (var (original, redacted) in redactions)
            {
                text = text.Replace(original, redacted, StringComparison.Ordinal);
            }

            newFragments.Add(text == fragment.Text
                ? fragment
                : fragment with { Text = text });
        }

        return new RenderedLogOutput(newFragments, output.Signals, output.Layouts);
    }

    private static string ApplyRedaction(string value, RedactionRule rule) =>
        RedactionHelper.Apply(value, rule.Mode, rule.MaskChar, rule.VisibleChars);
}

/// <summary>
/// How a property value should be redacted.
/// </summary>
public sealed record RedactionMode(string Value)
{
    /// <summary>Replace the entire value with [REDACTED].</summary>
    public static RedactionMode Remove { get; } = new("Remove");

    /// <summary>Mask most characters, optionally leaving trailing visible chars.</summary>
    public static RedactionMode Mask { get; } = new("Mask");

    /// <summary>Replace with a truncated SHA-256 hash for correlation without exposure.</summary>
    public static RedactionMode Hash { get; } = new("Hash");

    public override string ToString() => Value;
}

/// <summary>
/// A single redaction rule targeting a named property.
/// </summary>
public sealed record RedactionRule(
    string PropertyName,
    RedactionMode Mode,
    char MaskChar = '*',
    int VisibleChars = 0);

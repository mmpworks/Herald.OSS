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
            RedactionHelper.RequireUsableKey(rule.Mode, rule.HashKey, rule.PropertyName);
            _rules[rule.PropertyName] = rule with { HashKey = RedactionHelper.OwnedCopy(rule.HashKey) };
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
        RedactionHelper.Apply(value, rule.Mode, rule.MaskChar, rule.VisibleChars, rule.HashKey);
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
    /// <remarks>
    /// The hash has no key. A low-entropy value (an SSN, a phone number, a short
    /// record number) can be recovered by hashing every candidate. Use
    /// <see cref="KeyedHash"/> for identifiers.
    /// </remarks>
    public static RedactionMode Hash { get; } = new("Hash");

    /// <summary>
    /// Replace with a truncated HMAC-SHA256 under the rule's <c>HashKey</c>:
    /// <c>hmac-sha256:</c> followed by 32 lowercase hex characters (128 bits).
    /// Values correlate for holders of the key and cannot be recovered by
    /// trying candidates without it. A rule with this mode and no key of at
    /// least 16 bytes is rejected when the processor is built.
    /// </summary>
    public static RedactionMode KeyedHash { get; } = new("KeyedHash");

    public override string ToString() => Value;
}

/// <summary>
/// A single redaction rule targeting a named property.
/// </summary>
public sealed record RedactionRule(
    string PropertyName,
    RedactionMode Mode,
    char MaskChar = '*',
    int VisibleChars = 0)
{
    /// <summary>HMAC key for <see cref="RedactionMode.KeyedHash"/>. Ignored by every other mode.</summary>
    public byte[]? HashKey { get; init; }
}

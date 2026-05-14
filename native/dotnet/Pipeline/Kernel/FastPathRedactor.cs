#nullable enable

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using MMP.Herald.Output.Rendering;
using MMP.Herald.Pipeline.Processors;
using MMP.Herald.Templating;

namespace MMP.Herald.Pipeline.Kernel;

/// <summary>
/// Kernel-aware redactor that operates on the property span before the
/// <see cref="LogEventBuffer"/> is constructed. Stays on the kernel fast
/// path when no rule fires — zero allocation, zero copy. Spills to a
/// rewritten array only when a rule actually matches.
///
/// <para>
/// <b>Why this exists.</b> Adding any <see cref="ILogEventProcessor"/> to
/// the pipeline disqualifies the kernel fast path entirely (see
/// <see cref="KernelEligibility"/>) — the call moves onto the
/// event-allocation path that costs ~750 ns / ~1.3 KB per event regardless
/// of whether a rule fires. A pipeline whose only event-shaping need is
/// exact-name redaction does not need that overhead. This redactor lives
/// directly on the <see cref="StructuredLogger"/>'s dispatch boundary —
/// it is not a processor, does not register with
/// <see cref="Configuration.LogPipelinePolicy.EventProcessors"/>, and does
/// not move the call off the kernel.
/// </para>
///
/// <para>
/// <b>Scope.</b> Exact-name rules with <see cref="RedactionMode"/>
/// <c>Remove</c> / <c>Mask</c> / <c>Hash</c> only. Pattern rules
/// (glob / regex), event-action rules (<c>DropEvent</c> /
/// <c>ReplaceMessage</c>), and value-pattern conditions are rejected at
/// construction — those features need the full event-pipeline shape and
/// belong on <see cref="CompiledRedactionProcessor"/>. The fast path
/// covers the dominant production case; the long tail uses the heavy
/// processor.
/// </para>
///
/// <para>
/// <b>Re-render of <c>Message</c>.</b> Unnecessary on the kernel path —
/// the kernel does not eagerly render <see cref="LogEventBuffer.Message"/>;
/// it stays empty until a sink renders lazily against the (already-redacted)
/// property span. The fast path therefore avoids the
/// <see cref="MessageTemplateParser"/> allocation that
/// <see cref="CompiledRedactionProcessor"/> pays per redaction.
/// </para>
/// </summary>
public sealed class FastPathRedactor
{
    private readonly Dictionary<string, Rule> _rules;

    /// <summary>
    /// Compact rule shape — only the fields the fast path actually reads
    /// per event. Glob / regex / event-action / value-pattern fields from
    /// <see cref="CompiledRedactionRule"/> are not retained because they
    /// are rejected at construction.
    /// </summary>
    private readonly struct Rule
    {
        public readonly RedactionMode Mode;
        public readonly char MaskChar;
        public readonly int VisibleChars;

        public Rule(RedactionMode mode, char maskChar, int visibleChars)
        {
            Mode = mode;
            MaskChar = maskChar;
            VisibleChars = visibleChars;
        }
    }

    public FastPathRedactor(IReadOnlyList<CompiledRedactionRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        _rules = new Dictionary<string, Rule>(
            rules.Count, StringComparer.OrdinalIgnoreCase);

        foreach (var rule in rules)
        {
            RejectIfUnsupported(rule);

            // Last-write-wins on duplicate names — matches CompiledRedactionProcessor.
            _rules[rule.PropertyNamePattern] =
                new Rule(rule.Mode, rule.MaskChar, rule.VisibleChars);
        }
    }

    /// <summary>Number of compiled rules. Diagnostic / test surface.</summary>
    public int Count => _rules.Count;

    // ── LogProperty path ──────────────────────────────────────────────

    /// <summary>
    /// Apply redaction to a span of <see cref="LogProperty"/>. Returns the
    /// input span unchanged when no rule fires (zero allocation, zero copy).
    /// Returns a span over a freshly allocated array when at least one
    /// rule fires.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlySpan<LogProperty> Apply(ReadOnlySpan<LogProperty> input)
    {
        if (input.IsEmpty) return input;

        var firstHit = FindFirstHit(input, out var firstRule);
        return firstHit < 0 ? input : RewriteFrom(input, firstHit, firstRule);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int FindFirstHit(ReadOnlySpan<LogProperty> input, out Rule firstRule)
    {
        for (var i = 0; i < input.Length; i++)
        {
            if (_rules.TryGetValue(input[i].Name, out firstRule)) return i;
        }
        firstRule = default;
        return -1;
    }

    private LogProperty[] RewriteFrom(ReadOnlySpan<LogProperty> input, int firstHit, Rule firstRule)
    {
        var output = new LogProperty[input.Length];
        RewriteInto(input, firstHit, firstRule, output);
        return output;
    }

    /// <summary>
    /// Rental-aware redaction. When at least one rule fires, the rewrite
    /// array comes from <see cref="ArrayPool{T}"/> instead of being freshly
    /// allocated; the caller must return <paramref name="rented"/> after the
    /// kernel call (with <c>clearArray: true</c>) so the next renter doesn't
    /// observe stale slots. Returns a span over <c>rented[..count]</c>.
    /// When no rule fires the input span is returned unchanged and
    /// <paramref name="rented"/> is null.
    ///
    /// <para>
    /// Lifetime contract: the returned span aliases either the input or the
    /// rented array; both stay live until the caller's <c>kernel(in buffer)</c>
    /// dispatch returns. Sinks must not retain the span past <c>Emit</c> — the
    /// <see cref="LogEventBuffer"/> ref-struct shape enforces this at the type
    /// level (cannot escape the call), and sinks needing async work copy via
    /// <c>LogEventBuffer.ToLogEvent</c>.
    /// </para>
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ReadOnlySpan<LogProperty> ApplyWithRental(
        ReadOnlySpan<LogProperty> input,
        out LogProperty[]? rented,
        out int count)
    {
        if (input.IsEmpty)
        {
            rented = null;
            count = 0;
            return input;
        }

        var firstHit = FindFirstHit(input, out var firstRule);
        if (firstHit < 0)
        {
            rented = null;
            count = input.Length;
            return input;
        }

        rented = ArrayPool<LogProperty>.Shared.Rent(input.Length);
        count = input.Length;
        RewriteInto(input, firstHit, firstRule, rented.AsSpan(0, count));
        return rented.AsSpan(0, count);
    }

    private void RewriteInto(
        ReadOnlySpan<LogProperty> input,
        int firstHit,
        Rule firstRule,
        Span<LogProperty> output)
    {
        // Back-fill earlier props unchanged.
        for (var i = 0; i < firstHit; i++) output[i] = input[i];

        // Redact the first hit — rule is carried from FindFirstHit, no second lookup.
        output[firstHit] = RedactOne(input[firstHit], firstRule);

        // Continue walking; remaining props may or may not match.
        for (var i = firstHit + 1; i < input.Length; i++)
        {
            var prop = input[i];
            output[i] = _rules.TryGetValue(prop.Name, out var rule)
                ? RedactOne(prop, rule)
                : prop;
        }
    }

    private static LogProperty RedactOne(LogProperty prop, Rule rule)
    {
        // string fast path — avoids the virtual ToString call when the
        // property value is already a string (the common case).
        var value = prop.ResolvedValue switch
        {
            string s => s,
            null => "null",
            var v => v.ToString() ?? "null",
        };
        var redacted = RedactionHelper.Apply(value, rule.Mode, rule.MaskChar, rule.VisibleChars);
        return new LogProperty(
            prop.Name,
            redacted,
            prop.CaptureMode,
            prop.Format,
            prop.Visibility);
    }

    // ── LogPropertyCompact path ───────────────────────────────────────

    /// <summary>
    /// Apply redaction to a span of <see cref="LogPropertyCompact"/>.
    /// Same shape as the <see cref="LogProperty"/> overload — pass-through
    /// when no rule fires, single allocation when at least one does.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlySpan<LogPropertyCompact> Apply(ReadOnlySpan<LogPropertyCompact> input)
    {
        if (input.IsEmpty) return input;

        var firstHit = FindFirstHit(input, out var firstRule);
        return firstHit < 0 ? input : RewriteFrom(input, firstHit, firstRule);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int FindFirstHit(ReadOnlySpan<LogPropertyCompact> input, out Rule firstRule)
    {
        for (var i = 0; i < input.Length; i++)
        {
            if (_rules.TryGetValue(input[i].Name, out firstRule)) return i;
        }
        firstRule = default;
        return -1;
    }

    private LogPropertyCompact[] RewriteFrom(ReadOnlySpan<LogPropertyCompact> input, int firstHit, Rule firstRule)
    {
        var output = new LogPropertyCompact[input.Length];
        RewriteInto(input, firstHit, firstRule, output);
        return output;
    }

    /// <summary>
    /// Compact-property rental variant. Same shape and lifetime contract as
    /// the <see cref="LogProperty"/> overload.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ReadOnlySpan<LogPropertyCompact> ApplyWithRental(
        ReadOnlySpan<LogPropertyCompact> input,
        out LogPropertyCompact[]? rented,
        out int count)
    {
        if (input.IsEmpty)
        {
            rented = null;
            count = 0;
            return input;
        }

        var firstHit = FindFirstHit(input, out var firstRule);
        if (firstHit < 0)
        {
            rented = null;
            count = input.Length;
            return input;
        }

        rented = ArrayPool<LogPropertyCompact>.Shared.Rent(input.Length);
        count = input.Length;
        RewriteInto(input, firstHit, firstRule, rented.AsSpan(0, count));
        return rented.AsSpan(0, count);
    }

    private void RewriteInto(
        ReadOnlySpan<LogPropertyCompact> input,
        int firstHit,
        Rule firstRule,
        Span<LogPropertyCompact> output)
    {
        for (var i = 0; i < firstHit; i++) output[i] = input[i];

        output[firstHit] = RedactOne(input[firstHit], firstRule);

        for (var i = firstHit + 1; i < input.Length; i++)
        {
            var prop = input[i];
            output[i] = _rules.TryGetValue(prop.Name, out var rule)
                ? RedactOne(prop, rule)
                : prop;
        }
    }

    private static LogPropertyCompact RedactOne(LogPropertyCompact prop, Rule rule)
    {
        var value = prop.Value switch
        {
            string s => s,
            null => "null",
            var v => v.ToString() ?? "null",
        };
        var redacted = RedactionHelper.Apply(value, rule.Mode, rule.MaskChar, rule.VisibleChars);
        return new LogPropertyCompact(prop.Name, redacted);
    }

    // ── Construction-time validation ──────────────────────────────────

    private static void RejectIfUnsupported(CompiledRedactionRule rule)
    {
        if (rule.PatternKind != RedactionPatternKind.ExactName)
        {
            throw new ArgumentException(
                $"FastPathRedactor only supports ExactName rules. Rule '{rule.PropertyNamePattern}' " +
                $"has PatternKind={rule.PatternKind}. Use CompiledRedactionProcessor for pattern rules.",
                nameof(rule));
        }
        if (rule.EventAction != RedactionEventAction.None)
        {
            throw new ArgumentException(
                $"FastPathRedactor does not support event-action rules. Rule '{rule.PropertyNamePattern}' " +
                $"has EventAction={rule.EventAction}. Use CompiledRedactionProcessor for DropEvent / ReplaceMessage.",
                nameof(rule));
        }
        if (rule.ValuePattern is not null)
        {
            throw new ArgumentException(
                $"FastPathRedactor does not support value-pattern conditions. Rule '{rule.PropertyNamePattern}' " +
                $"has ValuePattern set. Use CompiledRedactionProcessor for value-conditional redaction.",
                nameof(rule));
        }
        if (rule.When is not null)
        {
            throw new ArgumentException(
                $"FastPathRedactor does not support per-event When predicates. Rule '{rule.PropertyNamePattern}' " +
                $"has When set. Use CompiledRedactionProcessor for predicate-conditional redaction.",
                nameof(rule));
        }
    }
}

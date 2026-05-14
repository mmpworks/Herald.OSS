// Copyright (c) 2026 MMP LLC
// Licensed under the MIT License. See LICENSE in the project root.
#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using MMP.Herald.Events;
using MMP.Herald.Templating;

namespace MMP.Herald.Addons.Reduction;

/// <summary>
/// Pipeline decorator that reduces high-volume numeric event streams to
/// per-window summary events. Each rule picks off events whose category
/// matches and whose configured property is numeric; the decorator
/// accumulates count / sum / min / max across a tumbling window, and when
/// the window fills it emits one synthesized event in place of the
/// originals. Events that don't match any rule pass through unchanged.
///
/// <para>
/// <b>Use case.</b> Simulations and tight-loop services emit one event
/// per step / frame / request. A 60 Hz simulation runs at 60 events/sec
/// per category before reduction; with a 100-event window the per-event
/// volume drops to 0.6 events/sec while still capturing mean / min / max.
/// Across a cluster that's weeks of disk-write time saved per quarter.
/// </para>
///
/// <para>
/// <b>Scope.</b> The decorator does not forward original events that
/// matched a rule — they are absorbed. Suppressing originals is the
/// reduction's whole point. Events that match no rule (different
/// category or no extractable numeric property) pass through to the
/// inner pipeline.
/// </para>
///
/// <para>
/// <b>Thread safety.</b> Window state is held in a
/// <see cref="ConcurrentDictionary{TKey,TValue}"/> keyed by category +
/// rule index, with a per-state lock guarding the short accumulate /
/// emit transition. The hot path is one dictionary lookup, one lock
/// acquire, four field updates, and a branch — bounded enough for a
/// 60+ Hz tight loop.
/// </para>
///
/// <para>
/// <b>Synthesized event provenance.</b> The summary event copies its
/// <c>GenSource</c> and <c>Context</c> from the last contributing event
/// so gated sinks accept it on the same provenance path as the
/// originals. Time is stamped at emit time (window-close moment).
/// </para>
/// </summary>
public sealed class WindowedMeanLogger : ILogger
{
    private readonly ILogger _next;
    private readonly IReadOnlyList<WindowedMeanRule> _rules;

    // (category-name, rule-index) → state. Per-rule keying lets one
    // category run multiple rules independently (e.g., aggregate both
    // Energy and KineticEnergy off the same physics-tick stream).
    private readonly ConcurrentDictionary<(string Category, int RuleIndex), WindowState> _states;

    public WindowedMeanLogger(ILogger next, IReadOnlyList<WindowedMeanRule> rules)
    {
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(rules);
        _next = next;
        _rules = rules;
        _states = new ConcurrentDictionary<(string, int), WindowState>();

        // Validate at construction so a bad rule fails fast, not at first event.
        for (var i = 0; i < rules.Count; i++) rules[i].Validate();
    }

    /// <summary>The downstream logger summary events flow into.</summary>
    public ILogger Inner => _next;

    /// <summary>The reduction rules in priority order. First match wins.</summary>
    public IReadOnlyList<WindowedMeanRule> Rules => _rules;

    public void Log(LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);

        // First-match-wins. Events that match any rule are absorbed; events
        // that match none flow through. This is the cognitive-complexity
        // hot spot — kept linear and small on purpose.
        for (var i = 0; i < _rules.Count; i++)
        {
            var rule = _rules[i];
            if (!CategoryMatches(logEvent.Category.Value, rule.CategoryPrefix)) continue;
            if (!TryReadNumeric(logEvent, rule.PropertyName, out var value)) continue;

            AccumulateAndMaybeEmit(logEvent, rule, i, value);
            return;
        }

        _next.Log(logEvent);
    }

    private static bool CategoryMatches(string categoryName, string prefix) =>
        prefix.Length == 0 ||
        categoryName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);

    private static bool TryReadNumeric(LogEvent logEvent, string propertyName, out double value)
    {
        var prop = logEvent.GetProperty(propertyName);
        if (prop is null) { value = 0; return false; }

        var raw = prop.Value.ResolvedValue;
        if (raw is null) { value = 0; return false; }

        // Cover the obvious numeric kinds inline; fall back to Convert for
        // anything else (decimal, byte, etc.). Convert.ToDouble throws on
        // unconvertible inputs — the catch keeps the hot path defensive
        // without forcing every caller to pre-validate.
        switch (raw)
        {
            case double d: value = d; return true;
            case float f: value = f; return true;
            case int i: value = i; return true;
            case long l: value = l; return true;
        }
        try
        {
            value = Convert.ToDouble(raw, CultureInfo.InvariantCulture);
            return true;
        }
        catch (Exception ex) when (ex is InvalidCastException or FormatException or OverflowException)
        {
            value = 0;
            return false;
        }
    }

    private void AccumulateAndMaybeEmit(LogEvent triggering, WindowedMeanRule rule, int ruleIndex, double value)
    {
        var state = _states.GetOrAdd((triggering.Category.Value, ruleIndex), _ => new WindowState());

        WindowSummary? toEmit = null;
        lock (state)
        {
            state.Count++;
            state.Sum += value;
            if (value < state.Min) state.Min = value;
            if (value > state.Max) state.Max = value;

            if (state.Count >= rule.WindowSize)
            {
                state.WindowIndex++;
                toEmit = new WindowSummary(
                    WindowIndex: state.WindowIndex,
                    Count: state.Count,
                    Mean: state.Sum / state.Count,
                    Min: state.Min,
                    Max: state.Max);
                state.Reset();
            }
        }

        if (toEmit is { } summary)
        {
            EmitSummary(triggering, rule, summary);
        }
    }

    private void EmitSummary(LogEvent triggering, WindowedMeanRule rule, WindowSummary summary)
    {
        // The synthesized event reuses the triggering event's category,
        // GenSource, and Context so gated sinks see it as the same
        // provenance stream. Level is held at Information — operators
        // who want to fan summary events to a different sink can route
        // by category or by the synthesized template's distinctive shape.
        var properties = new LogProperty[]
        {
            new("Window", summary.WindowIndex),
            new("Property", rule.PropertyName),
            new("Mean", summary.Mean),
            new("Count", summary.Count),
            new("Min", summary.Min),
            new("Max", summary.Max),
        };

        var rendered = RenderSummary(rule.SummaryTemplate, summary, rule.PropertyName);

        var synthesized = new LogEvent(
            TimeUtc: DateTimeOffset.UtcNow,
            Level: triggering.Level,
            Category: triggering.Category,
            MessageTemplate: rule.SummaryTemplate,
            Message: rendered,
            Properties: properties,
            Context: triggering.Context,
            EventId: null,
            CausedBy: null,
            GenSource: triggering.GenSource);

        _next.Log(synthesized);
    }

    // Compact placeholder substitution for the summary template. The
    // pipeline's full template parser is overkill here — six known
    // placeholders, all numeric or a property name. Format-spec support
    // for the standard double formats (F4, G, E) keeps the rendered text
    // readable for the per-event eyes-on-the-stream operator.
    private static string RenderSummary(string template, WindowSummary s, string propertyName)
    {
        return template
            .Replace("{Window}", s.WindowIndex.ToString(CultureInfo.InvariantCulture))
            .Replace("{Property}", propertyName)
            .Replace("{Mean:F4}", s.Mean.ToString("F4", CultureInfo.InvariantCulture))
            .Replace("{Mean}", s.Mean.ToString("G", CultureInfo.InvariantCulture))
            .Replace("{Count}", s.Count.ToString(CultureInfo.InvariantCulture))
            .Replace("{Min:F4}", s.Min.ToString("F4", CultureInfo.InvariantCulture))
            .Replace("{Min}", s.Min.ToString("G", CultureInfo.InvariantCulture))
            .Replace("{Max:F4}", s.Max.ToString("F4", CultureInfo.InvariantCulture))
            .Replace("{Max}", s.Max.ToString("G", CultureInfo.InvariantCulture));
    }

    // Internal mutable state. Lives behind the per-(category, rule) lock.
    private sealed class WindowState
    {
        public int Count;
        public double Sum;
        public double Min = double.MaxValue;
        public double Max = double.MinValue;
        public int WindowIndex;

        public void Reset()
        {
            Count = 0;
            Sum = 0;
            Min = double.MaxValue;
            Max = double.MinValue;
        }
    }

    private readonly record struct WindowSummary(
        int WindowIndex, int Count, double Mean, double Min, double Max);
}

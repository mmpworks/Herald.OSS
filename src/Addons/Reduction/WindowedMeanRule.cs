// Copyright (c) 2026 MMPWorks LLC
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.
#nullable enable

using System;

namespace MMP.Herald.Addons.Reduction;

/// <summary>
/// One reduction rule. Each rule says: "for events whose category starts
/// with <paramref name="CategoryPrefix"/> and that carry a numeric property
/// named <paramref name="PropertyName"/>, accumulate values across a tumbling
/// window of <paramref name="WindowSize"/> events; when the window fills,
/// emit one synthesized summary event and discard the originals."
///
/// <para>
/// <b>Why a separate rule type.</b> Pipelines often have several high-volume
/// numeric streams that benefit from reduction (per-step energy in a sim,
/// per-frame draw time in a renderer, per-request latency in a service).
/// One <see cref="WindowedMeanLogger"/> can run many independent rules,
/// each picking off the events it cares about and leaving the rest alone.
/// </para>
///
/// <para>
/// <b>Scope semantics.</b> A rule that doesn't match an event passes the
/// event through to the next decorator unchanged. A rule that matches
/// suppresses the original event and accumulates its value. Originals are
/// not forwarded — that's the point: the reduction is what makes the
/// per-event volume drop.
/// </para>
/// </summary>
/// <param name="CategoryPrefix">
/// Match events whose <c>Category.Name</c> starts with this string
/// (ordinal, case-insensitive). Pass an empty string to match every
/// category.
/// </param>
/// <param name="PropertyName">
/// The numeric property to accumulate. Properties whose value is not
/// convertible to <see cref="double"/> are treated as a non-match and
/// pass through.
/// </param>
/// <param name="WindowSize">
/// Tumbling window size in events. Must be at least 2 — a window of one
/// would emit a summary per event, which is what the reduction is meant
/// to avoid.
/// </param>
/// <param name="SummaryTemplate">
/// Message template used for the synthesized summary event. Available
/// placeholders: <c>{Window}</c> (1-based window index), <c>{Property}</c>
/// (the property name), <c>{Mean}</c>, <c>{Count}</c>, <c>{Min}</c>,
/// <c>{Max}</c>. Defaults to a compact format that works for most
/// numeric streams.
/// </param>
public sealed record WindowedMeanRule(
    string CategoryPrefix,
    string PropertyName,
    int WindowSize,
    string SummaryTemplate = "window {Window} {Property}: mean={Mean:F4} count={Count} min={Min:F4} max={Max:F4}")
{
    /// <summary>
    /// Validate the rule. Throws on invalid configuration so misuse fails
    /// at builder time, not at first event.
    /// </summary>
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(CategoryPrefix);
        ArgumentException.ThrowIfNullOrWhiteSpace(PropertyName);
        ArgumentException.ThrowIfNullOrWhiteSpace(SummaryTemplate);
        if (WindowSize < 2)
        {
            throw new ArgumentOutOfRangeException(
                nameof(WindowSize),
                WindowSize,
                "WindowSize must be at least 2; a one-event window would emit a summary per event.");
        }
    }
}

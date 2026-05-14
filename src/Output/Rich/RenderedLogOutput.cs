#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace MMP.Herald.Output.Rich;
/// <summary>
/// Rendered rich output made of styled fragments and optional actionable signals.
/// Fragments are visual output for the host to render.
/// Signals are side-effect triggers for handlers to dispatch.
/// Both are produced by transformers; writers render fragments,
/// signal handlers dispatch signals.
/// </summary>
public sealed record RenderedLogOutput(
    IReadOnlyList<RenderedLogFragment> Fragments,
    IReadOnlyList<LogSignal>? Signals = null,
    IReadOnlyList<LogOutputLayout>? Layouts = null)
{
    /// <summary>
    /// True when this output carries at least one signal.
    /// </summary>
    public bool HasSignals => Signals is { Count: > 0 };

    /// <summary>
    /// True when this output carries structured layout hints.
    /// Layout-aware writers use these for tables, panels, and grids.
    /// Writers that don't understand layouts ignore them.
    /// </summary>
    public bool HasLayouts => Layouts is { Count: > 0 };

    public static RenderedLogOutput FromPlainText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return new RenderedLogOutput(
        [
            new RenderedLogFragment(text)
        ]);
    }

    /// <summary>
    /// Create output with both fragments and signals.
    /// </summary>
    public static RenderedLogOutput WithSignals(
        IReadOnlyList<RenderedLogFragment> fragments,
        IReadOnlyList<LogSignal> signals)
    {
        return new RenderedLogOutput(fragments, signals);
    }

    public string ToPlainText()
    {
        return string.Concat(Fragments.Select(static fragment => fragment.Text));
    }
}
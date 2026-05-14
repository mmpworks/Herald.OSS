#nullable enable

using System.Collections.Generic;

namespace MMP.Herald.Output.Rich;

/// <summary>
/// Structured layout hints for rich output rendering.
/// These are optional metadata on RenderedLogOutput that layout-aware
/// writers can use to render tables, panels, grids, and other structures.
///
/// Writers that don't understand layouts ignore them and fall back to
/// plain fragment rendering. This keeps the system backward-compatible
/// while enabling game-engine-specific structured output.
/// </summary>
public abstract record LogOutputLayout
{
    /// <summary>
    /// A titled, bordered panel containing styled fragments.
    /// Renders as a box with a header in layout-aware writers.
    /// Falls back to "--- Title ---\ncontent\n---" in plain text.
    ///
    /// Usage: combat summaries, NPC dialogue boxes, quest status panels.
    /// </summary>
    public sealed record Panel(
        string? Title,
        IReadOnlyList<RenderedLogFragment> Content,
        string? BorderColor = null) : LogOutputLayout;

    /// <summary>
    /// A table with named columns and typed rows.
    /// Renders as an aligned grid in layout-aware writers.
    /// Falls back to tab-separated values in plain text.
    ///
    /// Usage: combat round summaries, inventory lists, stat blocks.
    /// </summary>
    public sealed record Table(
        IReadOnlyList<string> Columns,
        IReadOnlyList<TableRow> Rows) : LogOutputLayout;

    /// <summary>
    /// A horizontal divider/separator with optional label.
    /// </summary>
    public sealed record Divider(string? Label = null) : LogOutputLayout;

    /// <summary>
    /// A key-value pair list rendered as aligned columns.
    /// Usage: character stat blocks, configuration dumps, debug state.
    /// </summary>
    public sealed record PropertyList(
        IReadOnlyList<PropertyListEntry> Entries,
        string? Title = null) : LogOutputLayout;
}

/// <summary>
/// A single row in a table layout.
/// Each cell is a list of styled fragments for rich rendering.
/// </summary>
public sealed record TableRow(IReadOnlyList<IReadOnlyList<RenderedLogFragment>> Cells);

/// <summary>
/// A single key-value entry in a property list layout.
/// </summary>
public sealed record PropertyListEntry(
    string Key,
    IReadOnlyList<RenderedLogFragment> Value);

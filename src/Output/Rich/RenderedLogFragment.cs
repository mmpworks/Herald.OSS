#nullable enable

namespace MMP.Herald.Output.Rich;
/// <summary>
/// A styled fragment of rendered log output.
/// Hosts decide how to interpret the style metadata.
/// </summary>
public sealed record RenderedLogFragment(
    string Text,
    string? ColorName = null,
    bool IsBold = false,
    bool IsItalic = false,
    string? BackgroundColorName = null);
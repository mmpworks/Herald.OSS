#nullable enable

namespace MMP.Herald.Output.Rendering.Themes;

/// <summary>
/// Unified style definition for any output element.
/// Replaces the separate LogLevelStyle and PropertyStyle records.
/// </summary>
public sealed record OutputStyle(
    string? ColorName = null,
    bool UseBold = false,
    bool UseItalic = false,
    string? BackgroundColorName = null);

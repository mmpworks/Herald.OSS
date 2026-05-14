#nullable enable

namespace MMP.Herald.Output.Rendering;

/// <summary>
/// Style definition for a log property in console output.
/// Applied to both property key and value fragments.
/// </summary>
public sealed record PropertyStyle(
    string? ColorName = null,
    bool UseBold = false,
    bool UseItalic = false);

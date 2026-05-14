#nullable enable

namespace MMP.Herald.Configuration.Runtime;

/// <summary>
/// Runtime per-category style definition for console output. Normalized
/// from the JSON transport record so downstream code does not depend on
/// the JSON shape. Parallels <see cref="LoggingRuntimePropertyStyleDefinition"/>.
/// </summary>
public sealed record LoggingRuntimeCategoryStyleDefinition(
    string CategoryName,
    string? ColorName = null,
    bool UseBold = false,
    bool UseItalic = false,
    string? BackgroundColorName = null);

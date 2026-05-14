#nullable enable

namespace MMP.Herald.Configuration.Json;

/// <summary>
/// JSON-facing theme override. Element names match OutputElementKind subtype names
/// (e.g., "Timestamp", "LevelText", "PropertyValue", "PropertyName").
/// Qualifier is optional (e.g., level key for LevelText, property name for PropertyValue).
/// </summary>
public sealed record JsonThemeOverride(
    string Element,
    string? Qualifier = null,
    string? ColorName = null,
    bool UseBold = false,
    bool UseItalic = false,
    string? BackgroundColorName = null);

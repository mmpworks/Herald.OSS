#nullable enable

namespace MMP.Herald.Configuration.Json;

/// <summary>
/// JSON-facing per-property style configuration for console output.
/// </summary>
public sealed record JsonPropertyStyleConfig(
    string PropertyName,
    string? ColorName = null,
    bool UseBold = false,
    bool UseItalic = false,
    string? BackgroundColorName = null);

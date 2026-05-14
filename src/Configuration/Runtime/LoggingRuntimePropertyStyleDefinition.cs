#nullable enable

namespace MMP.Herald.Configuration.Runtime;

/// <summary>
/// Runtime per-property style definition for console output.
/// </summary>
public sealed record LoggingRuntimePropertyStyleDefinition(
    string PropertyName,
    string? ColorName = null,
    bool UseBold = false,
    bool UseItalic = false,
    string? BackgroundColorName = null);

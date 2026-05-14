#nullable enable

using System.Collections.Generic;

namespace MMP.Herald.Configuration.Runtime;

/// <summary>
/// Runtime theme configuration after JSON normalization.
/// </summary>
public sealed record LoggingRuntimeThemeConfiguration(
    string? ThemeName,
    IReadOnlyList<LoggingRuntimeThemeOverride> Overrides);

/// <summary>
/// Runtime theme override entry.
/// </summary>
public sealed record LoggingRuntimeThemeOverride(
    string Element,
    string? Qualifier,
    string? ColorName,
    bool UseBold,
    bool UseItalic,
    string? BackgroundColorName = null);

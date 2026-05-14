#nullable enable

using System.Collections.Generic;

namespace MMP.Herald.Configuration.Json;

/// <summary>
/// JSON-facing console theme configuration.
/// If Name is specified, a built-in theme is used as the base.
/// Overrides are applied on top of the base theme.
/// </summary>
public sealed record JsonConsoleThemeConfig(
    string? Name = null,
    IReadOnlyList<JsonThemeOverride>? Overrides = null);

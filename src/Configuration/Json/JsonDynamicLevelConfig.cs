#nullable enable

using System.Collections.Generic;

namespace MMP.Herald.Configuration.Json;

/// <summary>
/// JSON-facing configuration for dynamic log level switching.
/// </summary>
public sealed record JsonDynamicLevelConfig(
    bool Enabled = false,
    IReadOnlyList<JsonCategoryLevelOverride>? CategoryOverrides = null);

/// <summary>
/// JSON-facing per-category level override definition.
/// </summary>
public sealed record JsonCategoryLevelOverride(
    string Category,
    string LevelKey);

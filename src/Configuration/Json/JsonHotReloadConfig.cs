#nullable enable

namespace MMP.Herald.Configuration.Json;

/// <summary>
/// JSON-facing configuration for hot reload of logging configuration.
/// When enabled, the logging pipeline watches for config changes and rebuilds automatically.
/// </summary>
public sealed record JsonHotReloadConfig(
    bool Enabled = false,
    int DebounceMs = 500);

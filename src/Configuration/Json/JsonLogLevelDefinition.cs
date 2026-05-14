#nullable enable

namespace MMP.Herald.Configuration.Json;
/// <summary>
/// JSON-facing definition of a log level.
/// </summary>
public sealed record JsonLogLevelDefinition(
    string Key,
    string DisplayName);
#nullable enable

namespace MMP.Herald.Configuration.Runtime;
/// <summary>
/// Runtime level definition normalized from transport config.
/// </summary>
public sealed record LoggingRuntimeLogLevelDefinition(
    string Key,
    string DisplayName);
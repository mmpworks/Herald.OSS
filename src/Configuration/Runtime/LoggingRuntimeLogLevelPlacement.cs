#nullable enable

namespace MMP.Herald.Configuration.Runtime;
/// <summary>
/// Runtime placement rule normalized from transport config.
/// Position is expected to be "before" or "after".
/// </summary>
public sealed record LoggingRuntimeLogLevelPlacement(
    string LevelKey,
    string Position,
    string RelativeToLevelKey);
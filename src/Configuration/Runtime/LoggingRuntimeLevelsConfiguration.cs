#nullable enable

using System.Collections.Generic;

namespace MMP.Herald.Configuration.Runtime;
/// <summary>
/// Runtime level configuration after JSON transport types have been normalized.
/// </summary>
public sealed record LoggingRuntimeLevelsConfiguration(
    IReadOnlyList<LoggingRuntimeLogLevelDefinition> BaseLevels,
    IReadOnlyList<LoggingRuntimeLogLevelDefinition> AdditionalLevels,
    IReadOnlyList<LoggingRuntimeLogLevelPlacement> Placements);
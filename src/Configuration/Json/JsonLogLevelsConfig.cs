#nullable enable

using System.Collections.Generic;

namespace MMP.Herald.Configuration.Json;
/// <summary>
/// JSON-facing level configuration.
/// BaseLevels establishes the initial ordered baseline.
/// Placements optionally inject additional levels before or after existing ones.
/// </summary>
public sealed record JsonLogLevelsConfig(
    IReadOnlyList<JsonLogLevelDefinition> BaseLevels,
    IReadOnlyList<JsonLogLevelDefinition> AdditionalLevels,
    IReadOnlyList<JsonLogLevelPlacement> Placements);
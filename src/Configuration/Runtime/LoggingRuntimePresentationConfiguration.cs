// Lines 1-15
#nullable enable

using System.Collections.Generic;

namespace MMP.Herald.Configuration.Runtime;
/// <summary>
/// Runtime presentation configuration for aliases and level styles.
/// </summary>
public sealed record LoggingRuntimePresentationConfiguration(
    IReadOnlyList<LoggingRuntimeAliasDefinition> Aliases,
    IReadOnlyList<LoggingRuntimeLevelStyleDefinition> LevelStyles,
    IReadOnlyList<LoggingRuntimePropertyStyleDefinition> PropertyStyles,
    LoggingRuntimeThemeConfiguration? Theme = null,
    IReadOnlyList<LoggingRuntimeCategoryStyleDefinition>? CategoryStyles = null);
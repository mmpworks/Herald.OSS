#nullable enable

using System;
using System.Collections.Generic;
using MMP.Herald.Configuration.Runtime;

namespace MMP.Herald.Levels;
/// <summary>
/// Builds a level registry from declarative runtime configuration.
/// </summary>
public sealed class ConfiguredLogLevelRegistryFactory : IConfiguredLogLevelRegistryFactory
{
    public ILogLevelRegistry Create(LoggingRuntimeLevelsConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var registry = new LogLevelRegistry();
        var levelsByKey = new Dictionary<string, LogLevel>(StringComparer.OrdinalIgnoreCase);

        RegisterBaseLevels(configuration, registry, levelsByKey);
        RegisterAdditionalLevels(configuration, levelsByKey);
        ApplyPlacements(configuration, registry, levelsByKey);

        return registry;
    }

    private static void RegisterBaseLevels(
        LoggingRuntimeLevelsConfiguration configuration,
        LogLevelRegistry registry,
        Dictionary<string, LogLevel> levelsByKey)
    {
        foreach (var levelDefinition in configuration.BaseLevels)
        {
            var level = CreateLevel(levelDefinition);

            registry.Register(level);
            levelsByKey.Add(level.Key, level);
        }
    }

    private static void RegisterAdditionalLevels(
        LoggingRuntimeLevelsConfiguration configuration,
        Dictionary<string, LogLevel> levelsByKey)
    {
        foreach (var levelDefinition in configuration.AdditionalLevels)
        {
            var level = CreateLevel(levelDefinition);

            if (levelsByKey.ContainsKey(level.Key))
            {
                throw new InvalidOperationException(
                    $"A log level with key '{level.Key}' is already defined.");
            }

            levelsByKey.Add(level.Key, level);
        }
    }

    private static void ApplyPlacements(
        LoggingRuntimeLevelsConfiguration configuration,
        LogLevelRegistry registry,
        Dictionary<string, LogLevel> levelsByKey)
    {
        foreach (var placement in configuration.Placements)
        {
            if (!levelsByKey.TryGetValue(placement.LevelKey, out var level))
            {
                throw new KeyNotFoundException(
                    $"No log level with key '{placement.LevelKey}' exists for placement.");
            }

            var alreadyRegistered = registry.Contains(level);

            if (alreadyRegistered)
            {
                throw new InvalidOperationException(
                    $"The log level '{placement.LevelKey}' is already registered and cannot be placed again.");
            }

            switch (placement.Position.ToLowerInvariant())
            {
                case Services.JsonConfigProperties.PlacementBefore:
                    registry.RegisterBefore(placement.RelativeToLevelKey, level);
                    break;

                case Services.JsonConfigProperties.PlacementAfter:
                    registry.RegisterAfter(placement.RelativeToLevelKey, level);
                    break;

                default:
                    throw new InvalidOperationException(
                        $"Unsupported level placement position '{placement.Position}'. Use 'before' or 'after'.");
            }
        }
    }

    private static LogLevel CreateLevel(LoggingRuntimeLogLevelDefinition definition)
    {
        return new LogLevel(definition.Key, definition.DisplayName);
    }
}
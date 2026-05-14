#nullable enable

using System;
using MMP.Herald.Configuration.Json;
using MMP.Herald.Levels;

namespace MMP.Herald.Configuration;
/// <summary>
/// Core bootstrap helper that turns JSON-backed config into runtime configuration
/// and a configured level registry.
/// </summary>
public static class LoggingRuntimeBootstrap
{
    public static LoggingRuntimeBootstrapResult Bootstrap(
        JsonLoggingConfig jsonConfig,
        IConfiguredLogLevelRegistryFactory configuredRegistryFactory,
        ILoggingConfigurationMapper configurationMapper)
    {
        ArgumentNullException.ThrowIfNull(jsonConfig);
        ArgumentNullException.ThrowIfNull(configuredRegistryFactory);
        ArgumentNullException.ThrowIfNull(configurationMapper);

        // Build the runtime levels once, here, and let the mapper
        // reuse the same shape via DefaultLoggingConfigurationMapper.
        // MapLevels — one conversion table, not two parallel copies.
        var runtimeLevels = DefaultLoggingConfigurationMapper.MapLevels(jsonConfig.Levels);

        var levelRegistry = configuredRegistryFactory.Create(runtimeLevels);
        var runtimeConfiguration = configurationMapper.Map(jsonConfig, levelRegistry);

        return new LoggingRuntimeBootstrapResult(
            RuntimeConfiguration: runtimeConfiguration,
            LevelRegistry: levelRegistry);
    }
}
#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using MMP.Herald.Configuration.Json;
using MMP.Herald.Configuration.Runtime;
using MMP.Herald.Enrichers;
using MMP.Herald.Filters;
using MMP.Herald.Levels;
using MMP.Herald.Predicates;

namespace MMP.Herald.Configuration;

/// <summary>
/// Maps JSON-facing config into runtime logging configuration.
/// </summary>
public sealed class DefaultLoggingConfigurationMapper : ILoggingConfigurationMapper
{
    public LoggingRuntimeConfiguration Map(
        JsonLoggingConfig jsonConfig,
        ILogLevelRegistry levelRegistry)
    {
        ArgumentNullException.ThrowIfNull(jsonConfig);
        ArgumentNullException.ThrowIfNull(jsonConfig.Levels);
        ArgumentNullException.ThrowIfNull(levelRegistry);

        var runtimeLevels = MapLevels(jsonConfig.Levels);
        var runtimePresentation = MapPresentation(jsonConfig);
        var minimumLevel = ResolveLevel(levelRegistry, jsonConfig.MinimumLevel);
        var pipelinePolicy = MapPipelinePolicy(jsonConfig, minimumLevel, levelRegistry);
        var runtimeSinks = MapSinks(jsonConfig, levelRegistry);
        var runtimeRoutes = MapRoutes(jsonConfig);

        return new LoggingRuntimeConfiguration(
            Levels: runtimeLevels,
            Presentation: runtimePresentation,
            PipelinePolicy: pipelinePolicy,
            Sinks: runtimeSinks,
            Routes: runtimeRoutes,
            DumpRegisteredLevelsToConsole: jsonConfig.DumpRegisteredLevelsToConsole,
            IncludeCallerInfo: jsonConfig.IncludeCallerInfo,
            IncludeActivityContext: jsonConfig.IncludeActivityContext,
            TestLoopbackUrl: jsonConfig.TestLoopbackUrl,
            TestLoopbackLogDir: jsonConfig.TestLoopbackLogDir,
            // Clamp to 1; a zero or negative entries-per-file would make
            // the rolling logic loop forever. The JSON default is 1000
            // (matches the record default) so a missing field keeps
            // existing behaviour.
            LoopbackEntriesPerFile: jsonConfig.LoopbackEntriesPerFile > 0
                ? jsonConfig.LoopbackEntriesPerFile
                : 1000,
            LoopbackUseNdjson: jsonConfig.LoopbackUseNdjson,
            PipelineName: jsonConfig.PipelineName);
    }

    /// <summary>
    /// Map the JSON levels block into the runtime shape. Public-static
    /// so <see cref="LoggingRuntimeBootstrap"/> can build the levels
    /// once before constructing a level registry; the mapper then
    /// reuses the same body when filling out
    /// <see cref="LoggingRuntimeConfiguration"/>. One conversion table,
    /// not two — adding a level field touches one method.
    /// </summary>
    public static LoggingRuntimeLevelsConfiguration MapLevels(JsonLogLevelsConfig levels) =>
        new(
            BaseLevels: levels.BaseLevels
                .Select(static level => new LoggingRuntimeLogLevelDefinition(level.Key, level.DisplayName))
                .ToList(),
            AdditionalLevels: levels.AdditionalLevels
                .Select(static level => new LoggingRuntimeLogLevelDefinition(level.Key, level.DisplayName))
                .ToList(),
            Placements: levels.Placements
                .Select(static p => new LoggingRuntimeLogLevelPlacement(p.LevelKey, p.Position, p.RelativeToLevelKey))
                .ToList());

    private static LoggingRuntimePresentationConfiguration MapPresentation(JsonLoggingConfig config) =>
        new(
            Aliases: config.Aliases
                .Select(static a => new LoggingRuntimeAliasDefinition(a.Key, a.TransformerKind, a.BasedOnAlias))
                .ToList(),
            LevelStyles: config.LevelStyles
                .Select(static s => new LoggingRuntimeLevelStyleDefinition(
                    s.LevelKey, s.ColorName, s.UseBold, s.UseItalic, s.BackgroundColorName))
                .ToList(),
            PropertyStyles: (config.PropertyStyles ?? [])
                .Select(static ps => new LoggingRuntimePropertyStyleDefinition(
                    ps.PropertyName, ps.ColorName, ps.UseBold, ps.UseItalic, ps.BackgroundColorName))
                .ToList(),
            Theme: MapThemeConfig(config.ConsoleTheme),
            CategoryStyles: (config.CategoryStyles ?? [])
                .Select(static cs => new LoggingRuntimeCategoryStyleDefinition(
                    cs.CategoryName, cs.ColorName, cs.UseBold, cs.UseItalic, cs.BackgroundColorName))
                .ToList());

    private static LogPipelinePolicy MapPipelinePolicy(
        JsonLoggingConfig config, LogLevel minimumLevel, ILogLevelRegistry registry) =>
        new(
            minimumLevel,
            MapAsyncPolicy(config.Async),
            MapBatchingPolicy(config),
            MapDynamicLevelPolicy(config.DynamicLevels, minimumLevel, registry),
            MapSamplingFilter(config.Sampling),
            MapPostFilteringPolicy(config.PostFiltering, registry),
            MapFlightRecorderPolicy(config.FlightRecorder, minimumLevel, registry),
            HotReloadEnabled: config.HotReload is { Enabled: true });
    // Enricher reconstruction intentionally stays out of the mapper.
    // - Initial build: QuickLogBuilder.Build() passes the live enricher
    //   instance (Enrichers.Resolve()) as the factory's explicit `enricher:`
    //   arg, so it always wins regardless of what the runtime config says.
    //   Reconstructing here would force initial builds to round-trip through
    //   EnricherJsonRegistry, which throws for any user-defined enricher
    //   that isn't registered (test stubs, plugin types loaded later, etc.).
    // - JSON-driven Reload: HotReloadableLoggingBootstrap.ExecuteReload
    //   reads jsonConfig.Enrichers directly and reconstructs via the
    //   registry there, so the throw lands exactly where it should — at the
    //   moment a JSON config explicitly declares a kind nothing knows how
    //   to build.

    // Preserves DeferRendering regardless of async being enabled. When async is
    // off, the deferred event still flows through the Rendering step on the
    // caller's thread — the win is late rejection and zero render cost on sinks
    // that consume structured output.
    private static AsyncLogPolicy MapAsyncPolicy(JsonAsyncLogPolicyConfig config) =>
        config.Enabled
            ? AsyncLogPolicy.Enabled(config.Capacity, config.DropStrategy)
                with { DeferRendering = config.DeferRendering }
            : AsyncLogPolicy.Disabled with { DeferRendering = config.DeferRendering };

    private static List<LoggingRuntimeSinkDefinition> MapSinks(
        JsonLoggingConfig config, ILogLevelRegistry registry) =>
        config.Sinks
            .Where(static sink => sink.Enabled)
            .Select(sink => new LoggingRuntimeSinkDefinition(
                Name: sink.Name, Kind: sink.Kind, Path: sink.Path, Alias: sink.Alias,
                Uri: sink.Uri, Host: sink.Host, Port: sink.Port,
                RetryPolicy: MapRetryPolicy(sink.Retry),
                RollingPolicy: MapRollingPolicy(sink.Rolling),
                MinLevel: sink.MinLevel is not null ? registry.GetByKeyOrNull(sink.MinLevel) : null,
                OutputTemplate: sink.OutputTemplate, IsAudit: sink.IsAudit,
                EnableMetrics: sink.EnableMetrics,
                Label: sink.Label,
                RunState: ParseRunState(sink.RunState),
                TestOutFileSuffix: sink.TestOutFileSuffix,
                TeeLiveToFile: sink.TeeLiveToFile,
                TeeLiveToUrl: sink.TeeLiveToUrl,
                Properties: sink.Properties))
            .ToList();

    /// <summary>
    /// Map the JSON string ("disabled" | "live" | "test") to the runtime
    /// enum. An unrecognised or missing value defaults to <c>Live</c> so
    /// pipeline JSON files written before the run-state field existed
    /// load with their previous behaviour.
    /// </summary>
    private static SinkRunState ParseRunState(string? raw) =>
        raw?.Trim().ToLowerInvariant() switch
        {
            "disabled" => SinkRunState.Disabled,
            "test"     => SinkRunState.Test,
            _          => SinkRunState.Live, // null / "live" / unknown all map to Live
        };

    private static List<LoggingRuntimeRouteDefinition> MapRoutes(JsonLoggingConfig config) =>
        config.Routes
            .Where(static route => route.Enabled)
            .Select(static route => new LoggingRuntimeRouteDefinition(route.SinkName, route.Predicate))
            .ToList();

    private static LoggingRuntimeFileRollingPolicy? MapRollingPolicy(JsonFileRollingConfig? rollingConfig)
    {
        if (rollingConfig is null)
        {
            return null;
        }

        var interval = rollingConfig.Interval.ToLowerInvariant() switch
        {
            Services.JsonConfigProperties.IntervalHourly => LogFileRollingInterval.Hourly,
            Services.JsonConfigProperties.IntervalDaily => LogFileRollingInterval.Daily,
            Services.JsonConfigProperties.IntervalCustom => LogFileRollingInterval.Custom,
            _ => LogFileRollingInterval.None
        };

        return new LoggingRuntimeFileRollingPolicy(
            Interval: interval,
            MaxBytes: rollingConfig.MaxBytes,
            MaxRetainedFiles: rollingConfig.MaxRetainedFiles,
            LogQueueSize: rollingConfig.LogQueueSize,
            StartMinute: rollingConfig.StartMinute,
            CaptureDurationMinutes: rollingConfig.CaptureDurationMinutes,
            FileNameSuffix: rollingConfig.FileNameSuffix,
            Locale: rollingConfig.Locale,
            RetentionDays: rollingConfig.RetentionDays,
            TotalSizeCapBytes: rollingConfig.TotalSizeCapBytes);
    }

    private static LoggingRuntimeRetryPolicy? MapRetryPolicy(JsonRetryPolicyConfig? retryConfig)
    {
        if (retryConfig is null || !retryConfig.Enabled)
        {
            return null;
        }

        if (retryConfig.MaxAttempts <= 0)
        {
            throw new InvalidOperationException(
                $"Retry max attempts must be greater than zero. Received '{retryConfig.MaxAttempts}'.");
        }

        if (retryConfig.DelayMs < 0)
        {
            throw new InvalidOperationException(
                $"Retry delay must be zero or greater. Received '{retryConfig.DelayMs}'.");
        }

        return new LoggingRuntimeRetryPolicy(
            retryConfig.MaxAttempts,
            retryConfig.DelayMs);
    }

    private static LogLevel ResolveLevel(ILogLevelRegistry levelRegistry, string levelKey)
    {
        return levelRegistry.GetByKeyOrNull(levelKey)
            ?? throw new KeyNotFoundException(
                $"No log level with key '{levelKey}' exists in the registry.");
    }
    
    private static LoggingRuntimeThemeConfiguration? MapThemeConfig(
        JsonConsoleThemeConfig? config)
    {
        if (config is null)
        {
            return null;
        }

        var overrides = (config.Overrides ?? [])
            .Select(static ov => new LoggingRuntimeThemeOverride(
                ov.Element,
                ov.Qualifier,
                ov.ColorName,
                ov.UseBold,
                ov.UseItalic,
                ov.BackgroundColorName))
            .ToList();

        return new LoggingRuntimeThemeConfiguration(config.Name, overrides);
    }

    private static PostFilteringPolicy? MapPostFilteringPolicy(
        JsonPostFilteringConfig? config,
        ILogLevelRegistry levelRegistry)
    {
        if (config is null || !config.Enabled)
        {
            return null;
        }

        if (config.TriggerCondition is null || config.NormalFilter is null || config.EscalatedFilter is null)
        {
            throw new InvalidOperationException(
                "PostFiltering requires triggerCondition, normalFilter, and escalatedFilter to be specified.");
        }

        var compiler = new DefaultLogPredicateCompiler();
        var triggerPredicate = compiler.Compile(config.TriggerCondition, levelRegistry);
        var normalPredicate = compiler.Compile(config.NormalFilter, levelRegistry);
        var escalatedPredicate = compiler.Compile(config.EscalatedFilter, levelRegistry);

        var batchFilter = new ConditionalBatchFilter(triggerPredicate, normalPredicate, escalatedPredicate);

        return new PostFilteringPolicy(
            BatchFilter: batchFilter,
            MaxBatchSize: config.MaxBatchSize > 0 ? config.MaxBatchSize : Services.PipelineDefaults.BatchSize,
            MaxBatchDelayMs: config.MaxBatchDelayMs > 0 ? config.MaxBatchDelayMs : Services.PipelineDefaults.BatchDelayMs);
    }

    // Resolves the optional MinimumLevel/TriggerLevel against the registry.
    // null MinimumLevel inherits the pipeline minimum (so the recorder
    // captures everything below the normal threshold). null TriggerLevel
    // inherits "error" — the conventional flush trigger.
    //
    // The decorator is Community-tier (free), but the canonical shape is
    // locked: bufferSize 200, no minimum-level override, null-or-"error"
    // trigger. Any deviation requires Pro; the gate fires here so JSON
    // config and the fluent builder share one enforcement point.
    private static FlightRecorderPolicy? MapFlightRecorderPolicy(
        JsonFlightRecorderConfig? config,
        LogLevel pipelineMinimumLevel,
        ILogLevelRegistry levelRegistry)
    {
        if (config is null || !config.Enabled)
        {
            return null;
        }

        if (config.BufferSize <= 0)
        {
            throw new InvalidOperationException(
                $"Flight recorder buffer size must be greater than zero. Received '{config.BufferSize}'.");
        }

        var minimumLevel = config.MinimumLevel is null
            ? pipelineMinimumLevel
            : ResolveLevel(levelRegistry, config.MinimumLevel);

        var triggerLevel = config.TriggerLevel is null
            ? ResolveLevel(levelRegistry, Services.LogLevelKeys.Error)
            : ResolveLevel(levelRegistry, config.TriggerLevel);

        return new FlightRecorderPolicy(
            BufferSize: config.BufferSize,
            MinimumLevel: minimumLevel,
            TriggerLevel: triggerLevel);
    }

    private static ILogFilter? MapSamplingFilter(JsonSamplingConfig? config)
    {
        if (config is null || !config.Enabled || config.Rules is not { Count: > 0 })
        {
            return null;
        }

        var entries = new List<CompositeSamplingFilter.SamplingRuleEntry>();

        foreach (var rule in config.Rules)
        {
            var scopePredicate = BuildSamplingScope(rule.Category, rule.MessageContains);

            ILogFilter filter = rule.MaxPerSecond > 0
                ? new ThrottlingFilter(rule.MaxPerSecond, TimeSpan.FromSeconds(1), scope: null)
                : new SamplingFilter(rule.SampleRate > 0 ? rule.SampleRate : 1, scope: null);

            entries.Add(new CompositeSamplingFilter.SamplingRuleEntry(scopePredicate, filter));
        }

        return new CompositeSamplingFilter(entries);
    }

    private static ILogPredicate BuildSamplingScope(string? category, string? messageContains)
    {
        var hasCategory = !string.IsNullOrWhiteSpace(category);
        var hasMessage = !string.IsNullOrWhiteSpace(messageContains);

        return (hasCategory, hasMessage) switch
        {
            (true, true) => new CompiledLogPredicate(e =>
                e.Category.Value.Equals(category, StringComparison.OrdinalIgnoreCase) &&
                e.Message.Contains(messageContains!, StringComparison.OrdinalIgnoreCase)),
            (true, false) => new CompiledLogPredicate(e =>
                e.Category.Value.Equals(category, StringComparison.OrdinalIgnoreCase)),
            (false, true) => new CompiledLogPredicate(e =>
                e.Message.Contains(messageContains!, StringComparison.OrdinalIgnoreCase)),
            _ => new CompiledLogPredicate(_ => true)
        };
    }

    private static DynamicLevelPolicy? MapDynamicLevelPolicy(
        JsonDynamicLevelConfig? config,
        LogLevel minimumLevel,
        ILogLevelRegistry levelRegistry)
    {
        if (config is null || !config.Enabled)
        {
            return null;
        }

        var globalSwitch = LogLevelSwitch.For(minimumLevel);
        CategoryLevelSwitchMap? categoryMap = null;

        if (config.CategoryOverrides is { Count: > 0 })
        {
            categoryMap = new CategoryLevelSwitchMap();

            foreach (var categoryOverride in config.CategoryOverrides)
            {
                var level = levelRegistry.GetByKeyOrNull(categoryOverride.LevelKey)
                    ?? throw new KeyNotFoundException(
                        $"No log level with key '{categoryOverride.LevelKey}' exists in the registry " +
                        $"(referenced by category override for '{categoryOverride.Category}').");

                categoryMap.SetCategoryLevel(categoryOverride.Category, level);
            }
        }

        return new DynamicLevelPolicy(globalSwitch, categoryMap);
    }

    private static BatchingPolicy MapBatchingPolicy(JsonLoggingConfig jsonConfig)
    {
        ArgumentNullException.ThrowIfNull(jsonConfig);

        var batching = jsonConfig.Batching;

        if (batching is null || !batching.Enabled)
        {
            return BatchingPolicy.Disabled;
        }

        var maxBatchSize = batching.MaxBatchSize <= 0
            ? Services.PipelineDefaults.BatchSize
            : batching.MaxBatchSize;

        var maxBatchDelayMs = batching.MaxBatchDelayMs <= 0
            ? Services.PipelineDefaults.BatchDelayMs
            : batching.MaxBatchDelayMs;

        return new BatchingPolicy(
            IsEnabled: true,
            MaxBatchSize: maxBatchSize,
            MaxBatchDelayMs: maxBatchDelayMs);
    }
}
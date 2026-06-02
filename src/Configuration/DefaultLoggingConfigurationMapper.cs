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
            // SamplingFilter (legacy single slot) stays null — the mapper produces the
            // AND-chain Filters list instead. EffectiveFilters reads the list.
            PostFilteringPolicy: MapPostFilteringPolicy(config.PostFiltering, registry),
            FlightRecorderPolicy: MapFlightRecorderPolicy(config.FlightRecorder, minimumLevel, registry),
            HotReloadEnabled: config.HotReload is { Enabled: true },
            Filters: MapFilters(config.Sampling, registry),
            // External-event-injection consent round-trips through the JSON config
            // so a pipeline restored from JSON (or hot-reloaded) preserves it.
            // See docs/design/external-event-injection-switch.md.
            AllowExternalEventInjection: config.AllowExternalEventInjection);
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
                MinLevel: sink.MinLevel is not null ? registry.GetByKeyOrNull(NormalizeLevelKey(sink.MinLevel)) : null,
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
        // S-3 shim: normalize pre-rename keys before registry lookup so on-disk
        // config files written with old keys (info/warn/critical/trace) continue to
        // load without errors. Applied only at this config-load boundary — not at
        // the wire/registry boundary, which loud-rejects old keys (Task 9).
        // This shim is deletable once all field deployments are migrated.
        var normalizedKey = NormalizeLevelKey(levelKey);
        return levelRegistry.GetByKeyOrNull(normalizedKey)
            ?? throw new KeyNotFoundException(
                $"No log level with key '{levelKey}' exists in the registry.");
    }

    /// <summary>
    /// One-time forward migration for on-disk config files written with
    /// pre-rename level keys. Applied only at the config-file-load boundary
    /// (this mapper). Old keys arriving on the wire are rejected loud by
    /// GetByKeyOrNull (Task 9 loud-reject contract).
    /// </summary>
    /// <remarks>
    /// DELETABLE once all field deployments have been migrated to Serilog-vocab
    /// keys. On next save the file will carry the new key and this shim becomes
    /// a no-op for that deployment. The S-3 label tracks this shim in the
    /// Serilog rename wave plan.
    /// </remarks>
    private static string NormalizeLevelKey(string key) => key switch
    {
        "info"     => "information",
        "warn"     => "warning",
        "critical" => "fatal",
        "trace"    => "verbose",
        _          => key,
    };
    
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

    /// <summary>
    /// Maps the JSON sampling config into a LIST of runtime filters that the FilteringLogger
    /// AND-chains — every filter must Allow the event for it to pass. This is the correct
    /// composition for the three-toggle case (sampling AND throttling AND adaptive all apply):
    /// each rule becomes its own filter in the chain.
    /// <para>
    /// <b>Why a list, not a <see cref="CompositeSamplingFilter"/>:</b> CompositeSamplingFilter
    /// is first-scope-match-wins — exactly ONE rule fires per event. That is right for
    /// per-category sampling overlays, but WRONG for "sampling AND throttling AND adaptive
    /// simultaneously," where applying only the first rule would silently skip the others (a
    /// drop-policy hazard). The FilteringLogger's AND-chain applies all of them and attributes
    /// each drop to the right filter via ClassifyReason.
    /// </para>
    /// <para>
    /// A rule that carries a scope (category / message-contains) is wrapped in a
    /// <see cref="CompositeSamplingFilter"/> of one entry so the scope still narrows WHICH
    /// events that single filter governs — scope is a per-rule "only applies to" predicate,
    /// orthogonal to the AND-chain across rules.
    /// </para>
    /// </summary>
    /// <summary>
    /// Test seam: exposes the private <see cref="MapFilters"/> so the composition tests can
    /// assert the AND-chain LIST shape (one runtime filter per JSON rule) without building a
    /// full pipeline. Internal — visible only to the test assemblies via InternalsVisibleTo.
    /// </summary>
    internal static IReadOnlyList<ILogFilter>? MapForTest(JsonSamplingConfig? config, ILogLevelRegistry registry) =>
        MapFilters(config, registry);

    private static IReadOnlyList<ILogFilter>? MapFilters(JsonSamplingConfig? config, ILogLevelRegistry registry)
    {
        if (config is null || !config.Enabled || config.Rules is not { Count: > 0 })
        {
            return null;
        }

        var filters = new List<ILogFilter>(config.Rules.Count);
        foreach (var rule in config.Rules)
        {
            var filter = MapSamplingRuleFilter(rule, registry);

            // Scope narrows which events THIS filter governs. A scoped rule wraps in a
            // one-entry CompositeSamplingFilter (first-match within the single rule) so
            // out-of-scope events pass this chain link untouched; the AND-chain across
            // rules is preserved by keeping each rule a separate list element.
            var hasScope = !string.IsNullOrWhiteSpace(rule.Category)
                           || !string.IsNullOrWhiteSpace(rule.MessageContains);
            if (hasScope)
            {
                var scope = BuildSamplingScope(rule.Category, rule.MessageContains);
                filter = new CompositeSamplingFilter(
                    new[] { new CompositeSamplingFilter.SamplingRuleEntry(scope, filter) });
            }

            filters.Add(filter);
        }

        return filters;
    }

    /// <summary>
    /// Maps one JSON sampling rule to its runtime <see cref="ILogFilter"/>. Mode is chosen
    /// by precedence: adaptive (AdaptiveNormalSampleRate &gt; 0) wins over throttling
    /// (MaxPerSecond &gt; 0), which wins over fixed-rate sampling. This mirrors the
    /// builder methods (WithAdaptiveSampling / WithThrottling / WithSampling) so a config
    /// produced by the builder round-trips to the same filter it would have built
    /// programmatically. Adaptive needs the level registry to resolve its error level,
    /// which is why the registry threads through here.
    /// </summary>
    private static ILogFilter MapSamplingRuleFilter(JsonSamplingRule rule, ILogLevelRegistry registry)
    {
        if (rule.AdaptiveNormalSampleRate > 0)
        {
            var window = rule.AdaptiveWindowMs > 0
                ? TimeSpan.FromMilliseconds(rule.AdaptiveWindowMs)
                : TimeSpan.FromSeconds(1);
            return new Addons.MetricExtraction.AdaptiveSamplingFilter(
                normalSampleRate: rule.AdaptiveNormalSampleRate,
                errorThreshold: rule.AdaptiveErrorThreshold > 0 ? rule.AdaptiveErrorThreshold : 1,
                window: window,
                levelRegistry: registry);
        }

        if (rule.MaxPerSecond > 0)
        {
            return new ThrottlingFilter(rule.MaxPerSecond, TimeSpan.FromSeconds(1), scope: null);
        }

        return new SamplingFilter(rule.SampleRate > 0 ? rule.SampleRate : 1, scope: null);
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
                var level = levelRegistry.GetByKeyOrNull(NormalizeLevelKey(categoryOverride.LevelKey))
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
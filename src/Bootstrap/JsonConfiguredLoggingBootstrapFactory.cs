#nullable enable

using System;
using System.Collections.Generic;
using MMP.Herald.Configuration;
using MMP.Herald.Configuration.Runtime;
using MMP.Herald.Expansions;
using MMP.Herald.Failures;
using MMP.Herald.Levels;
using MMP.Herald.Output.Rendering;
using MMP.Herald.Pipeline;
using MMP.Herald.Predicates;
using MMP.Herald.Metrics;
using MMP.Herald.Output.Rendering.Themes;
using MMP.Herald.Routing;
using MMP.Herald.Routing.Providers;
using MMP.Herald.Templating;
using MMP.Herald.Time;

namespace MMP.Herald.Bootstrap;

/// <summary>
/// Core bootstrap factory for JSON-configured logging.
/// Host-specific customization is optional and limited to adapters such as the rich console writer.
/// </summary>
public static class JsonConfiguredLoggingBootstrapFactory
{
    public static LoggingBootstrap Create(
        IDateTimeProvider dateTimeProvider,
        LoggingRuntimeConfiguration runtimeConfiguration,
        ILogLevelRegistry levelRegistry,
        LoggingHostAdapters? hostAdapters = null,
        IReadOnlyList<IDestructuringPolicy>? destructuringPolicies = null,
        IReadOnlyList<ILogSinkProvider>? additionalSinkProviders = null,
        IReadOnlyList<ILogEventProcessor>? eventProcessors = null,
        Enrichers.ILogEnricher? enricher = null,
        PipelineStrategy? pipelineStrategy = null,
        IReadOnlyList<Pipeline.IConfigurablePipelineDecorator>? customDecorators = null)
    {
        ArgumentNullException.ThrowIfNull(dateTimeProvider);
        ArgumentNullException.ThrowIfNull(runtimeConfiguration);
        ArgumentNullException.ThrowIfNull(levelRegistry);

        var adapters = hostAdapters ?? new LoggingHostAdapters();
        var styleResolver = new ConfigurableLogLevelStyleResolver(
            runtimeConfiguration.Presentation.LevelStyles);

        var theme = BuildConsoleTheme(runtimeConfiguration);

        var transformerRegistryFactory = new ConfiguredLogOutputTransformerRegistryFactory(
            aliasDefinitions: runtimeConfiguration.Presentation.Aliases,
            styleResolver: styleResolver,
            transformerFactory: new DefaultConfiguredTransformerFactory(theme));

        var expansionRegistryFactory = new DefaultLogLevelOutputExpansionRegistryFactory();

        var failureSink = new DiagnosticLogFailureSink(
            maxEntries: 200);

        // The metrics registry doubles as the pipeline's IPipelineDropSink.
        // Decorators with drop paths (AsyncLogger queue overflow,
        // CircuitBreakerLogger open-state reject) report through this
        // registry; the per-sink collectors surface drops as
        // herald_sink_drops_total via PrometheusMetricsRenderer. Creating
        // the registry here lets us thread it into both the sink factory
        // (for delivery / failure counters) and the policy (for drops).
        var metricsRegistry = new LogMetricsRegistry();

        var pipelinePolicyFactory = new DefaultLogPipelinePolicyFactory(
            runtimeConfiguration.PipelinePolicy.MinimumLevel,
            runtimeConfiguration.PipelinePolicy.AsyncPolicy,
            runtimeConfiguration.PipelinePolicy.BatchingPolicy,
            runtimeConfiguration.PipelinePolicy.DynamicLevelPolicy,
            runtimeConfiguration.PipelinePolicy.SamplingFilter,
            runtimeConfiguration.PipelinePolicy.PostFilteringPolicy,
            runtimeConfiguration.PipelinePolicy.HotReloadEnabled,
            eventProcessors ?? runtimeConfiguration.PipelinePolicy.EventProcessors,
            pipelineStrategy ?? runtimeConfiguration.PipelinePolicy.Strategy,
            customDecorators: customDecorators,
            dropSink: metricsRegistry,
            // The AND-chain filter list from the mapper (sampling + throttling + adaptive).
            // Supersedes the single SamplingFilter slot; the policy folds either via
            // EffectiveFilters.
            filters: runtimeConfiguration.PipelinePolicy.Filters);

        return new LoggingBootstrap(
            dateTimeProvider: dateTimeProvider,
            runtimeConfiguration: runtimeConfiguration,
            levelRegistryFactory: new FixedLogLevelRegistryFactory(levelRegistry),
            transformerRegistryFactory: transformerRegistryFactory,
            expansionRegistryFactory: expansionRegistryFactory,
            sinkRouterFactory: new DefaultLogSinkRouterFactory(
                new DefaultLogSinkFactory(BuildSinkProviderRegistry(adapters, additionalSinkProviders), failureSink, metricsRegistry),
                new DefaultLogPredicateCompiler(),
                failureSink),
            pipelineFactory: new DefaultLogPipelineFactory(
                // Mirror the eventProcessors / pipelineStrategy fallback shape:
                // explicit constructor arg wins, otherwise the runtime config
                // (populated by DefaultLoggingConfigurationMapper from
                // JsonLoggingConfig.Enrichers) drives the enricher chain so a
                // JSON-only Reload reconstructs the same enrichment as the
                // original builder produced.
                enricher: enricher ?? runtimeConfiguration.PipelinePolicy.Enricher,
                includeCallerInfo: runtimeConfiguration.IncludeCallerInfo,
                includeActivityContext: runtimeConfiguration.IncludeActivityContext,
                destructuringPolicies: destructuringPolicies is null
                    ? DestructuringPolicyRegistry.Empty
                    : new DestructuringPolicyRegistry(destructuringPolicies)),
            pipelinePolicyFactory: pipelinePolicyFactory,
            levelDumpRenderer: new ConfigurableLogLevelDumpRenderer(styleResolver),
            richConsoleWriter: adapters.ResolveRichConsoleWriter(),
            failureSink: failureSink,
            metricsRegistry: metricsRegistry,
            additionalSinkProviders: additionalSinkProviders);
    }

    // `internal` not `private` so Herald.Core.Tests can unit-test the seed
    // behavior directly. InternalsVisibleTo is already granted at the csproj
    // level. The optional `seedSource` parameter is the test seam: production
    // callers pass null and the seed comes from LogSinkProviderRegistry.Default
    // (the process-wide singleton); tests pass an isolated registry to avoid
    // cross-test contamination through the shared Default.
    internal static LogSinkProviderRegistry BuildSinkProviderRegistry(
        LoggingHostAdapters adapters,
        IReadOnlyList<ILogSinkProvider>? additionalProviders,
        LogSinkProviderRegistry? seedSource = null)
    {
        var registry = new LogSinkProviderRegistry();
        var seed = seedSource ?? LogSinkProviderRegistry.Default;

        // Seed from the process-wide LogSinkProviderRegistry.Default first.
        // Every Herald.Sinks.* assembly auto-registers its providers into
        // Default via a [ModuleInitializer] emitted by
        // MMP.Herald.Generators.SinkAutoRegistrationGenerator at the
        // sink's compile time. Pulling those in here closes the
        // "dotnet add package MMP.Herald.Sinks.X = sink works in
        // pipeline" promise end-to-end — without this seed, the
        // auto-registered providers exist in Default but never reach
        // the per-pipeline registry the runtime resolves through.
        //
        // Explicit additionalProviders (passed below) override any
        // same-kind entry from Default — Register() uses an indexer
        // overwrite, so the last write wins.
        foreach (var provider in seed.AllProviders())
        {
            registry.Register(provider);
        }

        registry.Register(new ConsoleSinkProvider(adapters.ResolveRichConsoleWriter()));
        registry.Register(new NullLogSinkProvider());
        // text_file / json_file live in the Herald.Sinks monorepo as
        // MMP.Herald.Sinks.File. Consumers opt in via
        // FileSinkRegistration.RegisterAll on the registry, or via the
        // QuickLogBuilder.WithFileSinkProviders() extension method, or
        // by passing the providers through additionalSinkProviders.
        // HttpJson / TcpJsonLine / UdpJsonLine live in the Herald.Sinks
        // monorepo as MMP.Herald.Sinks.HttpJson / .TcpJsonLine / .UdpJsonLine.
        // Consumers opt in via the RegisterAll helpers on each NuGet or
        // by passing the provider through additionalSinkProviders.
        // OTLP JSON / OTLP protobuf / protobuf-file live in the Herald.Sinks
        // monorepo as MMP.Herald.Sinks.Otlp. Consumers opt in via
        // OtlpSinkRegistration.RegisterAll (or the per-kind helpers) on
        // the registry, or by passing the providers through
        // additionalSinkProviders.
        //
        // Elasticsearch, Slack, and GenericWebhook live in the Herald.Sinks
        // monorepo as MMP.Herald.Sinks.Elasticsearch / .Slack / .GenericWebhook.
        // Consumers opt in via the <Name>SinkRegistration.RegisterAll helper
        // or by passing the provider through additionalSinkProviders.
        //
        // Azure-backed sink providers (ApplicationInsights, AzureTables) live
        // in the sibling Herald.Core.Azure assembly so Core stays AOT-clean.
        // Consumers who want them call AzureSinkRegistration.RegisterAll on
        // this registry after construction, or pass the providers via the
        // additionalProviders parameter below.
        //
        // Seq / Splunk / Honeycomb / Datadog / Loki / SignalFx / Sentry /
        // PagerDuty live in the Herald.Sinks monorepo. Consumers opt in by
        // calling <Name>SinkRegistration.RegisterAll on the registry or by
        // passing the provider via additionalSinkProviders.

        if (additionalProviders is not null)
        {
            foreach (var provider in additionalProviders)
            {
                registry.Register(provider);
            }
        }

        return registry;
    }

    private static IConsoleTheme BuildConsoleTheme(
        LoggingRuntimeConfiguration runtimeConfiguration)
    {
        var presentation = runtimeConfiguration.Presentation;

        // If a named theme is configured, use it as base with optional overrides.
        if (presentation.Theme is not null)
        {
            return BuildFromThemeConfig(presentation.Theme);
        }

        // Backward compatibility: build theme from legacy levelStyles + propertyStyles.
        return BuildFromLegacyStyles(presentation);
    }

    private static IConsoleTheme BuildFromThemeConfig(
        Configuration.Runtime.LoggingRuntimeThemeConfiguration themeConfig)
    {
        var baseTheme = ResolveBaseTheme(themeConfig.ThemeName);

        if (themeConfig.Overrides is not { Count: > 0 })
        {
            return baseTheme;
        }

        // Merge base theme entries with overrides.
        var entries = new List<ConfigurableConsoleTheme.ThemeEntry>();

        // Add overrides from config.
        foreach (var ov in themeConfig.Overrides)
        {
            var element = ParseElementKind(ov.Element);

            if (element is not null)
            {
                entries.Add(new ConfigurableConsoleTheme.ThemeEntry(
                    element,
                    new OutputStyle(ov.ColorName, ov.UseBold, ov.UseItalic, ov.BackgroundColorName),
                    ov.Qualifier));
            }
        }

        // If there's a base theme, wrap it so overrides take priority.
        if (baseTheme is not ConfigurableConsoleTheme)
        {
            return new OverlayConsoleTheme(baseTheme, new ConfigurableConsoleTheme(entries));
        }

        // For a configurable base, merge all entries (base + overrides, overrides last).
        return new ConfigurableConsoleTheme(entries);
    }

    private static IConsoleTheme BuildFromLegacyStyles(
        Configuration.Runtime.LoggingRuntimePresentationConfiguration presentation)
    {
        var entries = new List<ConfigurableConsoleTheme.ThemeEntry>
        {
            // Default element styles matching previous ConsoleOutputTransformer behavior.
            new(new OutputElementKind.Timestamp(), new OutputStyle("dim_gray")),
            new(new OutputElementKind.Category(), new OutputStyle(UseItalic: true)),
        };

        // Map level styles to LevelText entries with qualifiers.
        foreach (var levelStyle in presentation.LevelStyles)
        {
            entries.Add(new ConfigurableConsoleTheme.ThemeEntry(
                new OutputElementKind.LevelText(),
                new OutputStyle(levelStyle.ColorName, levelStyle.UseBold, levelStyle.UseItalic, levelStyle.BackgroundColorName),
                levelStyle.LevelKey));
        }

        // Map property styles to PropertyValue entries with qualifiers.
        foreach (var propStyle in presentation.PropertyStyles)
        {
            entries.Add(new ConfigurableConsoleTheme.ThemeEntry(
                new OutputElementKind.PropertyValue(),
                new OutputStyle(propStyle.ColorName, propStyle.UseBold, propStyle.UseItalic, propStyle.BackgroundColorName),
                propStyle.PropertyName));

            entries.Add(new ConfigurableConsoleTheme.ThemeEntry(
                new OutputElementKind.PropertyName(),
                new OutputStyle(propStyle.ColorName, propStyle.UseBold, propStyle.UseItalic, propStyle.BackgroundColorName),
                propStyle.PropertyName));
        }

        // Map category (channel) styles to Category entries with qualifiers.
        // The default Category entry above keeps its italic fallback, so
        // unknown category names still render the way they always have;
        // known names pick up the configured colour.
        foreach (var catStyle in presentation.CategoryStyles ?? [])
        {
            entries.Add(new ConfigurableConsoleTheme.ThemeEntry(
                new OutputElementKind.Category(),
                new OutputStyle(catStyle.ColorName, catStyle.UseBold, catStyle.UseItalic, catStyle.BackgroundColorName),
                catStyle.CategoryName));
        }

        return new ConfigurableConsoleTheme(entries);
    }

    private static readonly Dictionary<string, IConsoleTheme> ThemeMap = new(StringComparer.OrdinalIgnoreCase)
    {
        [Services.JsonConfigProperties.ThemeDark] = BuiltInConsoleThemes.Dark,
        [Services.JsonConfigProperties.ThemeLight] = BuiltInConsoleThemes.Light,
        [Services.JsonConfigProperties.ThemeLiterate] = BuiltInConsoleThemes.Literate,
        [Services.JsonConfigProperties.ThemeNone] = BuiltInConsoleThemes.None,
    };

    private static IConsoleTheme ResolveBaseTheme(string? themeName) {
        return themeName is not null && ThemeMap.TryGetValue(themeName, out var theme)
            ? theme
            : BuiltInConsoleThemes.Dark;
    }

    private static readonly Dictionary<string, OutputElementKind> ElementKindMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["timestamp"] = OutputElementKind.Timestamp.Instance,
        ["leveltext"] = OutputElementKind.LevelText.Instance,
        ["category"] = OutputElementKind.Category.Instance,
        ["separator"] = OutputElementKind.Separator.Instance,
        ["messagetext"] = OutputElementKind.MessageText.Instance,
        ["propertyname"] = OutputElementKind.PropertyName.Instance,
        ["propertyvalue"] = OutputElementKind.PropertyValue.Instance,
        ["stringvalue"] = OutputElementKind.StringValue.Instance,
        ["numericvalue"] = OutputElementKind.NumericValue.Instance,
        ["nullvalue"] = OutputElementKind.NullValue.Instance,
        ["exceptiontext"] = OutputElementKind.ExceptionText.Instance,
        ["punctuation"] = OutputElementKind.Punctuation.Instance,
    };

    private static OutputElementKind? ParseElementKind(string element) {
        return ElementKindMap.TryGetValue(element, out var kind) ? kind : null;
    }

    /// <summary>
    /// Simple overlay that checks an override theme first, then falls back to a base theme.
    /// </summary>
    private sealed class OverlayConsoleTheme : IConsoleTheme
    {
        private readonly IConsoleTheme _baseTheme;
        private readonly IConsoleTheme _overlay;

        public OverlayConsoleTheme(IConsoleTheme baseTheme, IConsoleTheme overlay)
        {
            _baseTheme = baseTheme;
            _overlay = overlay;
        }

        public OutputStyle? Resolve(OutputElementKind element, string? qualifier = null)
        {
            return _overlay.Resolve(element, qualifier) ?? _baseTheme.Resolve(element, qualifier);
        }
    }
}
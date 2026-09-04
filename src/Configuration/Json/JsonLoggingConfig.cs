#nullable enable

using System.Collections.Generic;

namespace MMP.Herald.Configuration.Json;

/// <summary>
/// JSON-facing root logging configuration.
/// </summary>
public sealed record JsonLoggingConfig(
    JsonLogLevelsConfig Levels,
    string MinimumLevel,
    JsonAsyncLogPolicyConfig Async,
    JsonBatchingPolicyConfig? Batching,
    bool DumpRegisteredLevelsToConsole,
    IReadOnlyList<JsonLogOutputAliasConfig> Aliases,
    IReadOnlyList<JsonLogLevelStyleConfig> LevelStyles,
    IReadOnlyList<JsonLogSinkConfig> Sinks,
    IReadOnlyList<JsonLogRouteConfig> Routes,
    bool IncludeCallerInfo = false,
    bool IncludeActivityContext = false,
    JsonDynamicLevelConfig? DynamicLevels = null,
    JsonSamplingConfig? Sampling = null,
    JsonPostFilteringConfig? PostFiltering = null,
    JsonFlightRecorderConfig? FlightRecorder = null,
    JsonHotReloadConfig? HotReload = null,
    IReadOnlyList<JsonPropertyStyleConfig>? PropertyStyles = null,
    JsonConsoleThemeConfig? ConsoleTheme = null,
    IReadOnlyList<JsonPipelineStepConfig>? PipelineSteps = null,
    // Strategy *selection* — "default" / "minimal" / "filterEarly" / "custom".
    // PipelineSteps already carries the per-step ordering and config, but the
    // selection itself was lost on round-trip: a builder that called
    // WithPipelineStrategy(PipelineStrategy.FilterEarly()) had no way to
    // reload the same strategy from JSON, because the factory's default kicks
    // in when no strategy is passed. Null preserves the existing default
    // behaviour (factory uses PipelineStrategy.Default()).
    string? PipelineStrategyName = null,
    string? PipelineName = null,
    IReadOnlyList<JsonChannelConfig>? Channels = null,
    IReadOnlyList<JsonEnricherConfig>? Enrichers = null,
    // Custom pipeline decorators (plugin-supplied). Pre-refactor these
    // travelled only as in-memory instances handed to the bootstrap
    // factory and were dropped on every Reload, so a Lean deploy reading
    // the same JSON ended up with a chain missing every host-attached
    // decorator. The Kind on each entry routes through DecoratorJsonRegistry
    // at reconstruction time.
    IReadOnlyList<JsonPipelineDecoratorConfig>? PipelineDecorators = null,
    IReadOnlyList<string>? EventProcessors = null,
    int? BridgeCount = null,
    int? AuditSinkCount = null,
    IReadOnlyList<string>? CustomSinkProviders = null,
    JsonFileRollingConfig? FileRolling = null,
    IReadOnlyList<JsonLogCategoryStyleConfig>? CategoryStyles = null,
    string? TestLoopbackUrl = null,
    string? TestLoopbackLogDir = null,
    int LoopbackEntriesPerFile = 1000,
    bool LoopbackUseNdjson = true,
    // Kernel-aware fast-path companions. Each one is null when not
    // configured; the JSON-driven Reload re-installs them on the
    // StructuredLogger so runtime configuration changes preserve the
    // fast-path shape. None of these disqualify the kernel — that's the
    // whole point of the fast-path family. See
    // Modules/Core/docs/kernel-fast-path-pattern.md for the design.
    JsonFastPathRedactionConfig? FastPathRedaction = null,
    JsonFastPathSamplingConfig? FastPathSampling = null,
    JsonFastPathEnrichmentConfig? FastPathEnrichment = null,
    JsonFastPathDynamicLevelConfig? FastPathDynamicLevel = null,
    JsonFastPathAsyncSinkConfig? FastPathAsyncSink = null,
    // Property-naming policy identifier. Resolved at Reload time via
    // NamingPolicyRegistry.Resolve(id). Null/omitted defaults to "pascal"
    // (Herald.OSS 1.0+ default); other built-ins are "camel" (camelCase
    // property keys) and "snake" (OpenTelemetry-friendly). Custom
    // policy ids must be registered before the first Reload that names
    // them, or Reload throws UnknownNamingPolicyException (cold start) /
    // emits a ReloadDegraded diagnostic and keeps the prior policy
    // (hot reload). See PropertyNamingPolicy + NamingPolicyRegistry.
    string? NamingPolicy = null,
    // External-event-injection consent. True only when the builder called
    // AllowExternalEventInjection(). Serialized so a pipeline restored from
    // JSON preserves the consent and a hot reload does not silently re-arm the
    // refusal. Default false = injection refused loudly at the Log(LogEvent)
    // port. See docs/design/external-event-injection-switch.md.
    bool AllowExternalEventInjection = false,
    // Config-file schema version. Absent in a file written before the field
    // existed, which deserializes to 1 through this default, so every existing
    // config keeps loading. LoggingJsonSerializer.Deserialize refuses any other
    // value rather than parsing a newer schema with its unknown fields dropped.
    // See UnsupportedConfigSchemaVersionException.
    int SchemaVersion = 1);

/// <summary>Channel sink reference in the config.</summary>
public sealed record JsonChannelConfig(string Name);
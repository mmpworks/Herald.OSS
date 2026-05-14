# Herald Core Addons Catalog

Operational reference for every addon under `Modules/Core/src/Addons/`. Each row names the edition that gates the addon, the threading contract callers can rely on, and the test (or benchmark) that exercises it. The class-level xmldoc on each file carries the *what* and the *how-to-use* — this catalog carries the *what-edition / how-safe / where-tested* facts in one place so an operator wiring a pipeline does not have to scrape it from xmldoc files one by one.

Edition gating is enforced at the source by `IComponentMetadata.MinimumEdition` and `HeraldEditionGate`. When the catalog says `Pro` or `Enterprise`, registration on a lower edition throws at bootstrap — the front-end reflects the decision; the back-end enforces it.

## How to read the threading column

| Phrase | What it means |
|---|---|
| **Immutable** | Constructor-set fields, no mutation after construction. Safe to share across threads without coordination. |
| **Thread-safe** | Public methods may be called from any thread. Internal coordination uses one of: `Interlocked`, `Volatile`, `ConcurrentDictionary`, `Channel<T>`, `SemaphoreSlim`, or a `lock` on a private object. |
| **Single-writer** | The addon assumes one writing thread (typically the AsyncLogger drain). Reads from other threads are safe but concurrent writes are not. |
| **Caller-coordinated** | The addon delegates synchronization to the caller. Common for write-once-then-read configuration types. |

## Catalog

### Archive

| Addon | Edition | Threading | Tests / Benchmarks |
|---|---|---|---|
| `SinkArchivePolicy` | Pro (tar) / Enterprise (cloud) | Immutable record | (covered by orchestrator + provider tests) |
| `IArchiveProvider` | Pro (tar) / Enterprise (cloud) | Provider implementations document their own contract | (interface, no direct test) |
| `ArchiveResult` | Pro | Immutable record | (covered by orchestrator + provider tests) |
| `SinkArchiveCheckpoint` | Pro | Caller-coordinated — atomic write through temp+rename | `tests/Addons/Archive/SinkArchiveOrchestratorTests.cs` |
| `SinkArchiveCheckpointStatus` | Pro | Immutable enum | (covered by checkpoint tests) |
| `SinkArchiveOrchestrator` | Pro | Single-writer per file path; concurrent calls with different paths are safe | `tests/Addons/Archive/SinkArchiveOrchestratorTests.cs` |
| `LocalTarArchiveProvider` | Pro | Stateless — concurrent calls with different paths are independent | `tests/Addons/Archive/LocalTarArchiveProviderTests.cs` |
| `S3ArchiveProvider` | Enterprise | Stateless — a fresh `AmazonS3Client` per call; safe to share the provider across orchestrators | `tests/Addons/Archive/S3ArchiveProviderTests.cs` |
| `AzureBlobArchiveProvider` | Enterprise | Stateless — a fresh `BlobContainerClient` per call | `tests/Addons/Archive/AzureBlobArchiveProviderTests.cs` |
| `IStreamingArchiveProvider` | Enterprise | Providers safe for concurrent `OpenAsync`; sessions are single-owner | (interface, no direct test) |
| `IStreamingArchiveSession` | Enterprise | Single-owner; caller serialises `AppendAsync`. Final flush on `DisposeAsync` | (contract covered by `StreamingArchiveLoggerTests.cs`) |
| `StreamingArchivePolicy` | Enterprise | Immutable record | `tests/Addons/Archive/StreamingArchivePolicyTests.cs` |
| `StreamingArchiveLogger` | Enterprise | Single-owner of the session; `DisposeAsync` flushes and closes | `tests/Addons/Archive/StreamingArchiveLoggerTests.cs` |
| `AzureBlobStreamingArchiveProvider` | Enterprise | Stateless provider; per-session append-blob client held by the session | `tests/Addons/Archive/AzureBlobStreamingArchiveProviderTests.cs` |

### AzureSinks

| Addon | Edition | Threading | Tests / Benchmarks |
|---|---|---|---|
| `ApplicationInsightsLogSink` | Enterprise | Thread-safe — shared `HttpClient`, synchronous `Send`; caller is expected to wrap in a batching decorator | `tests/Addons/AzureSinks/ApplicationInsightsLogSinkTests.cs` |
| `ApplicationInsightsConnectionString` | Enterprise | Immutable record — parsed once at construction | `tests/Addons/AzureSinks/ApplicationInsightsLogSinkTests.cs` |
| `ApplicationInsightsSeverityMapper` | Enterprise | Immutable — pure mapping table | (covered by sink tests) |
| `ApplicationInsightsLogSinkProvider` | Enterprise | Caller-coordinated — bootstrap time | `tests/Addons/SinkProviderCoverageTests.cs` |
| `AzureTableLogSink` | Enterprise | Thread-safe — row sequence under `Interlocked.Increment`; `TableClient` is thread-safe by SDK contract | `tests/Addons/AzureSinks/AzureTableLogSinkTests.cs` |
| `AzureTablePartitionKeyStrategy` | Enterprise | Immutable enum | (covered by sink tests) |
| `AzureTableLogSinkProvider` | Enterprise | Caller-coordinated — bootstrap time | `tests/Addons/SinkProviderCoverageTests.cs` |

The Seq / Splunk / Honeycomb / Datadog / Loki / Sentry / PagerDuty destination sinks ship as Enterprise NuGets under the Herald.Sinks monorepo (`MMP.Herald.Sinks.Seq`, `.Splunk`, `.Honeycomb`, `.Datadog`, `.Loki`, `.Sentry`, `.PagerDuty`). Consumers opt in by referencing the package and calling `<Name>SinkRegistration.RegisterAll` on the sink-provider registry.

### BinarySerialization

| Addon | Edition | Threading | Tests / Benchmarks |
|---|---|---|---|
| `MessagePackLogFormatter` | Pro | Thread-safe — stateless formatter | `tests/Addons/MessagePackLogFormatterTests.cs` |

### CommunityTransports

The Elasticsearch / Slack / GenericWebhook destination sinks (plus the `WebhookRule` / `WebhookRuleCondition` / `WebhookRuleEngine` rules engine that the generic webhook carries) now ship under the Herald.Sinks monorepo as `MMP.Herald.Sinks.Elasticsearch`, `MMP.Herald.Sinks.Slack`, and `MMP.Herald.Sinks.GenericWebhook`. Consumers opt in by referencing the package and calling `<Name>SinkRegistration.RegisterAll` on the sink-provider registry. The rules engine is invoked via `GenericWebhookSinkRegistration.RegisterWithRules(registry, rules)`.

### Compliance

| Addon | Edition | Threading | Tests / Benchmarks |
|---|---|---|---|
| `ComplianceContextKeys` | Community | Immutable — string constants | (compile-time constants, no test surface) |
| `HmacChainLogger` | Enterprise | Thread-safe — sequence under `Interlocked.Increment`, hash chain under `lock` | `tests/Addons/AddonTests.cs`, `tests/Compliance/HmacChainLoggerTests.cs` |
| `RedactionRuleParser` | Enterprise | Thread-safe — stateless static parser; all state lives in the returned `CompiledRedactionRule` | `tests/Addons/Compliance/RedactionRuleParserTests.cs` |
| `RedactionRuleParseException` | Enterprise | Immutable — thrown by `RedactionRuleParser` when rule head or predicate fails to parse | (surface asserted by parser tests) |
| `SequenceNumberEnricher` | Community | Thread-safe — counter under `Interlocked.Increment` | `tests/Enrichers/EnricherTests.cs` |

### GameEnrichers

| Addon | Edition | Threading | Tests / Benchmarks |
|---|---|---|---|
| `BuildInfoEnricher` | Community | Immutable — captured at construction | `tests/Enrichers/EnricherTests.cs` |
| `GameContextKeys` | Community | Immutable — string constants | (compile-time constants) |
| `PlayerEnricher` | Community | Thread-safe — mutable player id under `volatile` | `tests/Enrichers/EnricherTests.cs` |
| `SceneEnricher` | Community | Thread-safe — mutable scene name under `volatile` | `tests/Enrichers/EnricherTests.cs` |
| `SessionEnricher` | Community | Thread-safe — session id swapped via `Interlocked.Exchange` | `tests/Enrichers/EnricherTests.cs` |

### GamePerformance

| Addon | Edition | Threading | Tests / Benchmarks |
|---|---|---|---|
| `BreadcrumbTrail` | Pro | Thread-safe — fixed ring buffer with `Interlocked` head index | `tests/Addons/AddonTests.cs` |
| `ConditionalDebugLogger` | Community | Thread-safe — conditional gate is `volatile bool` | `tests/Addons/AddonCoverageTests.cs` |
| `CrashSafeRingBuffer` | Pro | Thread-safe — `Interlocked` head/tail, lock-free reads | `tests/Addons/AddonTests.cs` |
| `FlightRecorderLogger` | Enterprise | Thread-safe — events captured into a `CrashSafeRingBuffer` | `tests/Pipeline/FlightRecorderLoggerTests.cs`, `tests/Addons/AddonTests.cs` |
| `FrameBudgetLogger` | Pro | Single-writer — designed for the game-loop tick, one thread per frame | `tests/Addons/AddonTests.cs` |
| `HotPathLogger` | Pro | Thread-safe — IsEnabled is `Volatile.Read`, no per-call allocation | `tests/Addons/HotPathLoggerTests.cs`, `tests/Addons/HotPathLoggerFactoryTests.cs`, `tests/Pipeline/PipelineTimingTests.cs`. **Bench**: `benchmarks/HotPathBenchmarks.cs` |
| `HotPathStringHandler` | Pro | Stack-only — `ref struct`, never escapes the stack frame | (covered indirectly by HotPathLogger tests) |

### Instrumentation

| Addon | Edition | Threading | Tests / Benchmarks |
|---|---|---|---|
| `InstrumentAttribute` | Community | Immutable — attribute metadata | (marker attribute, no runtime test) |
| `SpanScope` | Community | Single-writer — instance is per-call, owned by the caller's stack | `tests/Spans/SpanFactoryTests.cs` |

### ManagementApi

| Addon | Edition | Threading | Tests / Benchmarks |
|---|---|---|---|
| `HeraldManagementApi` | Pro | Thread-safe — wraps a `HeraldRegistry`, internal state via `ConcurrentDictionary` | `tests/Addons/AddonTests.cs`, `tests/Addons/PluginSystemTests.cs`, `tests/Addons/ChannelManagementTests.cs` |
| `LiveLogCapture` | Pro | Thread-safe — bounded `Channel<T>` reader/writer | `tests/Addons/LiveLogCaptureTests.cs` |
| `SampleDataGenerator` | Pro | Thread-safe — `Random` instance gated by `lock` | `tests/Addons/AddonTests.cs` |

### MelAdapter

| Addon | Edition | Threading | Tests / Benchmarks |
|---|---|---|---|
| `HeraldLoggerProvider` | Community | Thread-safe — wraps a `StructuredLogger`, no provider-side state | `tests/Addons/MelAdapterTests.cs` |

### MetricExtraction

| Addon | Edition | Threading | Tests / Benchmarks |
|---|---|---|---|
| `AdaptiveSamplingFilter` | Pro | Thread-safe — error counter and sampling state under `Interlocked` | `tests/Filters/AdaptiveSamplingFilterTests.cs` (and existing filter integration tests) |
| `LogDeduplicationProcessor` | Pro | Thread-safe — `ConcurrentDictionary<string, DedupeEntry>` | `tests/Addons/LogDeduplicationProcessorTests.cs` |
| `LogMetricExtractor` | Pro | Thread-safe — counter map is `ConcurrentDictionary` | `tests/Metrics/LogMetricExtractorTests.cs` |
| `MetricContextKeys` | Community | Immutable — string constants | (compile-time constants) |

### NetworkTransports

| Addon | Edition | Threading | Tests / Benchmarks |
|---|---|---|---|
| `HealthEndpointExporter` | Pro | Thread-safe — runs an isolated `HttpListener` loop | `tests/Addons/AddonCoverageTests.cs` |

The HTTP / TCP / UDP JSON-line sinks ship as separately-versioned NuGets under the Herald.Sinks monorepo: `MMP.Herald.Sinks.HttpJson`, `MMP.Herald.Sinks.TcpJsonLine`, `MMP.Herald.Sinks.UdpJsonLine`. Consumers opt in by referencing the package and calling `<Name>SinkRegistration.RegisterAll` on the sink-provider registry.

### Observability

| Addon | Edition | Threading | Tests / Benchmarks |
|---|---|---|---|
| `CardinalityGuardProcessor` | Pro | Thread-safe — per-property counters via `ConcurrentDictionary<string, HashSet>` with `lock` on the inner set | `tests/Addons/CardinalityGuardTests.cs`, `tests/Addons/NextWaveAddonTests.cs` |
| `ErrorBudgetMonitor` | Pro | Thread-safe — error counter is `Interlocked.Increment` | `tests/Addons/ErrorBudgetMonitorTests.cs` |
| `TraceContextPropagator` | Pro | Thread-safe — pure functions over W3C trace context | `tests/Addons/TraceContextPropagatorTests.cs` |

### OtlpSinks (decoder-side only)

The three OTLP **sinks** (JSON, protobuf, protobuf-file) and their shared serializer shipped to `MMP.Herald.Sinks.Otlp` under the Herald.Sinks monorepo. What remains in Core is the **receiver-side** surface — decoders that parse incoming OTLP payloads — which is expected to move to a future `Herald.Receivers.Otlp` package.

| Addon | Edition | Threading | Tests / Benchmarks |
|---|---|---|---|
| `OtelLogRecord` | Pro | Immutable record | (serializer-based round-trip tests removed with the sink migration; restore when Herald.Receivers.Otlp lands) |
| `OtlpJsonDecoder` | Pro | Thread-safe — stateless | (pending; see above) |
| `OtlpLogsDecoder` | Pro | Thread-safe — stateless façade | (pending) |
| `OtlpMetricsExporter` | Pro | Thread-safe — counters via `ConcurrentDictionary` | (pending) |
| `OtlpProtobufLogDecoder` | Pro | Thread-safe — stateless decoder | (pending) |

### QualityChecks

| Addon | Edition | Threading | Tests / Benchmarks |
|---|---|---|---|
| `LogSchemaRegistry` | Community | Thread-safe — `ConcurrentDictionary` of schemas | `tests/Addons/LogSchemaRegistryTests.cs` |
| `SentenceLogDetector` | Community | Thread-safe — pure detector, no state | `tests/Addons/SentenceLogDetectorTests.cs` |
| `StrategyValidator` | Community | Thread-safe — stateless validator | `tests/Pipeline/PipelineStrategyTests.cs` |

### Query

| Addon | Edition | Threading | Tests / Benchmarks |
|---|---|---|---|
| `LogEventQuery` | Community | Immutable — compiled query is read-only | `tests/Addons/Query/LogEventQueryTests.cs` |
| `QueryEvaluator` | Community | Thread-safe — pure evaluator over a compiled expression tree | `tests/Addons/Query/QueryEvaluatorTests.cs` |
| `QueryExpression` | Community | Immutable record tree | (covered by evaluator/parser tests) |
| `QueryParseException` | Community | Immutable | (covered by parser tests) |
| `QueryParser` | Community | Thread-safe — pure parser, no state | `tests/Addons/Query/QueryParserTests.cs` |
| `QueryToken` | Community | Immutable record | (covered by tokenizer tests) |
| `QueryTokenizer` | Community | Thread-safe — pure tokenizer | `tests/Addons/Query/QueryTokenizerTests.cs` |
| `LogFileSearcher` | Community | Thread-safe — stateless static class, streaming file reads | `tests/Addons/Query/LogFileSearcherTests.cs` |
| `LogFileSearchResult` | Community | Immutable record | (covered by searcher tests) |
| `ExpressionLogFilter` | Community | Immutable — compiled LogEventQuery wrapped as ILogFilter | `tests/Addons/Query/ExpressionLogFilterTests.cs` |

### Replay

| Addon | Edition | Threading | Tests / Benchmarks |
|---|---|---|---|
| `LogReplayReader` | Pro | Single-writer — stream reader is owned by the caller's iterator loop | `tests/Replay/LogReplayReaderTests.cs` |

### Reduction

| Addon | Edition | Threading | Tests / Benchmarks |
|---|---|---|---|
| `WindowedMeanRule` | Community | Immutable record | (covered by `WindowedMeanLogger` tests) |
| `WindowedMeanLogger` | Community | Thread-safe — `ConcurrentDictionary` of per-(category, rule) state, per-state lock guarding the short accumulate / emit transition | `tests/Addons/Reduction/WindowedMeanLoggerTests.cs` |
| `WindowedMeanStepHandler` | Community | Stateless handler; the decorator it installs holds the state | (covered by `WindowedMeanLogger` tests) |

## Adding a new addon

When you add a public addon under `src/Addons/<Group>/`:

1. Class-level xmldoc with `<summary>` carrying *what it is*, *when to use vs alternatives*, and *one short usage example*.
2. Add a row to this catalog with the edition, threading contract, and the test file that exercises it.
3. If the addon has a hot path that benefits from a benchmark, add the benchmark file under `benchmarks/` and reference it in the catalog row.

The pattern is enforced through code review, not a CI gate today — the catalog's value is only as strong as the discipline of the contributor adding the row. A future enhancement could parse this README at test time and assert every addon `*.cs` file has a corresponding row.

# Fork Scope — Herald.OSS

This document records what was stripped from Herald.Core to produce
Herald.OSS, and why. Treat it as the authoritative reference when
reconciling diffs against upstream Herald.Core.

Snapshot date: 2026-05-16 (0.2.1 synthesis pass)
Source commit (Herald.Core): `98d23fd` (post-Option-E redaction patch)

## 0.2.1 reconciliation note

The 0.2.0 release stripped `HeraldEdition`, `MinimumEdition`,
`GenSource`, and `GenSourceGatedSink` on the read-of-the-time that
they were inert residue with no consumer. The 0.2.1 release restored
those types as Enterprise-gotcha hooks per the broader Herald
architectural philosophy that consumer-facing hooks stay present in
OSS even when OSS itself enforces nothing against them — a downstream
commercial wrapper can plug into the well-known property and decorator
names without editing OSS source. The inventory tables below record
the current state. Where 0.2.0 said "removed" and 0.2.1 said
"restored," the row reads "retained" with a note. Where the hook is
still pending lift, the row reads "deferred."

## What stays the same

- License: Apache 2.0 (identical to Herald.Core's `LICENSE`).
- Root namespace: `MMP.Herald.*`. Type names are unchanged.
- Kernel + pipeline shape: `StructuredLogger`, `LogEventBuffer`,
  `IKernelSink`, the four accept-path call shapes (typed-args, params-span,
  interpolated, level-bound).
- Source generators that produce non-gated API surface
  (`TypedArgsOverloadGenerator`).
- Redaction fast-path (`FastPathRedactor`, `RedactionHelper`).
- The kernel-aware redactor + enricher hooks on `StructuredLogger`.

## What was stripped

### 1. Edition machinery — gate stripped, badge retained (v0.2.1)

The runtime gate that *enforces* edition restrictions is gone; OSS
runs as a single edition with no behavior tied to the value. The
informational badge — `HeraldEdition` plus `ILogSinkProvider.MinimumEdition`
— is retained as the hook a downstream commercial wrapper reads to
decide what to admit. OSS itself reads nothing.

| Path | Status |
|---|---|
| `src/HeraldEdition.cs` | **Retained (0.2.1).** Sealed record with `Community` / `Pro` / `Enterprise` instances and an `Includes(required)` ranking comparison. OSS does not enforce; downstream wrappers read it. |
| `src/HeraldEditionGate.cs` | Removed in 0.1.0. The runtime gate that rejected gated APIs at non-matching editions does not ship in OSS. |
| `src/Licensing/` | Removed in 0.1.0. Licensing infrastructure that backed the gate. |
| `tests/HeraldEditionGateTests.cs` | Removed in 0.1.0. Tests for the removed gate machinery. |
| `ILogSinkProvider.MinimumEdition` | **Retained (0.2.1).** Default interface property returning `HeraldEdition.Community`. Sinks override to surface a tier intent; OSS routes everything regardless of value. |
| `HeraldTenant.EnsureAllowedForCurrentEdition` | Removed in 0.2.0. The empty-body validation hook was the only piece with no downstream consumer. |

### 2. Provenance gate — carrier + decorator retained, registrar deferred (v0.2.1)

The provenance carrier and the gate decorator that consumes it are
both present. OSS does not stamp `GenSource` by default and does not
wrap any sink with the gate by default, so out-of-the-box behavior is
unchanged from a 0.2.0 read. A downstream commercial wrapper that
wants multi-tenant routing without per-sink code stamps the field at
construction time and wraps select sinks with the gate — no edit to
OSS source required. The external-caller registrar that turns the
gate into an operational multi-tenant surface is deferred to B-7;
the gate primitive is independently usable without it.

| Path | Status |
|---|---|
| `src/Pipeline/Kernel/GenSourceGatedSink.cs` | **Retained (0.2.1).** Wraps any `ILogger` and only forwards events whose `GenSource` matches the gate's reference token or a registered accepted source. Reference-equality fast path plus copy-on-write `HashSet` fallback. `GenSourceGatedKernelSink` is the `IKernelSink` variant. |
| `src/Pipeline/Kernel/ExternalSourceRegistrar.cs` | **Deferred to B-7.** HMAC-derived keys, anti-replay timestamp lock, and pluggable persistence. Plan documented in `Herald/wiki/designs/b7-external-source-registrar.md`. The gate primitive does not require it; the registrar is operational sugar. |
| `GenSource` field on `LogEvent`, `LogEventBuffer` | **Retained (0.2.1).** Optional `string?` parameter, null default; existing callers compile unchanged. The `ToLogEvent` materialisation path on `LogEventBuffer` propagates the stamp into the heap event. |
| `LogEventFactory`, `DeferredLogEventFactory`, `ILogEventFactory` GenSource plumbing | Removed in 0.2.0. The factory layer never carried tenant intent; downstream wrappers stamp `GenSource` at the call site instead of plumbing it through the factory. |
| `_genSource` threading through `StructuredLogger` and `DefaultLogPipelineFactory` | Removed in 0.2.0. Wrappers that need a default stamp wrap the pipeline themselves. |
| `GenSource: ...` arguments to `LogEvent` / `LogEventBuffer` constructions in `HotPathLogger` and `WindowedMeanLogger` | Removed in 0.2.0. These OSS-internal callers stamp nothing. |

Out-of-the-box: events arrive at sinks with `GenSource = null`, no
gate is in the chain, behavior matches 0.2.0. A downstream wrapper
that wants the gate composes it at construction time and reads the
informational `MinimumEdition` from §1 alongside.

### 3. Source-gen analyzer that emits gate checks

The HERALDxxx analyzers in `Modules/Generators/` warn callers about
gated APIs at compile time. In Herald.OSS there are no gated APIs, so
the analyzer produces no useful warnings. The Herald.OSS build does
not reference the gate-enforcement generator output. The
`TypedArgsOverloadGenerator` (non-gate) is kept.

### 4. Distribution hardening

Removed: the IP-protection step that obfuscates type/member names in
the Pro / Enterprise release pipeline.

| Path | Reason |
|---|---|
| `obfuscar.xml` | Obfuscar configuration template. |
| `obfuscar.resolved.xml` | Per-target-framework resolved config. |
| `build.sh` Obfuscar invocation | The build script's obfuscation step is removed. |
| `bin/Release/{Edition}/.../protected/` output path | The hardened-output path doesn't exist. |
| `promote.sh` / `promote.ps1` | The release promotion scripts assume the paid release pipeline. |

### 5. Pro / Enterprise-only addons

No addon subdirectories under `src/Addons/` are removed in Herald.OSS.
The original strip plan considered pulling `ManagementApi/` because
Herald.Core gates it behind the Pro edition; that decision was
reversed before v0.1.0 shipped. With the gate machinery gone (see §1),
every addon Herald.Core carries is available at the source level here
too. Edition labels in `src/Addons/README.md` and in each addon's own
xmldoc describe the *upstream* gating intent and have no runtime
effect in Herald.OSS.

See the "What's preserved in `src/Addons/`" section below for the
full list of subdirectories that ship in the OSS distribution.

### 6. Documentation

Removed: all of `Modules/Core/docs/`, `CLA/`, `manifests/`, the
benchmark history under `docs/benchmarks/`, and the `Herald/` Obsidian
vault.

Rationale: the upstream docs discuss edition gating + paid-distribution
mechanics that don't apply here. Herald.OSS's `docs/` was reseeded
deliberately to describe only the OSS surface — howtos
(quickstart, sinks, operations), guides (architecture, building-sinks,
kernel-sink-pattern, aot-and-trimming, security-overview), benchmarks
(consolidated + per-bench records), and testing conventions.

### 7. Tests + benchmarks

Tests: copied selectively. `HeraldEditionGateTests.cs` and any tests
referencing `GenSource` are dropped. The remaining suite builds green
against the stripped source and runs on CI for net8 / net9 / net10.

Benchmarks: a fresh harness ships under `benchmarking/` (library
benches across all three TFMs plus net10 head-to-head comparisons
against Serilog, NLog, MEL, ZLogger, log4net). Results are recorded
in `docs/benchmarks/consolidated-benchmarks.md`.

## What's preserved in `src/Addons/`

The subdirectories below ship in Herald.OSS as-is from the Herald.Core
source. Edition labels (`Community` / `Pro` / `Enterprise`) appear in
each addon's own xmldoc — in Herald.OSS those labels are informational
only, because the gate machinery that would enforce them was stripped
per §1. Every addon listed here is reachable at the source level and
in the published NuGet.

| Subdirectory | What it provides |
|---|---|
| `Archive/` | Sink archive orchestration plus the local-tar provider. Cloud providers (S3, Azure Blob) compile here but require their respective SDK packages to be referenced by the consuming app. |
| `BinarySerialization/` | `MessagePackLogFormatter` — alternative binary formatter for sinks that accept opaque bytes. |
| `Compliance/` | `HmacChainLogger`, redaction-rule parser, `SequenceNumberEnricher`, and the shared compliance context keys. |
| `GameEnrichers/` | Build-info, player, scene, and session enrichers for game runtimes. |
| `GamePerformance/` | `HotPathLogger`, `FlightRecorderLogger`, `CrashSafeRingBuffer`, `BreadcrumbTrail`, `FrameBudgetLogger`, and the hot-path string handler. |
| `Instrumentation/` | `InstrumentAttribute` and the `SpanScope` lifecycle helper. |
| `ManagementApi/` | `HeraldManagementApi`, `LiveLogCapture`, sample data generator. The management surface is in the source even though Herald.Core gates it behind Pro. |
| `MelAdapter/` | `HeraldLoggerProvider` — exposes Herald as a `Microsoft.Extensions.Logging.ILoggerProvider`. |
| `MetricExtraction/` | `AdaptiveSamplingFilter`, `LogDeduplicationProcessor`, `LogMetricExtractor`, and the shared metric context keys. |
| `NetworkTransports/` | `HealthEndpointExporter` (an isolated `HttpListener` loop). Destination sinks (HTTP / TCP / UDP JSON-line) ship as separately-versioned NuGets under the Herald.Sinks monorepo and are not bundled here. |
| `Observability/` | `CardinalityGuardProcessor`, `ErrorBudgetMonitor`, `TraceContextPropagator`. |
| `OtlpSinks/` | Receiver-side decoders for OTLP JSON and protobuf payloads. OTLP destination sinks ship under `Herald.Sinks.Otlp`. |
| `QualityChecks/` | `LogSchemaRegistry`, `SentenceLogDetector`, `StrategyValidator`. |
| `Query/` | Compiled query expressions, parser/tokeniser, file searcher, and the `ExpressionLogFilter` wrapper. |
| `Reduction/` | `WindowedMeanLogger` plus the step handler and rule record that drive it. |
| `Replay/` | `LogReplayReader` for re-streaming captured events. |

The authoritative per-addon detail — threading contract, test coverage,
SDK shape — lives in `src/Addons/README.md` as carried over from
Herald.Core. That catalog still mentions edition gating because it is
the catalog for the upstream source set; in Herald.OSS the gate column
should be read as "design intent in Herald.Core" rather than "runtime
enforcement here."

## Relationship to Herald.Core going forward

Herald.OSS is the canonical Apache 2.0 upstream. Herald.Core layers
paid edition gating on top — see the architecture diagram in
[`README.md`](README.md). The two repos started from the same source
at commit `98d23fd`, but neither is a frozen snapshot of the other:
Herald.OSS evolves on its own track (sink unification, kernel
fast-path improvements, async drain), and Herald.Core absorbs those
upstream changes while adding edition-gated features that don't ship
here.

Sections 1–6 above record the current shape of Herald.OSS relative
to upstream Herald.Core at commit `98d23fd`. The 0.2.0 release was
the aggressive-strip baseline; 0.2.1 restored the consumer-facing
hooks (B-1 through B-6) per the "hooks present even if not used"
philosophy, 0.2.2 split runtime-notice traffic onto its own
process-wide channel so framework messages stop leaking into
user sinks, and 0.2.3 closed the loop on those restorations with severity
ranking, drop-observation events, a fallback subscriber, and the
sync-disposable disposal-chain fix. The inventory tables in §1 and §2
reflect the current 0.2.3 state — not the 0.2.0 intermediate and not
any earlier 0.2.x point. A separate file-level diff against `98d23fd`
was considered but retired — both repos have moved past the "fork
minus paid bits" frame, and the strip rationale already lives in the
sections above. Adopters consume Herald.OSS as its own library, not
as a delta against Herald.Core.

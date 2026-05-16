# Changelog

All notable changes to Herald.OSS are documented here. The format is
based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and
this project adheres to [Semantic Versioning](https://semver.org/).

## [0.2.1] — 2026-05-15

Audit-followup bug fixes on top of 0.2.0. No public API changes; the two
fixes correct silent-drop paths that affected richer pipelines and
hot-reload scenarios.

### Fixed

- **Kernel path now routes sink failures through the configured failure
  sink.** Previously, a throwing sink on the kernel fast path fell
  through to `System.Diagnostics.Trace.WriteLine` even when the pipeline
  was wired with an `ILogFailureSink` — the chain path (`SafeCompositeLogger`)
  reported failures through the sink, but the kernel did not. Now both
  paths share the same shape: when a failure sink is configured, the
  kernel synthesizes a `LogEvent` from the buffer's level, category,
  template, message, time, and event id, and hands it to
  `ILogFailureSink.ReportFailure(...)`. When no failure sink is wired the
  kernel still falls back to `Trace.WriteLine` with the
  `[Herald.OSS] kernel sink threw` prefix — the previous behaviour for
  vanilla pipelines is unchanged.
- **`WithContext` children now share a kernel holder with the parent.**
  Previously, a child logger built via `StructuredLogger.WithContext(...)`
  captured the parent's `LogKernel` delegate by value at construction.
  A subsequent `SwapKernel` on the parent (hot reload) updated the
  parent's view but left the child dispatching through the orphaned old
  kernel — long-running scope-bearing loggers (per-request ASP.NET
  loggers, typically) kept routing events to retired sinks. The kernel
  now lives behind an internal `KernelHolder` that the parent and every
  child reference together; a swap on the parent is observed by every
  child on the next dispatch.

### Added

- Unit tests `KernelFanOutFailureIsolationTests.Failure_sink_receives_synthesized_event_when_wired`
  and `...Trace_fallback_fires_when_no_failure_sink_is_wired` pinning
  the dual reporting paths.
- Unit test file `WithContextKernelOrphanTests` pinning parent ↔ child
  kernel sharing, swap propagation in both directions, swap-to-null,
  and grandchild holder sharing.

## [0.2.0] — 2026-05-15

Coordinated breaking-changes release. No external adopters yet — the
window for cheap breaking changes is now. The three changes below are
the kind of residue and bug fix that's expensive to land after 1.0.

### Removed (breaking)

- **`HeraldEdition` type and `MinimumEdition` property on
  `ILogSinkProvider`.** Herald.OSS is a single-edition distribution
  with no runtime gate; the type and the property surface were inert
  plumbing. Sink authors that previously declared
  `public HeraldEdition MinimumEdition => HeraldEdition.Community;`
  remove the line. Commercial wrappers that want to keep an edition
  badge can layer it back on as their own type.
- **`HeraldTenant.EnsureAllowedForCurrentEdition` method.** The OSS
  implementation was an empty body; gate enforcement is downstream-
  only. The two call sites in `HeraldRegistryInstance` are removed.
- **`GenSource` field on `LogEvent`, `LogEventBuffer`,
  `LogEventFactory`, `DeferredLogEventFactory`,
  `ILogEventFactory`, and the `_genSource` plumbing through
  `StructuredLogger`, `DefaultLogPipelineFactory`, `HotPathLogger`,
  and `WindowedMeanLogger`.** The provenance gate was already absent
  from the OSS distribution; the field was inert. Downstream
  commercial wrappers that need a provenance carrier can stamp the
  value into `Context["gen_source"]` instead.

### Changed (breaking)

- **`StructuredLogger.IsXxxAcceptable` and
  `HotPathLogger.IsXxxAcceptable` are now properties, not fields.**
  Source-compatible: `if (logger.IsDebugAcceptable) ...` keeps
  binding to the same member name. Binary-breaking for pre-compiled
  consumer assemblies that linked the field by `ldfld`; recompile
  against 0.2.0 to restore. The property getter is a single
  `Volatile.Read` so the emitted reject path stays one load plus
  branch.
- **Level-only hot reload now recomputes the per-known-level accept
  booleans.** A `RecomputeAcceptables` hook on the outer
  `StructuredLogger` is called from the level-only branch of
  `HotReloadableLoggingBootstrap.ExecuteReload`. Without this hook,
  a level-only reload that lowered the minimum left source-gen-
  emitted reject sites reading the stale field value — events at
  the newly-accepted levels were silently dropped.

### Added

- Unit test `IsXxxAcceptableHotReloadTests` pinning the
  IsXxxAcceptable property values at construction and after a
  RecomputeAcceptables call that lowers, raises, or clears the
  minimum.

## [Unreleased]

### Migration

**Default property naming flipped to PascalCase.** Herald 1.0 matches the
.NET ecosystem convention used by Serilog, Microsoft.Extensions.Logging,
and NLog. Calls of the form `logger.Info("user {UserId} signed in", userId)`
now emit the property as `UserId` instead of `userId`.

**This will break adapter-wrapped sinks that key on 0.x property names.**
If you wrap Serilog, Seq, Splunk, or any downstream system whose dashboards,
SIEM rules, or queries were built against pre-1.0 camelCase output, the
rename will silently change the wire format. The mitigation is one line:

```csharp
builder.WithNamingPolicy(PropertyNamingPolicy.Camel);
```

Pin to `Camel` to preserve 0.x behavior. Or run a coordinated cutover:
update the downstream schema first, then drop the override and adopt the
new default.

`Snake` is also available for OpenTelemetry-aligned consumers. Per-method
override via `[HeraldLog(NamingPolicy = "...")]` ships in 1.0.

### Added

- `KernelBufferAdapter.MaterializeAndRender(in LogEventBuffer)` —
  public helper for sinks implementing `IKernelSink` that need a
  fully-materialised heap `LogEvent` with rendered Message at the
  boundary. The four built-in addon sinks
  (`StreamingArchiveLogger`, `CrashSafeRingBuffer`, `LiveLogCapture`,
  `DirectTransformerLogger`) now implement `IKernelSink` via this
  helper. Third-party sink authors porting from `ILogger.Log(LogEvent)`
  can do the same with a one-line method body.
- `QuickLogResult.KernelDiagnostic` reports kernel eligibility at
  pipeline construction. The record carries `KernelEligible` and a
  human-readable `RejectionReason` from `KernelEligibility`.
- Public-release scaffolding: `CONTRIBUTING.md`, `CODE_OF_CONDUCT.md`,
  `SECURITY.md`, `CHANGELOG.md`, `.github/workflows/ci.yml` (build +
  test on net8/net9/net10 + NuGet pack smoke), PR and issue templates.

### Changed

- **Sink contract unified.** Every routed sink must now implement
  `IKernelSink` for the kernel fast path. Every built-in Herald.OSS
  sink — console, file, JSON, null, archive, ring-buffer, SSE
  capture, channel — implements both `ILogger` and `IKernelSink`, so
  default pipelines emit at kernel speed automatically. Custom sinks
  that skip `IKernelSink` fall back to the chain path; the
  disqualifying sink is named in `KernelDiagnostic.RejectionReason`.
- `KernelMixedSinkBenchmarks` reflects the strict eligibility check:
  a pipeline with a non-`IKernelSink` bridge runs the chain path at
  812.47 ns / 1,160 B per emit (vs 28.54 ns / 0 B for pure kernel).

### Removed

- `MaterializingKernelSink` and `IStructuredOnlySink` — the auto-wrap
  path introduced earlier in this development cycle is removed. With
  every built-in sink implementing `IKernelSink` directly, there is
  no legacy sink to wrap and no marker interface to opt into.
- `KernelDiagnostic.LegacySinks` and the `LegacySinkInfo` record —
  no longer meaningful when every sink is required to implement
  `IKernelSink`. The diagnostic now reports only `KernelEligible`
  and `RejectionReason`.

## [0.1.0] — 2026-05-14

Initial open-source bootstrap. Forked from Herald.Core at commit
`98d23fd` with edition-gating machinery, the provenance gate, and
distribution-hardening tooling removed. See `FORK_SCOPE.md` for the
authoritative list of what was stripped and why.

### Added

- Apache 2.0 licensed structured logging core for .NET 8 / 9 / 10.
- Kernel fast path: stack-allocated `LogEventBuffer` passed by `ref`
  to sinks that implement `IKernelSink`; zero-allocation emit on the
  common path.
- Four accept-path call shapes: typed-args, `params ReadOnlySpan<LogProperty>`,
  the interpolated string handler, and the level-bound interpolated
  variant.
- `LogPropertyCompact` typed-slot representation that avoids boxing
  value-type properties through to the kernel.
- Source generator `[HeraldLog]` for `static partial` log methods.
- Pipeline decorator strategy: swappable, filtering, async, rendering,
  batching, fanOut, flightRecorder, postFiltering, eventProcessing,
  plus a registry for custom decorators.
- Hot-reload via JSON config; atomic pipeline swap with zero event
  loss across the cutover.
- Destructuring policies, multi-tenancy via per-tenant
  `StructuredLogger`, MEL adapter (`HeraldLoggerProvider`),
  flight-recorder ring buffer with trigger-level drain, UTF-8 JSON
  formatter.
- AOT-clean: `IsAotCompatible`, `EnableAotAnalyzer`, and
  `EnableTrimAnalyzer` enabled at the project level.
- Workhorse test suite covering build, kernel fan-out, level
  filtering, multi-tenancy, hot reload, sink isolation, and
  plugin-trust paths (17 tests, all passing on net8 / net9 / net10).
- Benchmark suite under `benchmarking/library/{net8,net9,net10}/`
  (narrow Herald-only across TFMs) and
  `benchmarking/comparisons/net10/` (head-to-head vs Serilog, NLog,
  MEL, ZLogger, log4net).

### Removed (relative to Herald.Core 98d23fd)

- All edition-gating machinery (`HeraldEdition`, `HeraldEditionGate`,
  `src/Licensing/`).
- Provenance-gate sink decorator (`GenSourceGatedSink`,
  `ExternalSourceRegistrar`) and the `GenSource` field plumbing
  through `LogEvent`, `LogEventBuffer`, factories, and bootstrap.
- The `HERALDxxx` analyzer set that warns callers about gated APIs at
  compile time.
- Distribution-hardening tooling (Obfuscar config, promote scripts,
  hardened-output paths).
- `src/Addons/ManagementApi/` — the Management API ships in Herald.Pro.
- `Modules/Core/docs/`, `CLA/`, `manifests/` — documentation seeded
  deliberately rather than inherited.

See `FORK_SCOPE.md` for the authoritative diff.

[Unreleased]: https://github.com/mmpworks/Herald.OSS/compare/v0.2.0...HEAD
[0.2.0]: https://github.com/mmpworks/Herald.OSS/releases/tag/v0.2.0
[0.1.0]: https://github.com/mmpworks/Herald.OSS/releases/tag/v0.1.0

# Changelog

All notable changes to Herald.OSS are documented here. The format is
based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and
this project adheres to [Semantic Versioning](https://semver.org/).

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

[Unreleased]: https://github.com/mmpworks/Herald.OSS/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/mmpworks/Herald.OSS/releases/tag/v0.1.0

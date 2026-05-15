# Changelog

All notable changes to Herald.OSS are documented here. The format is
based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and
this project adheres to [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added

- Auto-wrap for legacy sinks: any routed sink that does not implement
  `IKernelSink` is wrapped in `MaterializingKernelSink` at pipeline
  construction so the kernel fast path activates regardless of sink
  mix. Sinks claiming `IStructuredOnlySink` skip the boundary message
  render; others get the rendered message so behaviour matches the
  chain path. `QuickLogResult.KernelDiagnostic` surfaces eligibility
  state and the list of auto-wrapped sinks for adopters who want to
  upgrade them.
- Public-release scaffolding: `CONTRIBUTING.md`, `CODE_OF_CONDUCT.md`,
  `SECURITY.md`, `CHANGELOG.md`, `.github/workflows/ci.yml` (build +
  test on net8/net9/net10 + NuGet pack smoke), PR and issue templates.

### Changed

- `KernelMixedSinkBenchmarks` mixed pipeline: 676.98 ns / 1,160 B →
  364.30 ns / 760 B per emit (-46% latency, -35% allocation). The
  remaining cost is the honest boundary materialisation for legacy
  sinks that read the rendered message; sinks claiming
  `IStructuredOnlySink` or implementing `IKernelSink` stay faster.

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

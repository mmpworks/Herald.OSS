# Herald.OSS

Open-source structured logging core for .NET. Apache 2.0.

[![CI](https://github.com/mmpworks/Herald.OSS/actions/workflows/ci.yml/badge.svg)](https://github.com/mmpworks/Herald.OSS/actions/workflows/ci.yml)
[![License: Apache 2.0](https://img.shields.io/badge/License-Apache_2.0-blue.svg)](LICENSE)

Herald.OSS is the upstream distribution of the Herald logging kernel.
The kernel passes a stack-allocated `LogEventBuffer` directly to sinks
through one contract — `IKernelSink`. Every built-in sink implements
it; the `HeraldSinkBase` abstract class is the one-line entry point
for custom sinks. The accept path stays zero-allocation across every
call shape — typed-args, `params ReadOnlySpan<LogProperty>`, the
interpolated handler, and the level-bound interpolated variant.

The package multi-targets `net8.0`, `net9.0`, and `net10.0`. net8.0
is the minimum — a net9 or net10 project restores the matching binary
automatically. AOT-clean. Trim-safe.

## Status — v0.10.0

Herald.OSS is the canonical Apache 2.0 upstream that the rest of the
Herald ecosystem absorbs from.

v0.10.0 adds the generic `WithNetworkSink(kind, endpoint)` builder seam
and the multi-filter compose seam on the pipeline. See
[`CHANGELOG.md`](CHANGELOG.md) for the per-version detail.

v0.10.0 carries the multi-policy interceptor introduced in v0.4.0:
property names at every literal-template call site are normalized
through the active naming policy at the consumer's compile time, so
events with the same template produce the same downstream schema
regardless of caller variable names. Consumers committed to the default
Pascal policy can opt into a single-lane interceptor via
`<HeraldNamingPolicyAssertion>Default</HeraldNamingPolicyAssertion>`
for an additional ~4 ns per emit.

Each release lands here first; the commercial Herald.Core
distribution picks up the changes and layers edition-gated extensions
on top. [`FORK_SCOPE.md`](FORK_SCOPE.md) is the authoritative inventory
of what does and does not ship in OSS.

## What ships in Herald.OSS

- `src/` — the kernel, pipeline, formatters, and addons. Multi-tenancy
  (`HeraldTenant`, `HeraldRegistry`) and plugin trust are structural
  OSS features and ship with no gate.
- `native/dotnet/` — the .NET implementation of the kernel, pipeline,
  and bootstrap (includes `StructuredLogger` and the typed-args
  overload set emitted by the generator).
- `generators/` — source-generator project. `[HeraldLog]` for
  `static partial` log methods plus the per-sink `[ModuleInitializer]`
  auto-registration generator. Packed into `analyzers/dotnet/cs/`
  inside the nupkg so downstream sinks pick it up without an extra
  analyzer reference.
- `tests/` — the workhorse test suite, organised across 14
  subdirectories (AOT, Addons, Bootstrap, Configuration, Diagnostics,
  Failures, Generators, Helpers, Otlp, Output, Pipeline, Quick,
  Routing, Templating). 495+ passing on net8 / 496+ on net9 / 496+
  on net10. Multi-TFM clean across all three.
- `benchmarking/library/{net8,net9,net10}/` — narrow Herald-only
  benches across TFMs.
- `benchmarking/comparisons/net10/` — head-to-head benches against
  Serilog, NLog, MEL, ZLogger, and log4net.
- `docs/howtos/` — task-oriented guides (quickstart, sinks, operations).
- `docs/guides/` — architectural and SDK references.
- `docs/benchmarks/` — benchmark methodology, per-bench records, and
  the consolidated rollup.
- `LICENSE` / `NOTICE` — Apache 2.0 license and attribution.

Notable surfaces in the public SDK:

- **Quick builder** — `QuickLogBuilder`, `QuickLogResult`, the
  `HeraldRegistry` static façade, and `HeraldHost` for hosts that
  need per-instance isolation.
- **Pipeline filters** — level filtering plus `WithSampling`,
  `WithThrottling`, and `WithAdaptiveSampling`. The three sampling
  methods compose: each call appends a rule, and the rules build into
  a single `CompositeSamplingFilter` where the first matching rule
  wins. Call one alone and you get the single-rule behavior it always
  had; layer them and you get sampling, throttling, and adaptive
  capture on the same pipeline.
- **Network sinks** — `WithNetworkSink(kind, endpoint)` declares a
  network sink by kind and endpoint without a kind-specific method.
  The `WithHttpJsonSink` / `WithOtlpSink` shortcuts are convenience
  wrappers over this same shape. A host that drives sinks from a
  catalog declares each one through this single seam instead of
  growing a method per destination. Pair it with a registered provider
  for the same kind.
- **Kernel + sinks** — `IKernelSink`, `HeraldSinkBase`,
  `KernelBufferAdapter.MaterializeAndRender`, `LogEventBuffer`,
  `LogPropertyCompact`.
- **Source generation + compile-time interceptor** — `[HeraldLog]`
  for explicit `static partial` log methods, plus an automatic
  interceptor that bakes property names into every literal-template
  `logger.Info(...)` call site at the consumer's compile time. Three
  built-in policies (Pascal / Snake / Camel) all baked per call
  site; the active policy lane is selected at runtime via the public
  `BuiltinPolicy` enum + `StructuredLogger.CurrentPolicyKind`
  property. Asserting consumers opt into a single-lane emit via
  `<HeraldNamingPolicyAssertion>Default</HeraldNamingPolicyAssertion>`
  for additional perf. `[assembly: HeraldBuildAssertion]` is
  auto-emitted into every consumer assembly so a host process can
  observe at runtime which compile-time shape the consumer chose.
- **Hot-reload** — `IConfigReloadSource`, `FileConfigReloadSource`,
  `HotReloadableLoggingBootstrap.ExecuteReload`, and the level-only
  fast path that recomputes the `IsXxxAcceptable` properties.
- **Management API** — `HeraldManagementApi`, `IManagementApiAuthorizer`,
  `AuthorizationDecision`, `OnAuthorizationDenied`,
  `DefaultAuthorizerFactory`, `LicenseStatusProvider`,
  `FileSinkPathResolver`, and the `RejectUnconfinedFileSinkPaths`
  strict-mode guard. Ships in OSS at the source level; the upstream
  Herald.Core gates it behind Pro.
- **Diagnostics channel** — `HeraldRuntimeMessages` /
  `HeraldRuntimeMessagesInstance`, `RuntimeNotice`, `NoticeSeverity`,
  `BoundedNoticeBuffer<T>`, `DiagnosticLogFailureSink`. Framework
  notices stay off user pipelines.
- **OTLP receivers** — JSON and protobuf decoders under
  `Addons/OtlpSinks/`. Destination OTLP sinks ship separately under
  `Herald.Sinks.Otlp`.
- **Flight recorder** — `FlightRecorderLogger` and
  `CrashSafeRingBuffer` for trigger-level drain on crash.
- **MEL adapter** — `HeraldLoggerProvider` exposes Herald as a
  `Microsoft.Extensions.Logging.ILoggerProvider`.
- **Multi-tenant routing** — `HeraldTenant`, `HeraldTenantScope`,
  per-tenant `StructuredLogger`. The `GenSourceGatedSink` provenance
  decorator and `HeraldEdition` informational badge stay visible to
  downstream wrappers; OSS enforces nothing against them.
- **Redaction fast path** — `FastPathRedactor` for fixed-rule
  redaction at the kernel boundary. ~8 ns per event over the
  baseline.
- **Sink isolation** — a throwing sink does not take down siblings;
  failures route through `ILogFailureSink` on both the kernel and
  chain paths.

## Benchmark headlines

4-property accept call, net10. Competitor rows regenerated 2026-05-16
against current package versions.

| Library | Latency | Allocation |
|---|---:|---:|
| Herald.OSS — asserted default | 27 ns | 0 B |
| Herald.OSS — multi-policy | 31 ns | 0 B |
| NLog | 59 ns | 248 B |
| MEL | 160 ns | 0 B |
| log4net | 192 ns | 336 B |
| Serilog | 210 ns | 720 B |
| ZLogger | 290 ns | 81 B |

Herald's two rows show the V1.1 trade. Consumers who commit at build
time to the default Pascal policy via
`<HeraldNamingPolicyAssertion>Default</HeraldNamingPolicyAssertion>`
get a single-lane interceptor with no runtime dispatch. Consumers who
want full Pascal / Snake / Camel coverage with runtime
`WithNamingPolicy(...)` switching get the multi-policy emit at every
call site. Both paths are allocation-free.

Real-sink benches confirm the delta is consumer-observable: file
sink, counter sink, and null sink all land within 0.7 ns of each
other. Herald's built-in sinks are async-buffered, so per-emit cost
is dispatch + buffer-fill regardless of sink shape — the dispatch
saving on the asserted path translates to real consumer throughput.

Full results, methodology, and reproduction commands live under
[`docs/benchmarks/`](docs/benchmarks/). The consolidated rollup is
[`docs/benchmarks/consolidated-benchmarks.md`](docs/benchmarks/consolidated-benchmarks.md).

## Getting started

- New to Herald? Start at
  [`docs/howtos/HOWTO-QUICKSTART.md`](docs/howtos/HOWTO-QUICKSTART.md).
- Need a custom sink? [`docs/howtos/HOWTO-SINKS.md`](docs/howtos/HOWTO-SINKS.md).
- Running in production? [`docs/howtos/HOWTO-OPERATIONS.md`](docs/howtos/HOWTO-OPERATIONS.md).
- Hosting an HTTP collector? [`docs/howtos/HOWTO-SERVER-OSS.md`](docs/howtos/HOWTO-SERVER-OSS.md).
- Want an operator UI? [`docs/howtos/HOWTO-DASHBOARD-OSS.md`](docs/howtos/HOWTO-DASHBOARD-OSS.md).
- Want to see it running with no setup? Install the demo tool:

  ```bash
  dotnet tool install --global Herald.DemoApp
  ```

  Run it and open the browser it points you to. The tool runs a Herald
  pipeline, serves a small UI, and streams live events so you can watch
  the pipeline work.
- Want to see it embedded end-to-end? The
  [Herald.SampleApps.HttpApi sample](https://github.com/mmpworks/Herald/tree/main/Modules/Server/samples/Herald.SampleApps.HttpApi)
  drops Herald.OSS into an ASP.NET Core HTTP API and lights up
  live-log capture via SSE.

Guides (conceptual + SDK):

- [`docs/guides/architecture.md`](docs/guides/architecture.md) — the
  three-layer picture.
- [`docs/guides/building-sinks.md`](docs/guides/building-sinks.md) —
  how sinks plug in and what it costs at runtime.
- [`docs/guides/kernel-sink-pattern.md`](docs/guides/kernel-sink-pattern.md) —
  zero-allocation custom sinks via `IKernelSink`.
- [`docs/guides/aot-and-trimming.md`](docs/guides/aot-and-trimming.md) —
  publishing native AOT against Herald.OSS.
- [`docs/guides/security-overview.md`](docs/guides/security-overview.md) —
  what the pipeline defends and what it does not.

## Install

```bash
dotnet add package Herald.OSS
```

Or pin the version in your project file:

```xml
<PackageReference Include="Herald.OSS" Version="0.10.0" />
```

## Quick example

```csharp
using MMP.Herald.Events;
using MMP.Herald.Quick;

var result = QuickLogBuilder.Create()
    .WithConsoleSink()
    .WithMinimumLevel("info")
    .BuildAndCommit();

result.Logger.Info(LogCategory.App,
    "User {UserId} purchased {Sku} for {Price}", 42, "alpha", 9.99);
```

Compose more than one filter on the same pipeline — level, sampling,
throttling, and adaptive capture all chain off the builder:

```csharp
var result = QuickLogBuilder.Create()
    .WithConsoleSink()
    .WithMinimumLevel("info")
    .WithThrottling(maxPerSecond: 5000)      // cap the firehose
    .WithAdaptiveSampling(                    // but capture everything on an error spike
        normalSampleRate: 10,
        errorThreshold: 20)
    .BuildAndCommit();
```

## Relationship to the Herald Ecosystem

Herald.OSS is the spine — the structured-event engine every other
Herald package attaches to. Ingestion shells, analytics overlays,
compliance frameworks, commercial editions, sinks: each one builds
on the same kernel and pipeline. None of them reimplements the data
path.

```
Herald.OSS (Apache 2.0, this repo)  ←  the structured-logging spine
    │
    ├──► Commercial editions (license-gated)
    │       • Herald.Pro         — resilience decorators
    │       • Herald.Enterprise  — WAL + audit chain
    │       • Herald.Compliance  — HIPAA / SOC 2 / EU AI Act overlays
    │
    ├──► Host shells (Apache 2.0)
    │       • Herald.Lean        — headless, config-driven
    │       • Herald.Server      — HTTP collection + query
    │       • Herald.Dashboard   — operator UI
    │       • Herald.ManagementApi
    │
    ├──► Enrichers & addons (Apache 2.0)
    │       • Herald.Sci         — HPC + MPI
    │       • Herald.ML          — batch + epoch + GPU
    │       • Herald.Embed       — one-line drop-in (+ Game, Godot, MEL)
    │
    └──► MMP.Herald.Sinks.* (separate repo, 80+ destinations)
```

Sink packages ship under the `MMP.Herald.Sinks.*` namespace — `MMP` is
the MMPWorks vendor prefix. Reference the one you need
(`MMP.Herald.Sinks.Seq`, `MMP.Herald.Sinks.Datadog`,
`MMP.Herald.Sinks.Otlp`, and the rest) and call its
`RegisterAll` on the sink-provider registry. The
`<Vendor>.Herald.Sinks.*` shape is open for third-party sinks too.

Feature work that doesn't depend on edition machinery lands in
Herald.OSS first; the commercial layer absorbs it. Edition-gated work
lands directly in the commercial layer. The OSS repo and the
commercial repos move forward together — neither is a frozen snapshot
of the other.

## Contributing

Contributions welcome. See [`CONTRIBUTING.md`](CONTRIBUTING.md) for the
process. First-time contributors will be asked to sign the
[CLA](https://github.com/mmpworks/cla-signatures) — the same CLA covers
every Herald repository.

Security vulnerabilities: see [`SECURITY.md`](SECURITY.md). Do not file
public issues.

## License

Apache 2.0. See [`LICENSE`](LICENSE) and [`NOTICE`](NOTICE).

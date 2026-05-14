# Quickstart — Herald.OSS

A short, practical guide to wiring Herald.OSS into a .NET project. The
goal is to get one event flowing through a real pipeline in the fewest
lines possible, then point at where to look next.

For deeper topics:

- Custom sinks, structural multi-tenancy: [`HOWTO-SINKS.md`](HOWTO-SINKS.md)
- Hot reload, async, JSON config, troubleshooting: [`HOWTO-OPERATIONS.md`](HOWTO-OPERATIONS.md)
- Architecture overview: [`../guides/architecture.md`](../guides/architecture.md)

## Install

Herald.OSS targets `net8.0`, `net9.0`, and `net10.0`. Add the package:

```bash
dotnet add package MMP.Herald.OSS
```

The package brings the kernel, the pipeline, the configuration layer,
and the built-in console sink. Other sinks (files, HTTP endpoints,
OpenTelemetry, etc.) ship as separate `MMP.Herald.Sinks.*` packages and
register themselves into Herald.OSS automatically when you add them.

## Build a basic pipeline

```csharp
using MMP.Herald.Events;
using MMP.Herald.Quick;

var herald = QuickLogBuilder.Create()
    .WithConsoleSink()
    .WithMinimumLevel("info")
    .BuildAndCommit();

herald.Logger.Info(LogCategory.App, "Herald.OSS is up");
```

`BuildAndCommit()` returns a `QuickLogResult`. Use `result.Logger` for
emission. Dispose `result.AsyncResource` (if non-null) before process
exit so any in-flight async batches drain cleanly.

## Configure the minimum level

The minimum level is the cheapest filter in the chain. Events ranked
below the configured floor are rejected before any per-sink dispatch.

```csharp
var herald = QuickLogBuilder.Create()
    .WithConsoleSink()
    .WithMinimumLevel("warn")    // trace + debug + info are dropped
    .BuildAndCommit();
```

Built-in level keys: `trace`, `debug`, `info`, `notice`, `success`,
`warn`, `error`, `critical`, `security`, `metric`.

## Add a custom sink

The simplest "custom sink" surface is a bridge — a plain `ILogger` your
code controls. Useful for tests, custom routing, and quick integrations:

```csharp
var captured = new List<string>();
var bridge = new MyBridgeLogger(captured); // implements ILogger

var herald = QuickLogBuilder.Create()
    .WithBridge(bridge)
    .WithMinimumLevel("trace")
    .BuildAndCommit();
```

For first-class, kind-keyed sinks that show up in configuration JSON,
implement `ILogSinkProvider` and register it via
`WithCustomSinkProvider(...)`. The provider is scoped to the builder
that registered it.

## Multi-tenancy

Multi-tenancy in Herald.OSS is structural: each tenant builds its own
pipeline. Pipelines do not share sinks, so isolation is a property of
how you wire the system — not a runtime check.

```csharp
var tenantA = QuickLogBuilder.Create()
    .WithBridge(tenantASink)
    .BuildAndCommit();

var tenantB = QuickLogBuilder.Create()
    .WithBridge(tenantBSink)
    .BuildAndCommit();

tenantA.Logger.Info(LogCategory.App, "for-A-only");
tenantB.Logger.Info(LogCategory.App, "for-B-only");
```

`tenantASink` only ever sees events from `tenantA.Logger`. Same for B.

## Hot reload

Enable the hot-reload entry point and point the builder at a JSON
config file. The pipeline rebuilds itself when the file changes.

```csharp
var herald = QuickLogBuilder.Create()
    .WithPipelineStrategy(
        Configuration.PipelineStrategy.Create().Swappable().Async().FanOut())
    .BuildAndCommit();

herald.HotReloadBootstrap?.WatchFile("logging.json");
```

Hot reload is opt-in via the `Swappable` strategy entry. Without it, a
config change requires a process restart.

## Where to look next

- [`HOWTO-SINKS.md`](HOWTO-SINKS.md) — custom sink providers, structural
  multi-tenancy, bridge sinks.
- [`HOWTO-OPERATIONS.md`](HOWTO-OPERATIONS.md) — hot reload, async,
  batching, JSON config round-trip, troubleshooting.
- [`../guides/architecture.md`](../guides/architecture.md) — kernel + pipeline
  + sinks at a level above the API surface.
- [`../benchmarks/HOWTO.md`](../benchmarks/HOWTO.md) — running and
  documenting Herald.OSS benchmarks.
- `tests/` — the included tests cover the canonical patterns; reading
  them is the fastest way to learn the API.
- `FORK_SCOPE.md` — explicit list of what's stripped from Herald.Core.
  If you reach for a Pro/Enterprise feature and miss it, this file
  tells you why.
- `src/Quick/QuickLogBuilder.With.cs` — every `With*` extension on the
  builder, in one file.
- `src/Pipeline/Kernel/` — the kernel data structures (`LogEventBuffer`,
  `IKernelSink`, `KernelCompiler`) for callers who want to build their
  own zero-allocation sinks.

## Stability

v0.1.0 is the initial open-source bootstrap. The surface is stable
across patch releases on the v0.1 line. Public-API additions may land
between minor releases until v1.0.0; breaking changes will be called
out in release notes.

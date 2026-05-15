# Quickstart — Herald.OSS

A short, practical guide to wiring Herald.OSS into a .NET project. The
goal is one event flowing through a real pipeline in the fewest lines
possible, then pointers to where to look next.

For deeper topics:

- Custom sinks, structural multi-tenancy: [`HOWTO-SINKS.md`](HOWTO-SINKS.md)
- Hot reload, async, JSON config, troubleshooting: [`HOWTO-OPERATIONS.md`](HOWTO-OPERATIONS.md)
- Architecture overview: [`../guides/architecture.md`](../guides/architecture.md)

## Install

Herald.OSS targets `net8.0`, `net9.0`, and `net10.0`. One package is
all you need to wire a working pipeline with console, file, null,
channel, audit, and fast-async sinks:

```bash
dotnet add package Herald.OSS
```

That brings the kernel, the pipeline, the configuration layer, and
every built-in sink listed above. **Network sinks** (HTTP, OTLP,
Datadog, Elasticsearch, Slack, etc.) ship as separately-versioned
`Herald.Sinks.*` packages and register themselves into Herald.OSS
automatically when added — but the quickstart below does not need any
of them.

## First event in 5 lines

```csharp
using MMP.Herald.Events;
using MMP.Herald.Quick;

var result = QuickLogBuilder.Create()
    .WithConsoleSink()
    .WithMinimumLevel("info")
    .BuildAndCommit();

result.Logger.Info(LogCategory.App, "Herald.OSS is up");
```

That writes one line to the console. The namespace is `MMP.Herald.*`
(the package id is `Herald.OSS`; the root namespace stays
`MMP.Herald` for SDK continuity). `LogCategory` is a built-in
classifier — `App`, `System`, `Security`, `Audit`, plus per-tenant
categories you define. Use `App` until your code wants a finer split.

`BuildAndCommit()` returns a `QuickLogResult`. Use `result.Logger` for
emission. Before process exit, dispose `result.AsyncResource` (if it's
non-null) so any in-flight async batches drain cleanly:

```csharp
if (result.AsyncResource is { } resource)
    await resource.DisposeAsync();
```

## What sinks ship with `Herald.OSS`?

Every sink in this table is available the moment you add the
`Herald.OSS` package — no second `dotnet add package` required.

| Builder method | Use case |
|---|---|
| `WithConsoleSink()` | Print events to stdout with the default renderer |
| `WithNullSink()` | Discard every event (benchmarks, muted configs) |
| `WithFileSink(path)` | Rolling-file writer; rotation + retention via overloads |
| `WithChannelSink(...)` | Route a named channel to a custom writer |
| `WithAuditSink(...)` | HMAC-chained audit log for tamper-evident records |
| `WithFastAsyncSink(...)` | Bounded-channel async wrapper for slower downstream sinks |
| `WithBridge(myLogger)` | Adapt any custom `IKernelSink` / `ILogger` you implement |

Network and cloud destinations (HTTP, OTLP, Datadog, Elasticsearch,
Slack webhook, AWS CloudWatch, Azure Application Insights, …) are
covered by the `Herald.Sinks.*` family. Each is a separate NuGet
package. Add the package; the corresponding `WithXxxSink(...)` builder
method becomes usable.

## Configure the minimum level

The minimum level is the cheapest filter in the chain. Events ranked
below the configured floor are rejected before any per-sink dispatch:

```csharp
var result = QuickLogBuilder.Create()
    .WithConsoleSink()
    .WithMinimumLevel("warn")    // trace + debug + info are dropped
    .BuildAndCommit();
```

Built-in level keys: `trace`, `debug`, `info`, `notice`, `success`,
`warn`, `error`, `critical`, `security`, `metric`.

## Multi-tenancy

Multi-tenancy in Herald.OSS is structural: each tenant builds its own
pipeline. Pipelines do not share sinks, so isolation is a property of
how the system is wired — not a runtime check:

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
config file. The pipeline rebuilds itself when the file changes:

```csharp
var result = QuickLogBuilder.Create()
    .WithPipelineStrategy(
        Configuration.PipelineStrategy.Create().Swappable().Async().FanOut())
    .BuildAndCommit();

result.HotReloadBootstrap?.WatchFile("logging.json");
```

Hot reload is opt-in via the `Swappable` strategy entry. Without it, a
config change requires a process restart.

## Where to look next

- [`HOWTO-SINKS.md`](HOWTO-SINKS.md) — custom sink providers,
  structural multi-tenancy, bridge sinks, the `IKernelSink` /
  `HeraldSinkBase` contract.
- [`HOWTO-OPERATIONS.md`](HOWTO-OPERATIONS.md) — hot reload, async,
  batching, JSON config round-trip, troubleshooting.
- [`../guides/architecture.md`](../guides/architecture.md) —
  kernel + pipeline + sinks at a level above the API surface.
- [`../benchmarks/consolidated-benchmarks.md`](../benchmarks/consolidated-benchmarks.md) —
  competitive numbers and methodology.
- `tests/` — the included tests cover the canonical patterns; reading
  them is the fastest way to learn the API.
- `FORK_SCOPE.md` — explicit list of what's stripped from Herald.Core
  upstream. If you reach for a Pro/Enterprise feature and miss it,
  this file tells you why.
- `src/Quick/QuickLogBuilder.With.cs` — every `With*` extension on the
  builder, in one file.
- `src/Pipeline/Kernel/` — the kernel data structures (`LogEventBuffer`,
  `IKernelSink`, `KernelCompiler`) for callers who want to build their
  own zero-allocation sinks.

## Stability

v0.1.0 is the initial open-source release. The surface is stable
across patch releases on the v0.1 line. Public-API additions may land
between minor releases until v1.0.0; breaking changes are called out
in `CHANGELOG.md` and the release notes.

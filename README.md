# Herald.OSS

Open-source structured logging core for .NET. Apache 2.0.

[![CI](https://github.com/mmpworks/Herald.OSS/actions/workflows/ci.yml/badge.svg)](https://github.com/mmpworks/Herald.OSS/actions/workflows/ci.yml)
[![License: Apache 2.0](https://img.shields.io/badge/License-Apache_2.0-blue.svg)](LICENSE)

Herald.OSS is the upstream distribution of the Herald logging kernel.
The kernel passes a stack-allocated `LogEventBuffer` directly to sinks
that implement `IKernelSink`; sinks that don't are auto-wrapped at the
boundary so the fast path still activates. The accept path stays
zero-allocation across every call shape — typed-args,
`params ReadOnlySpan<LogProperty>`, the interpolated handler, and the
level-bound interpolated variant.

Targets .NET 8, .NET 9, and .NET 10. AOT-clean. Trim-safe.

## Status — v0.1.0

This is the first open-source release. The source is forked from
[Herald.Core](https://github.com/mmpworks/Herald.Core) at commit
`98d23fd` with edition-gating machinery removed (no Pro / Enterprise
capability checks, no provenance gate, no distribution-hardening
overlay). Functionality of the kernel + pipeline + sinks is otherwise
unchanged. See [`FORK_SCOPE.md`](FORK_SCOPE.md) for the authoritative
list of what was stripped.

## What's in v0.1.0

- `src/` — the pipeline, kernel, formatters, addons not gated to
  Pro / Enterprise
- `native/dotnet/` — the .NET implementation of the kernel, pipeline,
  and bootstrap
- `generators/` — source-generator project (`[HeraldLog]` etc.)
- `tests/` — workhorse test suite covering build, kernel fan-out,
  level filtering, multi-tenancy, hot reload, sink isolation, and
  plugin-trust paths (17 tests, all passing on net8 / net9 / net10)
- `benchmarking/library/{net8,net9,net10}/` — narrow Herald-only
  benches across TFMs
- `benchmarking/comparisons/net10/` — head-to-head benches against
  Serilog, NLog, MEL, ZLogger, log4net
- `docs/howtos/` — task-oriented guides (quickstart, sinks, operations)
- `docs/guides/` — architectural and SDK references
- `docs/benchmarks/` — published benchmark methodology and results
- `docs/testing/` — test suite scope and conventions
- `LICENSE` — Apache License 2.0
- `NOTICE` — required Apache 2.0 attribution

Benchmark headlines (4-property accept call, net10):

| Library | Latency | Allocation |
|---|---:|---:|
| Herald.OSS | 27 ns | 0 B |
| NLog | 58 ns | 248 B |
| MEL | 151 ns | 208 B |
| log4net | 191 ns | 336 B |
| Serilog | 208 ns | 720 B |
| ZLogger | 299 ns | 71 B |

Full results, methodology, and reproduction commands live under
[`docs/benchmarks/`](docs/benchmarks/). The consolidated rollup is
[`docs/benchmarks/consolidated-benchmarks.md`](docs/benchmarks/consolidated-benchmarks.md).

## Getting started

- New to Herald? Start at
  [`docs/howtos/HOWTO-QUICKSTART.md`](docs/howtos/HOWTO-QUICKSTART.md).
- Need a custom sink? [`docs/howtos/HOWTO-SINKS.md`](docs/howtos/HOWTO-SINKS.md).
- Running in production? [`docs/howtos/HOWTO-OPERATIONS.md`](docs/howtos/HOWTO-OPERATIONS.md).

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

## Relationship to Herald.Core

Herald.OSS is the upstream. Herald.Core is the commercial distribution
layered on top:

```
Herald.OSS (Apache 2.0)
    │
    └─→ Herald.Core (Apache 2.0 + edition-gated extensions)
            │
            ├─→ Herald.Pro packages (extensions gated to Pro)
            └─→ Herald.Enterprise packages (extensions gated to Enterprise)
```

Future feature work that doesn't depend on the gate machinery lands in
Herald.OSS first; Herald.Core absorbs it. Edition-gated work lands
directly in Herald.Core.

## Contributing

Contributions welcome. See [`CONTRIBUTING.md`](CONTRIBUTING.md) for the
process. First-time contributors will be asked to sign the
[CLA](https://github.com/mmpworks/cla-signatures) — the same CLA covers
every Herald repository.

Security vulnerabilities: see [`SECURITY.md`](SECURITY.md). Do not file
public issues.

## License

Apache 2.0. See [`LICENSE`](LICENSE) and [`NOTICE`](NOTICE).

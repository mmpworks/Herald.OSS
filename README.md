# Herald.OSS

Open-source structured logging core for .NET. Apache 2.0.

Herald.OSS is the upstream distribution of the Herald logging kernel.
The kernel passes a stack-allocated `LogEventBuffer` directly to sinks
that implement `IKernelSink`; sinks that don't pay one allocation at
the boundary. The accept path stays zero-allocation across every call
shape — typed-args, `params ReadOnlySpan<LogProperty>`, the interpolated
handler, and level-bound interpolated.

## Status — v0.1.0

This is the initial open-source bootstrap. The source is forked from
[Herald.Core](https://github.com/mmpworks/Herald.Core) with edition-gating
machinery removed (no Pro/Enterprise capability checks, no provenance
gate, no distribution-hardening overlay). Functionality of the kernel
+ pipeline + sinks is otherwise unchanged.

Selected documentation, examples, and benchmark methodology will move
across from Herald.Core over the next milestone. **v1.0.0 lands once
the docs are seeded.** Until then, the canonical reference for any
mechanism in Herald.OSS is the corresponding file in Herald.Core's
`docs/`.

## What's in v0.1.0

- `src/` — the pipeline, kernel, formatters, addons not gated to Pro/Enterprise
- `native/dotnet/` — the .NET implementation of the kernel + pipeline + bootstrap
- `tests/` — workhorse tests covering build, kernel fan-out, level filtering, multi-tenancy, plugin trust
- `benchmarks/` — BenchmarkDotNet harness for kernel fan-out + accept-path
- `docs/` — documentation, organized by purpose:
    - `docs/howtos/` — task-oriented guides (quickstart, sinks, operations)
    - `docs/guides/` — architectural / conceptual reading
    - `docs/benchmarks/` — benchmark methodology and run history
    - `docs/testing/` — test suite scope and conventions
- `LICENSE` — Apache License 2.0
- `NOTICE` — required Apache 2.0 attribution
- `FORK_SCOPE.md` — explicit list of what was stripped from Herald.Core to produce this distribution

Tests and benchmarks are present-but-minimal in v0.1.0 and will fill out
toward 1.0.0.

## Getting started

- New to Herald? Start at [`docs/howtos/HOWTO-QUICKSTART.md`](docs/howtos/HOWTO-QUICKSTART.md).
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

## Relationship to Herald.Core

Herald.OSS is the upstream. Herald.Core is the canonical commercial
distribution layered on top:

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

Open to contributions. The contribution + CLA flow lives in the
Herald.Core repository for both repos:
https://github.com/mmpworks/cla-signatures

## License

Apache 2.0. See `LICENSE` and `NOTICE`.

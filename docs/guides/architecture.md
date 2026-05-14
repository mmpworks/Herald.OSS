# Architecture — Herald.OSS

A conceptual overview of how Herald.OSS is put together. This doc sits
above the API and below the source. It explains *why* the parts are
shaped the way they are; the HOWTOs explain *how* to use them.

## The three layers

Herald.OSS is three concentric layers:

```
┌──────────────────────────────────────────────────────────┐
│  Quick — QuickLogBuilder + QuickLogResult                │  Adopter API
│  ┌────────────────────────────────────────────────────┐  │
│  │  Pipeline — decorators, strategy, fan-out          │  │  Composition layer
│  │  ┌──────────────────────────────────────────────┐  │  │
│  │  │  Kernel — LogEventBuffer + LogKernel + sinks │  │  │  Hot path
│  │  └──────────────────────────────────────────────┘  │  │
│  └────────────────────────────────────────────────────┘  │
└──────────────────────────────────────────────────────────┘
```

Each layer has a single purpose. The boundaries are deliberate — a
caller at the Quick layer never touches the kernel directly, and a
custom sink at the kernel layer never touches the builder.

### Layer 1 — Quick (`src/Quick/`)

`QuickLogBuilder.Create()` is the entry point for almost every adopter.
The builder collects configuration via `With*` extensions, validates
it at `Build()` time, and produces either a JSON config blob or a
running `QuickLogResult`.

This layer exists for one reason: setup ergonomics. Without it, every
caller would need to wire `LoggingBootstrap` directly — which is fine
for tests but tedious for adoption.

### Layer 2 — Pipeline (`src/Pipeline/`)

The pipeline composes decorators in strategy order. Each step is a
single concern: async dispatch, batching, level filtering, rendering,
sink fan-out, hot-reload swapping. Strategies declare which steps
participate and in what order; the composer instantiates them.

This layer exists because real-world logging is a chain of independent
behaviors — async, filtering, rendering, batching — and bundling them
into one monolithic logger would mean every adopter pays for every
behavior whether they use it or not. The decorator chain lets adopters
opt into exactly the decorators they need.

### Layer 3 — Kernel (`src/Pipeline/Kernel/`)

The kernel is the hot path. A `LogEventBuffer` is a stack-allocated
ref struct that carries an event from the caller frame to the sinks
without any heap allocation. `IKernelSink` is the sink-side opt-in:
sinks that implement it consume `LogEventBuffer` directly; sinks that
don't pay one allocation at the boundary.

`KernelCompiler` produces a single delegate per pipeline. The delegate
captures the sinks as locals, hand-unrolls the fan-out for arities
1/2/3, and falls through to a captured-array loop for 4+. The JIT keeps
the whole thing in registers.

This layer exists because logging is on the hot path of every service.
The kernel pays for its existence in the accept-path latency numbers.

## How data flows through

A typical call:

```csharp
result.Logger.Info(LogCategory.App, "User {Id} logged in", userId);
```

1. **Accept.** The structured-logger entry point checks the minimum
   level. Below the floor, return immediately. Above the floor,
   continue.
2. **Construct the buffer.** Template parsing, property binding,
   timestamp capture. The result is a stack-allocated `LogEventBuffer`.
3. **Kernel dispatch.** The kernel delegate runs. For most pipelines
   this fans the buffer out to every sink in the route set.
4. **Sink.** Each sink either consumes the buffer directly
   (`IKernelSink`) or receives a materialized `LogEvent`
   (`ILogger.Log`).

The full chain — decorators, hot reload, filtering, rendering — runs
when the pipeline strategy includes those steps. A minimal pipeline
(no decorators, one sink, no async) takes only steps 1, 2, and 4.

## Hot reload

`SwappableLogger` is a wrapper inside the chain that holds a reference
to the live inner pipeline. When configuration changes:

1. The hot-reload coordinator builds a new inner pipeline from the new
   JSON config.
2. It atomically swaps the new pipeline into the `SwappableLogger`'s
   slot.
3. It swaps the cached kernel delegate on the outer `StructuredLogger`
   so the kernel fast path also points at the new pipeline.
4. In-flight events on the old pipeline complete on the old pipeline.
   New events go to the new one.
5. The old pipeline's resources are scheduled for disposal through the
   janitor, which bounds the dispose with telemetry on stuck shutdowns.

The kernel-delegate swap (step 3) is the load-bearing detail. Without
it, kernel-eligible callers keep dispatching to the orphaned pre-swap
pipeline's kernel — events disappear silently.

## Multi-tenancy

Multi-tenancy in Herald.OSS is structural, not enforced at runtime.
Each tenant calls `QuickLogBuilder.Create()` and builds its own
`QuickLogResult`. The two pipelines have:

- Independent decorator chains.
- Independent sink references (no shared `ILogger` across tenants
  unless that's intentional).
- Independent minimum-level switches.
- Independent hot-reload state.

If two tenants want to share a single batching sink (e.g. for shared
egress infrastructure), they pass the same sink reference into both
builders. That's a design choice — the sink should be tenant-aware in
that case.

The provenance-gate machinery that Herald.Core uses for in-process
injection defense is not in Herald.OSS. Trust at the tenant boundary
is structural — events only reach sinks the tenant's pipeline wired
in.

## Plugin trust

Custom sink providers added via `WithCustomSinkProvider(...)` are
scoped to the builder that registered them. They do not leak into
other builders, and they do not mutate the process-wide
`LogSinkProviderRegistry.Default`.

Resolution order for a sink kind:

1. **Local set** — providers registered on this builder.
2. **Fallback registry** — the process-wide `Default`,
   auto-populated by every `MMP.Herald.Sinks.*` package on assembly
   load.

Local entries take precedence. A builder that registers a custom
`console` provider overrides the built-in for that builder only.

## What's in Herald.OSS vs Herald.Core

Herald.OSS is the Apache-2.0 upstream. Herald.Core layers commercial
extensions on top through plugin packages. The split is:

- **OSS (this repo):** kernel, pipeline, configuration, Quick builder,
  bridge / console / channel / audit sinks, redaction, rendering,
  metrics, hot reload, multi-tenancy structure, plugin trust
  structure.
- **Core extensions (mmpworks/Herald):** edition machinery, the
  provenance gate, Pro/Enterprise plugin packages
  (CircuitBreakerLogger, RetryLogger, DurableBufferLogger,
  FallbackLogger, audit), distribution hardening (Obfuscar overlay),
  the management API.

The contract is: anything in Herald.OSS works without anything from
Core. Adopters who only need OSS pull only OSS.

## Where to look next

- [`../howtos/HOWTO-QUICKSTART.md`](../howtos/HOWTO-QUICKSTART.md) —
  first pipeline.
- [`../howtos/HOWTO-SINKS.md`](../howtos/HOWTO-SINKS.md) — custom
  sinks, structural multi-tenancy.
- [`../howtos/HOWTO-OPERATIONS.md`](../howtos/HOWTO-OPERATIONS.md) —
  hot reload, async, troubleshooting.
- [`../benchmarks/HOWTO.md`](../benchmarks/HOWTO.md) — performance
  methodology.
- [`../testing/HOWTO.md`](../testing/HOWTO.md) — test suite scope.
- `../../FORK_SCOPE.md` — explicit list of what's stripped vs Herald.Core.

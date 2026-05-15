# Operations — Herald.OSS

Running Herald.OSS in production. Hot reload, async + batching,
JSON config round-trip, and the common failure modes worth knowing
before they happen.

## Hot reload

Hot reload lets the operator change pipeline configuration at runtime
without a process restart. The `Swappable` strategy step is the entry
point — without it, every configuration change requires a restart.

```csharp
using MMP.Herald.Configuration;
using MMP.Herald.Quick;

var herald = QuickLogBuilder.Create()
    .WithPipelineStrategy(
        PipelineStrategy.Create().Swappable().Async().FanOut())
    .WithConsoleSink()
    .BuildAndCommit();

// Watch a JSON config file. Changes trigger a rebuild.
herald.HotReloadBootstrap?.WatchFile("logging.json", debounceMs: 500);
```

`HotReloadBootstrap` is null when the pipeline was built without `Swappable`.
Always null-check.

### Fast path vs slow path

The hot-reload coordinator picks between two paths per change:

- **Fast path** — only the minimum level changed. Updates the runtime
  level switch in place. No pipeline rebuild. Sub-millisecond.
- **Slow path** — anything else changed. Rebuilds the full inner
  pipeline and atomically swaps it in. In-flight events on the old
  pipeline complete on the old pipeline; new events go to the new one.

Both paths are coalescing. A burst of file changes during a slow
rebuild queues the latest one and applies it after the in-flight
rebuild completes. The drain is bounded — pathologically rapid edits
do not starve the lock.

### Switching config files at runtime

`SwitchConfigFile(...)` is the explicit form. Use it when the operator
intentionally moves from one config to another (e.g.
`logging-normal.json` → `logging-debug.json` during incident response):

```csharp
herald.HotReloadBootstrap?.SwitchConfigFile("logging-debug.json");
```

The current pipeline stops watching the old file, runs one reload from
the new file, and starts watching the new file going forward.

## Async + batching

The Async decorator offloads delivery from the calling thread. Add it
to the strategy and configure backpressure:

```csharp
var herald = QuickLogBuilder.Create()
    .WithPipelineStrategy(
        PipelineStrategy.Create().Swappable().Async().FanOut())
    .WithConsoleSink()
    .WithAsyncQueue(capacity: 10_000, dropStrategy: "drop_write")
    .BuildAndCommit();
```

Drop strategies:

- `drop_write` — new events are dropped when the queue is full.
  Preserves the older history at the cost of dropping the freshest
  events.
- `drop_oldest` — oldest queued events are evicted to make room.
  Preserves the freshest events at the cost of dropping older ones.
- `block` — calling thread waits for space. Backpressures into the
  caller. Use only when you control every producer and can afford the
  pause.

Pair Async with Batching when downstream sinks benefit from grouped
delivery (network sinks, HTTP endpoints, OTLP collectors):

```csharp
var herald = QuickLogBuilder.Create()
    .WithPipelineStrategy(
        PipelineStrategy.Create().Swappable().Async().Batching().FanOut())
    .WithBatching(maxSize: 100, delayMs: 250)
    .BuildAndCommit();
```

Batches flush on size threshold OR delay threshold, whichever fires
first.

### Drain on shutdown

Before process exit, drain `AsyncResource` so in-flight events reach
their sinks:

```csharp
if (herald.AsyncResource is { } resource)
{
    await resource.DisposeAsync();
}
```

Without the drain, async events queued at the moment of exit can be
lost.

## JSON config round-trip

`Build()` returns a `PipelineBuildResult` whose `ExportConfig()`
produces the complete JSON for the current builder state. `BuildAndCommit()`
goes further and atomically installs the pipeline. Together they give
you a deterministic config round-trip:

```csharp
var builder = QuickLogBuilder.Create()
    .WithConsoleSink()
    .WithMinimumLevel("info");

var json = builder.Build().ExportConfig();
// Persist json to disk, version it, ship it as part of the build.

// Later, a different process loads the same JSON and hot-reloads
// into it:
herald.HotReloadBootstrap?.Reload(json);
```

The JSON is the source of truth: every builder field that affects
runtime behavior is in the JSON, and any runtime field reachable via
`Reload(json)` is reachable via the builder API. If something round-
trips in one direction but not the other, that's a bug — please report
it.

### Configuration files in source

A typical layout ships a default JSON inside the application and
allows operators to override it externally:

```text
appsettings.json                    # baked into the build
logging.json                        # operator override, watched at runtime
logging-debug.json                  # alternative profile
```

The build seeds the pipeline from `appsettings.json` at startup, then
calls `WatchFile("logging.json")` to pick up operator changes without
a restart.

## Troubleshooting

### "No events are reaching my sink"

Walk the pipeline from outside in:

1. **Minimum level** — the cheapest filter. Confirm the event's level
   is at or above the configured floor. `MinimumLevel("trace")` accepts
   everything; `"warn"` drops debug and info.
2. **Route predicate** — `LevelAtOrAbove` and other predicates filter
   per sink. A misconfigured predicate can drop everything silently.
3. **Async queue depth** — if `Async` is in the strategy and the queue
   is full with `drop_write`, new events are dropped. Inspect
   `QuickLogResult.GetRuntimeState().AsyncQueueDepth` and
   `AsyncCapacity`.
4. **Sink run state** — the management API can disable a sink at
   runtime. Confirm the sink is `live`, not `disabled` or `test`.

### "Hot reload silently drops events"

Almost always one of:

- The reload built a pipeline whose minimum level is higher than the
  caller's events. Check the new config's level.
- The reload's strategy lacks a sink for the route the caller emits to.
  Confirm the route → sink mapping survived the reload.
- The hot-reload bootstrap was constructed without the original
  `StructuredLogger`. Without that reference, the kernel fast path keeps
  dispatching to the orphaned pre-reload pipeline. The default
  `QuickLogBuilder.Build()` path wires this correctly; custom bootstrap
  callers must thread the `StructuredLogger` into the
  `HotReloadableLoggingBootstrap` constructor.

### "Events arrive at the wrong tenant's sink"

Multi-tenancy in Herald.OSS is structural, not enforced at runtime.
Verify the wiring:

- Each tenant has its own `QuickLogResult` from its own
  `QuickLogBuilder`.
- Each pipeline references its own sink references (no shared
  `ILogger` instance across tenants unless that's intentional).
- Bridge sinks should be tenant-scoped — sharing a bridge across
  tenants intentionally is fine, accidentally is a bug.

If two tenants share infrastructure (e.g. a common batching sink),
that's not a tenancy violation — it's a design choice. But the
combined sink should be aware of the shared role.

### "Build() throws KeyNotFoundException for a sink kind"

The sink kind has no registered provider. Common causes:

- The `Herald.Sinks.*` package for that kind is not referenced.
- A custom provider was added via `WithCustomSinkProvider` on a
  different builder than the one calling `Build()`.
- A typo in the kind name. Kind keys are case-insensitive but otherwise
  literal — `text-file` does not match `text_file`.

## What this doc does not cover

- Benchmark methodology: see [`../benchmarks/HOWTO.md`](../benchmarks/HOWTO.md).
- API reference: see XML doc on the public types in `src/`.
- Pipeline architecture overview: see
  [`../guides/architecture.md`](../guides/architecture.md).

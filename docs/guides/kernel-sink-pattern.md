# Kernel sinks — the zero-allocation path

A guide for writers of custom sinks who care about per-event cost.
Herald.OSS gives every sink two ways to receive events. This doc
explains both, when each one matters, and how to write the fast one
without breaking anything.

This is an advanced guide. If you just want a sink that works, the
plain `ILogger` route in [`building-sinks.md`](building-sinks.md) is
all you need. Come back here when you've measured per-event cost and
the sink boundary is on the list.

## The two ways a sink can receive an event

Every sink in Herald.OSS implements `ILogger`. That is the floor — a
one-method interface that takes a `LogEvent` (a regular reference
type) and writes it somewhere.

Some sinks also implement `IKernelSink`. That is the opt-in — a
one-method interface that takes a `LogEventBuffer` (a stack-allocated
value type) and writes it somewhere.

```
                ┌─────────────────────────────┐
   Kernel       │   sink implements           │
   path  ─────▶ │   IKernelSink?              │
                └────────────┬────────────────┘
                             │
                  ┌──────────┴──────────┐
                  │                     │
                yes                    no
                  │                     │
                  ▼                     ▼
        Log(in LogEventBuffer)   materialize LogEvent
        — no allocation          — one allocation here
        — buffer lives on        — sink receives the
          the caller's stack       heap object via Log()
```

When every sink in a route set implements `IKernelSink`, the pipeline
keeps the event on the stack from the call site all the way to the
writer. When even one sink lacks the interface, the kernel materializes
a `LogEvent` once at the boundary and that sink uses it. The other
sinks still get the buffer.

The choice belongs to the sink author. Sinks that don't implement
`IKernelSink` still work. They just pay one allocation per event.

## When `IKernelSink` is worth it

The interface costs you nothing if you don't implement it. So the
question is: when should you?

Implement `IKernelSink` when **all three** of these hold:

1. **Your sink's `Log` work is short.** Writing to a `Channel<T>`,
   appending to a memory-mapped file, copying bytes to a per-thread
   buffer. If your sink writes JSON to disk through a `StreamWriter`,
   the per-event work is dominated by the I/O, not the allocation —
   `IKernelSink` is a smaller win there.
2. **You're on a hot path.** A game render loop, a high-rate request
   handler, a tight metrics ingestion loop. At low rates the per-event
   allocation is below the cost of everything else the process is
   doing.
3. **You can avoid retaining the buffer.** The `LogEventBuffer` lives
   on the caller's stack. The moment your `Log(in LogEventBuffer)`
   method returns, the storage is gone. A sink that hands the buffer
   to a background thread, or stashes a pointer to it, will read
   garbage on the second event.

If any one of those is wrong, write a plain `ILogger` sink and stop.
The kernel path is for sinks that earn it.

## The buffer

`LogEventBuffer` is a `readonly ref struct`. That phrase from the C#
language spec means three real things at the API level:

- The buffer lives on the stack. The CLR will not put it on the heap,
  in a field, in an async state machine, or inside a boxed interface.
  The compiler will refuse code that tries.
- The buffer's fields are read-only. You can read `buffer.Level` and
  `buffer.MessageTemplate`. You cannot reassign them.
- The buffer cannot outlive the call. Your `Log` method must read what
  it needs and either copy bytes out (e.g. into your own buffer) or
  finish writing before returning.

Here's what a buffer carries:

```csharp
public readonly ref struct LogEventBuffer
{
    public readonly DateTimeOffset TimeUtc;
    public readonly LogLevel       Level;
    public readonly LogCategory    Category;
    public readonly string         MessageTemplate;
    public readonly string         Message;
    public readonly ReadOnlySpan<LogProperty>        Properties;
    public readonly ReadOnlySpan<LogPropertyCompact> CompactProperties;
    public readonly LogEventId?    EventId;

    public LogEvent ToLogEvent();   // materializes once
}
```

`Properties` and `CompactProperties` are two shapes the caller might
have used. Read whichever is non-empty. (For most callers,
`CompactProperties` is the modern shape — stack-allocated property
slots.)

`ToLogEvent()` is the escape hatch. If you absolutely need a
`LogEvent` (because you're handing the event to existing async
machinery, for example), call it and the buffer becomes a heap event.
That's one allocation — the same allocation a non-kernel sink would
have paid anyway.

## Writing a kernel sink

The smallest meaningful example: a sink that counts events by level.

```csharp
using MMP.Herald;
using MMP.Herald.Events;
using MMP.Herald.Levels;
using MMP.Herald.Pipeline.Kernel;

public sealed class LevelCounterSink : ILogger, IKernelSink
{
    private readonly Dictionary<LogLevel, int> _counts = new();
    private readonly object _gate = new();

    // ILogger floor — receives a heap event.
    public void Log(LogEvent e)
    {
        lock (_gate) { _counts[e.Level] = _counts.GetValueOrDefault(e.Level) + 1; }
    }

    // IKernelSink opt-in — receives a stack buffer, no allocation.
    public void Log(in LogEventBuffer buffer)
    {
        lock (_gate) { _counts[buffer.Level] = _counts.GetValueOrDefault(buffer.Level) + 1; }
    }

    public IReadOnlyDictionary<LogLevel, int> Snapshot()
    {
        lock (_gate) { return new Dictionary<LogLevel, int>(_counts); }
    }
}
```

Both methods exist. The pipeline will call the buffer one when the
event is on the kernel path. If the event was materialized somewhere
upstream (some other sink in the route needed a `LogEvent`), the
pipeline calls the heap-event one instead. Both arrive at the same
counter.

You wire it like any other custom sink:

```csharp
var counter = new LevelCounterSink();
var herald = QuickLogBuilder.Create()
    .WithBridge(counter)
    .WithMinimumLevel("info")
    .BuildAndCommit();
```

## Three rules you must not break

These are the rules the ref-struct constraint encodes. Most code that
violates them will not compile. Two of them are still possible to do
wrong, so they're worth stating directly.

**1. Do not store the buffer past the call.**

```csharp
// Wrong — the compiler will reject this anyway.
private LogEventBuffer _last;

public void Log(in LogEventBuffer buffer)
{
    _last = buffer;   // compile error: ref struct cannot be a field
}
```

Even if you outwit the compiler, the storage behind `buffer.Properties`
is the caller's stack frame. Reading `_last.Properties` on a later
event reads whatever junk the stack now holds.

**2. Do not hand the buffer to async work.**

```csharp
// Wrong, even though it looks reasonable.
public void Log(in LogEventBuffer buffer)
{
    Task.Run(() => Write(buffer));   // compile error: ref struct can't be captured
}
```

If you need async dispatch, copy what you need out of the buffer
*first*, then do the async work. Or call `buffer.ToLogEvent()` and hand
the resulting heap event to the worker — that's exactly what the
shipped async wrapper does.

**3. Do not retain spans past the call.**

```csharp
public void Log(in LogEventBuffer buffer)
{
    _savedProps = buffer.Properties;   // a Span can't be stored either
}
```

Spans, like ref structs, are stack-only by design. If you want to keep
properties around, materialize them: `buffer.Properties.ToArray()`.
That allocates — but you opted in.

## What the kernel path actually skips

The honest answer: one allocation per event per sink that implements
`IKernelSink`. That is the headline difference.

What it does *not* skip:

- Your sink's own work. If your `Log(in LogEventBuffer)` allocates a
  `string` for each event, you've put the allocation back. Profile your
  hot path; the buffer is a delivery vehicle, not a guarantee.
- Pipeline decorators. Filtering, batching, async dispatch — any
  decorator the strategy turned on still runs. Those decorators are
  themselves kernel-aware where it helps; that's a pipeline-level
  property, not a sink property.
- The minimum-level filter. The pipeline rejects below-floor events
  before any sink — kernel or not — sees them. That's the cheapest
  gate in the chain.

The discipline that pays off is *consistency*: build all sinks in a
hot pipeline as kernel sinks. The kernel keeps the event on the stack
end-to-end when every fan-out target opts in. Even one non-kernel sink
in the route set forces materialization for the route, and the kernel
falls back to heap-event dispatch.

## How to verify your sink

Three checks work without owning a profiler:

**Allocation count.** Run a tight loop that emits 100,000 events
through your sink only, with a `MemoryDiagnoser` BenchmarkDotNet
harness. A clean `IKernelSink` adds zero per-event allocations on the
buffer path. If you see any, something inside your `Log` is allocating
— often a `string.Format` or a `LINQ` extension.

**Kernel introspection.** The pipeline exposes
`KernelIntrospection` (in `src/Pipeline/Kernel/`) that reports which
route sets are kernel-eligible. After building, ask it whether your
sink's route runs on the kernel. If it falls back, look for the sink
in the route that doesn't implement `IKernelSink`.

**A real test.** The OSS test suite includes
`Pipeline/Kernel/` tests that exercise kernel dispatch. Mirror those
tests for your sink: log a small batch, snapshot allocations, assert
zero allocations on the buffer path.

## Where to look next

- [`building-sinks.md`](building-sinks.md) — the plain `ILogger` route
  and what assembly loading actually costs.
- [`architecture.md`](architecture.md) — the three-layer picture of
  how the kernel sits inside the pipeline.
- `src/Pipeline/Kernel/IKernelSink.cs` — the interface.
- `src/Pipeline/Kernel/LogEventBuffer.cs` — the buffer fields and XML
  docs.
- `src/Pipeline/Kernel/LevelFilteredKernelSink.cs` — a small example
  of an `IKernelSink` wrapper from the OSS source itself.
- `src/Pipeline/Kernel/MaterializingKernelSink.cs` — the wrapper the
  pipeline inserts when at least one sink in a route needs a heap
  event.

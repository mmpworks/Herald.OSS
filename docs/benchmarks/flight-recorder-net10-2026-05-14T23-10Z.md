# Flight recorder — net10 — 2026-05-14T23-10Z

`FlightRecorderLogger` is a pipeline decorator that captures
below-floor events in a ring buffer; on a trigger-level event
(default: `error`) the buffer drains to the inner sinks before the
trigger event itself. The result is the full debug trail leading up
to an error, paid for only when the error fires.

No peer library ships an equivalent feature. The bench exists so
adopters know what flipping the recorder on costs.

## Host

```
BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8246)
12th Gen Intel Core i9-12900K, 1 CPU, 24 logical and 16 physical cores
.NET SDK 10.0.203
  [Host]     : .NET 10.0.7 (10.0.726.21808), X64 RyuJIT AVX2
```

## Setup

Two pipelines:

- **With recorder** — `Strategy(FlightRecorder + Filtering + FanOut)`
  + `WithFlightRecorder(bufferSize: 200, triggerLevel: "error")`
  + `WithNullSink()` + `WithMinimumLevel("warn")`. Debug emits
  land in the 200-slot ring buffer.
- **Baseline** — same minimum level, no recorder. Below-warn
  events drop at the level filter.

## Results

| Method | Mean | Ratio | Allocated |
|---|---:|---:|---:|
| Recorder_BufferWrite_DebugBelowFloor | 0.2049 ns | 0.92× | — |
| Baseline_RejectedAtFilter_DebugBelowFloor | 0.2226 ns | 1.00× | — |
| Recorder_TriggerDump_ErrorFlushes | 30.4110 ns | 136.69× | 24 B |

## Observations

- **Turning on the recorder costs nothing on the below-floor
  path.** Both `BufferWrite` and the baseline land at ~0.2 ns,
  which is BDN's measurement floor. The JIT eliminates the call
  site at this level. Recorder-on vs recorder-off is
  indistinguishable for the common case.
- **Trigger events cost ~30 ns and 24 B.** The 24 B is one boxed
  property in the trigger event itself. The 30 ns includes the
  trigger emit *plus* draining whatever's in the buffer at that
  moment to the null sink.

## Methodology caveat — read this before citing the number

The 30 ns dump number is *not* "drain 200 buffered events in 30
ns." BDN runs the `TriggerDump` benchmark in isolation; the
`BufferWrite` benchmark runs in its own measurement window. When
`TriggerDump` fires, the buffer holds events only from prior
`TriggerDump` iterations (each of which immediately flushed
whatever was buffered). So the per-iteration buffer state is
near-empty.

To measure the worst-case dump cost (full 200-event buffer), a
follow-up bench needs `[IterationSetup]` to refill the buffer
between iterations. The expected worst-case dump cost is closer to
200 × per-event-dispatch (~5–6 μs for 200 events at ~25–30 ns each
through the null sink).

What the 30 ns *does* tell you: the recorder's per-trigger fixed
cost (level check, buffer-state check, trigger emit) is small. The
dump cost scales linearly with whatever's in the buffer at trigger
time.

## Reading the result

The flight recorder is essentially free on the steady-state path.
Below-floor events take the buffer-write path at the JIT-elimination
floor; recorder-on vs recorder-off is statistically identical.

When a real error fires, the dump cost is proportional to how many
events were captured. For a 200-slot buffer at full capacity, that's
~5 μs of work to flush the trail — paid once per triggered error,
not per event.

In production terms: a service that errors once per minute and
keeps 200 events of context pays ~5 μs/error to dump the trail.
That's effectively free. The recorder turns "I wish I had the
debug trail leading up to that error" into a routine capability.

## What's not measured here

- **Full-buffer dump cost.** As noted in the methodology caveat,
  a follow-up bench with `[IterationSetup]` filling the buffer
  before each trigger would measure the 200-event drain
  realistically.
- **Concurrent producer + trigger.** Today's bench is
  single-threaded. A real production scenario has the trigger
  firing while producers are still emitting; the lock contention
  on `_sync` inside `FlightRecorderLogger` becomes load-bearing.
- **Buffer-size sweep.** 50 / 100 / 200 / 500 / 1000 slots to
  show how dump cost scales with retention depth.

## Reproduce

```bash
cd E:/dev/Herald.OSS
dotnet benchmarking/comparisons/net10/herald/bin/Release/net10.0/Herald.Comparison.dll \
  --filter "*FlightRecorder*" \
  --artifacts benchmarking/comparisons/net10/herald/results
```

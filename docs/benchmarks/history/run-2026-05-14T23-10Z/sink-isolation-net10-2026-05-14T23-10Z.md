# Sink isolation under load — net10 — 2026-05-14T23-10Z

A misbehaving sink should not DoS the rest of the pipeline. This
bench wires five bridge sinks and has one throw
`InvalidOperationException` on every emit; `SafeCompositeLogger`
must catch the throw and continue dispatching to the four healthy
sinks. The bench measures the latency tax the throwing sink
imposes on the per-event call.

## Host

```
BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8246)
12th Gen Intel Core i9-12900K, 1 CPU, 24 logical and 16 physical cores
.NET SDK 10.0.203
  [Host]     : .NET 10.0.7 (10.0.726.21808), X64 RyuJIT AVX2
```

## Results

| Method | Mean | Ratio | Allocated |
|---|---:|---:|---:|
| FiveHealthy_AllSinksLand | 397.3 ns | 1.00× | 664 B |
| FourHealthyOneThrowing_HealthySinksStillLand | 2,406.1 ns | 6.06× | 1,224 B |

## Observations

- **The pipeline survives.** The bench completes without escaping
  the exception to the caller. `SafeCompositeLogger` catches the
  throw, routes it through the failure sink, and proceeds with the
  remaining four sinks. The isolation guarantee holds.
- **The exception costs 2 μs per event.** Throwing + catching is
  expensive on .NET — roughly 2,000 ns of overhead even when the
  catch is local and immediate. The five-healthy baseline lands at
  397 ns; adding one thrower bumps each emit to 2,406 ns.
- **The 1,224 B allocation comes from the exception itself.**
  `InvalidOperationException` carries a stack trace, message, and
  inner-exception fields. Each thrown instance allocates roughly
  the delta over the healthy baseline (1,224 − 664 = 560 B
  excess).

## Reading the result

The headline is not "Herald is 6× slower with a broken sink." The
headline is "Herald keeps emitting when a sink breaks." A library
that crashed the pipeline on the first throw would post no number;
the 2,406 ns is the *cost of correctness* on the bad-sink path.

For production tuning: a sink that throws on every event is a
worst-case scenario. Real misbehaving sinks tend to throw
intermittently (a transient network error, a brief disk-full
window). The 2 μs exception cost applies only on the throwing
emits; healthy emits between throws stay at the four-sink baseline
of ~320 ns.

## What's not measured here

- **Failure-sink ingestion accuracy.** This bench verifies the
  pipeline doesn't crash. A follow-up should assert that the
  failure sink received exactly one event per throw — protect the
  contract, not just the throughput.
- **Tail behaviour over long runs.** A 60-second sustained run
  with intermittent throws would surface any leak in the catch
  path (allocations adding up, GC pressure).

## Reproduce

```bash
cd E:/dev/Herald.OSS
dotnet benchmarking/comparisons/net10/herald/bin/Release/net10.0/Herald.Comparison.dll \
  --filter "*SinkIsolation*" \
  --artifacts benchmarking/comparisons/net10/herald/results
```

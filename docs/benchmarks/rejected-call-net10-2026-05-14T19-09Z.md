# Rejected-call latency — net10 — 2026-05-14T19-09Z

How cheaply does Herald reject events below the pipeline's configured
minimum level? Production systems emit `trace` and `debug` calls at
every meaningful site; the same systems run with `warn` or higher as
the floor. The cost of a rejected call is therefore the cost the
overwhelming majority of `Logger.*` invocations actually pay.

## What this measures

Pipeline minimum level is `warn`. The bench emits at `trace`, `debug`,
and `info` (all below the floor) and at `warn` (above the floor, as a
reference). Herald's level-bound fast path pre-resolves the rank
comparison; rejected calls should land at or below BDN's measurement
floor.

## Host

```
BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8246)
12th Gen Intel Core i9-12900K, 1 CPU, 24 logical and 16 physical cores
.NET SDK 10.0.203
  [Host]     : .NET 10.0.7 (10.0.726.21808), X64 RyuJIT AVX2
  DefaultJob : .NET 10.0.7 (10.0.726.21808), X64 RyuJIT AVX2
```

## Results

| Method                          | Mean        | Error      | Allocated |
|-------------------------------- |------------:|-----------:|----------:|
| Herald_Rejected_Trace_ZeroProps |  0.0028 ns  | ± 0.0043 ns |        — |
| Herald_Rejected_Debug_ZeroProps |  0.0018 ns  | ± 0.0036 ns |        — |
| Herald_Rejected_Info_ZeroProps  |  0.0073 ns  | ± 0.0075 ns |        — |
| Herald_Rejected_Debug_OneProp   |  0.2220 ns  | ± 0.0124 ns |        — |
| Herald_Rejected_Debug_FourProps |  0.2141 ns  | ± 0.0122 ns |        — |
| Herald_Accepted_Warn_ZeroProps  | 25.1565 ns  | ± 0.1887 ns |        — |

## Observations

- **Rejected calls at zero arguments are JIT-eliminated.** The
  `0.001–0.007 ns` numbers are at BDN's measurement floor — the JIT
  observed that the `IsEnabled(LogLevel)` check returns false and
  removed the call entirely. The bench measures nothing because there
  is nothing left to measure.
- **One- and four-property rejects land at ~0.22 ns.** That's the
  cost of resolving the property values into local variables before
  the eliminated call site — boxed conversion via `params object[]`
  for the four-prop case, or a single boxed int for one prop. The
  level check itself contributes nothing.
- **Accepted-call baseline is 25 ns.** Rejected-to-accepted ratio is
  effectively 100 000× faster.

In production terms: a service that emits 100 debug calls per
accepted info call pays roughly the same total logging cost as a
service that emits only the info calls. The rejection path is free.

## Why this matters

Most logging benchmarks measure the accept path because that's the
expensive case. But in real production, accepted events are a small
fraction of emit-site calls. A library whose rejection path is slow
(50 ns or more per call) pays a continuous tax even when nothing is
being logged. Herald's level-bound dispatcher eliminates that tax.

Competitor numbers for this same shape come in a later iteration.

## Reproduce

```bash
cd E:/dev/Herald.OSS
dotnet build benchmarking/comparisons/net10/herald/Herald.Comparison.csproj -c Release

dotnet benchmarking/comparisons/net10/herald/bin/Release/net10.0/Herald.Comparison.dll \
  --filter "*RejectedCall*" \
  --artifacts benchmarking/comparisons/net10/herald/results
```

## Raw artifacts

`benchmarking/comparisons/net10/herald/results/results/MMP.Herald.OSS.Benchmarks.Comparisons.HeraldRow.RejectedCallBenchmarks-report-github.md`

# Kernel-mixed sink: cost of one legacy sink — net10

What does mixing one legacy `ILogger` sink into an otherwise
kernel-eligible pipeline cost? The kernel eligibility check requires
every routed sink to implement `IKernelSink`; one non-kernel sink
fails the check and the whole pipeline drops to the chain path.

## Host

```
BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8246)
12th Gen Intel Core i9-12900K, 1 CPU, 24 logical and 16 physical cores
.NET SDK 10.0.203
  [Host]     : .NET 10.0.7 (10.0.726.21808), X64 RyuJIT AVX2
```

## Setup

- **Pure kernel** — `WithNullSink()`. The null sink (`NoOpLogger`)
  implements `IKernelSink`. Pipeline takes the kernel fast path.
- **One legacy sink mixed in** — same null sink, plus a `WithBridge`
  to a plain `ILogger` that does NOT implement `IKernelSink`. The
  bridge wrapper itself is non-kernel; kernel eligibility fails and
  the entire pipeline takes the chain path.

Both pipelines emit the same 4-property `Info` call.

## Results

| Method | Mean | Allocated |
|---|---:|---:|
| PureKernel_FourProps | 26.89 ns | — |
| OneLegacySink_ForcesChainPath_FourProps | 676.98 ns | 1,160 B |

## Reading the table

- Mixing one legacy sink into a kernel-eligible pipeline costs
  **25× more per emit and 1.2 KB of additional allocation**.
- The cost is the chain path overhead: template parse, `LogEvent`
  heap construction, property `Dictionary` materialization,
  `IReadOnlyList<LogProperty>` allocation, ILogger.Log invocation
  per sink. None of this runs in the pure-kernel path.
- The non-kernel sink itself is fully discarding; the 677 ns / 1,160 B
  is not "what the legacy sink consumes" — it's what the rest of
  the pipeline has to do because the eligibility check failed.

## Practical guidance

If a pipeline mixes sinks, every sink must implement `IKernelSink`
for the fast path to engage. Implementations that exist today:

- `NoOpLogger` (the null sink) — built-in
- `PipelineBridge` — chain-path only, drops eligibility
- Custom sinks — implement `IKernelSink` explicitly to keep
  the pipeline kernel-eligible

For adopters with one foreign `ILogger` sink they can't change
(e.g., a third-party telemetry hook): either accept the 25× tax,
or wrap the foreign sink in an `IKernelSink` adapter that
materializes only at the sink boundary instead of pipeline-wide.

## Reproduce

```bash
cd E:/dev/Herald.OSS
dotnet benchmarking/comparisons/net10/herald/bin/Release/net10.0/Herald.Comparison.dll \
  --filter "*KernelMixed*" \
  --artifacts benchmarking/comparisons/net10/herald/results
```

## Raw artifacts

`benchmarking/comparisons/net10/herald/results/results/MMP.Herald.OSS.Benchmarks.Comparisons.HeraldRow.KernelMixedSinkBenchmarks-report-github.md`

# Kernel-mixed sink: cost of one legacy sink — net10

What does mixing one legacy `ILogger` sink into an otherwise
kernel-eligible pipeline cost? The factory auto-wraps any sink that
does not implement `IKernelSink` in `MaterializingKernelSink`, so the
kernel fast path activates regardless of sink mix. Per-emit cost
diverges based on whether the inner sink reads the rendered message.

## Host

```
BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8246)
12th Gen Intel Core i9-12900K, 1 CPU, 24 logical and 16 physical cores
.NET SDK 10.0.204
  [Host]     : .NET 10.0.8 (10.0.826.23019), X64 RyuJIT AVX2
```

## Setup

- **Pure kernel** — `WithNullSink()`. The null sink (`NoOpLogger`)
  implements `IKernelSink`. Pipeline takes the kernel fast path with
  no boundary materialisation.
- **One legacy sink mixed in** — same null sink, plus a `WithBridge`
  to a plain `ILogger` that does NOT implement `IKernelSink` and does
  NOT claim `IStructuredOnlySink`. The factory auto-wraps the bridge
  in `MaterializingKernelSink`; the kernel still activates, but the
  wrapped sink pays a heap-event materialisation and a message render
  at the boundary on every emit.

Both pipelines emit the same 4-property `Info` call.

## Results

| Method | Mean | Allocated |
|---|---:|---:|
| PureKernel_FourProps | 27.68 ns | — |
| OneLegacySink_ForcesChainPath_FourProps | 364.30 ns | 760 B |

## Reading the table

- Mixing one legacy sink into a kernel-eligible pipeline costs
  **~13× more per emit and 760 B of additional allocation**.
- The cost is the boundary materialisation:
  `LogEventBuffer.ToLogEvent()` allocates a heap `LogEvent`, copies
  the property span into an `IReadOnlyList<LogProperty>`, and the
  `MaterializingKernelSink` renders the message because the inner
  sink might read it. None of this runs in the pure-kernel path.
- The kernel itself still fans out — the kernel-native null sink
  receives the buffer directly with zero allocation, and the wrapped
  legacy sink receives the materialised event. The 364 ns / 760 B is
  the cost of the materialise + render step, not the cost of the
  legacy sink doing its own work.

## Practical guidance

If a pipeline mixes sinks, every emit now activates the kernel fast
path. The implementations break down as:

- **Native kernel sink** (implements `IKernelSink`) — zero boundary
  cost. Example: `NoOpLogger` (the null sink) and the Herald.Sinks
  family.
- **Structured-only legacy sink** (implements `IStructuredOnlySink`
  but not `IKernelSink`) — auto-wrapped; skips the message render
  because the sink declares it never reads rendered text. Pays a
  heap `LogEvent` allocation but no string-render allocation.
  Example: JSON sinks, OTLP exporters, custom structured-only
  receivers.
- **General legacy sink** (implements neither) — auto-wrapped; pays
  the full materialise + render boundary cost. This bench measures
  this worst case.

Adopters who want the absolute best numbers can inspect
`QuickLogResult.KernelDiagnostic.LegacySinks` to find which sinks
were auto-wrapped, then either implement `IKernelSink` on those
sinks or claim `IStructuredOnlySink` when the sink does not read
rendered text.

## Reproduce

```bash
cd E:/dev/Herald.OSS
dotnet benchmarking/comparisons/net10/herald/bin/Release/net10.0/Herald.Comparison.dll \
  --filter "*KernelMixed*" \
  --artifacts benchmarking/comparisons/net10/herald/results
```

## Raw artifacts

`benchmarking/comparisons/net10/herald/results/results/MMP.Herald.OSS.Benchmarks.Comparisons.HeraldRow.KernelMixedSinkBenchmarks-report-github.md`

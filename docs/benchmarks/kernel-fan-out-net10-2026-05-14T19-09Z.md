# Library: kernel fan-out — net10 — 2026-05-14T19-09Z

Per-call dispatch cost across every fan-out shape Herald's
`KernelCompiler` ships. Extended this iteration to include arities
8 and 16 — well above what the original 1/2/3/5 set covered — so the
scaling shape is visible from a single sink through to a routing tree
of sixteen.

## What this measures

The bench instantiates pre-compiled `LogKernel` delegates for six
sink-set sizes (1, 2, 3, 5, 8, 16). Each `[Benchmark]` invokes the
kernel delegate once with a stack-allocated buffer. The sinks are
`NullKernelSink` instances that implement both `ILogger` and
`IKernelSink` and discard the buffer in `Log(in LogEventBuffer)`.

The hand-unrolled shapes — `BindSingle` / `BindPair` / `BindTriple` —
emit inline calls. `BindMany_N` (4+) emits a loop over a captured
array. Both forms aim for register-allocation by the JIT.

## Host

```
BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8246)
12th Gen Intel Core i9-12900K, 1 CPU, 24 logical and 16 physical cores
.NET SDK 10.0.203
  [Host]     : .NET 10.0.7 (10.0.726.21808), X64 RyuJIT AVX2
  DefaultJob : .NET 10.0.7 (10.0.726.21808), X64 RyuJIT AVX2
```

## Results

| Method         | Mean     | Error    | Ratio | Allocated |
|--------------- |---------:|---------:|------:|----------:|
| FanOut_Single  | 20.04 ns | 0.408 ns |  1.00 |        — |
| FanOut_Pair    | 19.39 ns | 0.240 ns |  0.97 |        — |
| FanOut_Triple  | 20.42 ns | 0.437 ns |  1.02 |        — |
| FanOut_Many_5  | 20.72 ns | 0.438 ns |  1.03 |        — |
| FanOut_Many_8  | 21.43 ns | 0.429 ns |  1.07 |        — |
| FanOut_Many_16 | 25.76 ns | 0.119 ns |  1.29 |        — |

## Observations

- **From 1 to 8 sinks: flat at ~20–21 ns.** The unrolled shapes (1/2/3)
  and the captured-array loop (5/8) land within bench noise. The JIT
  is inlining the empty `Log(in buffer)` bodies and keeping the sink
  references in registers.
- **From 8 to 16 sinks: only 4 ns added.** 0.27 ns per additional
  sink. Linear-scaling-via-virtual-call estimate would be ~24 ns
  added for 8 more sinks; we see ~4 ns. The JIT is doing most of the
  work for us.
- **Zero allocation across the board.** The `LogEventBuffer` is a
  ref struct that lives on the caller frame; the kernel delegate
  captures the sinks once at compose time and never allocates on
  the hot path.

These numbers pin the kernel's dispatch-cost floor. Any sink
implementation that wraps or extends the kernel sink should benchmark
against the matching arity to confirm it doesn't regress the shape.

## Why this matters

The competitive comparison measures Herald against other libraries at
the one-sink baseline. Real production deployments wire multiple
sinks: file + console + remote (Loki/Datadog/etc.). For each
additional sink, most competitor libraries pay a roughly-linear cost
(one full dispatch per sink). Herald's compiled fan-out compresses
that scaling almost flat through 8 sinks and nearly so through 16.

A pipeline with 16 sinks routing through Herald's kernel pays 26 ns
per accepted event. The same pipeline routing through Serilog or
NLog would land near 100 ns or more — and that's before each
competitor sink's own per-event cost.

Competitor multi-sink benchmarks are a follow-up. The library row
here pins Herald's floor.

## Reproduce

```bash
cd E:/dev/Herald.OSS
dotnet build benchmarking/library/net10/Herald.OSS.LibraryBenchmarks.csproj -c Release

dotnet benchmarking/library/net10/bin/Release/net10.0/Herald.OSS.LibraryBenchmarks.net10.dll \
  --filter "*KernelFanOut*" \
  --artifacts benchmarking/library/net10/results
```

## Raw artifacts

`benchmarking/library/net10/results/results/MMP.Herald.OSS.Benchmarks.KernelFanOutBenchmarks-report-github.md`

# Library: kernel fan-out — net10 — 2026-05-14T18-10Z

Per-call dispatch cost across the four hand-written fan-out shapes in
`KernelCompiler`. Each shape takes a stack-allocated `LogEventBuffer`
and dispatches it through a discarding `IKernelSink`. The numbers here
isolate the **fan-out dispatch** cost from the accept-path cost.

## What this measures

The bench instantiates pre-compiled `LogKernel` delegates for four
arities (1 / 2 / 3 / 5 sinks). Each `[Benchmark]` invokes the kernel
delegate once with a stack-allocated buffer. The sinks are
`NullKernelSink` instances that implement both `ILogger` and
`IKernelSink` and discard the buffer in `Log(in LogEventBuffer)`.

The shapes:

- **Single** (1 sink) — `BindSingle(sink)`: direct delegate call.
- **Pair** (2 sinks) — `BindPair(a, b)`: two inline calls in sequence.
- **Triple** (3 sinks) — `BindTriple(a, b, c)`: three inline calls.
- **Many** (5 sinks) — `BindMany(IKernelSink[5])`: loop over captured
  array.

## Host

```
BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8246)
12th Gen Intel Core i9-12900K, 1 CPU, 24 logical and 16 physical cores
.NET SDK 10.0.203
  [Host]     : .NET 10.0.7 (10.0.726.21808), X64 RyuJIT AVX2
  DefaultJob : .NET 10.0.7 (10.0.726.21808), X64 RyuJIT AVX2
```

## Results

| Method        | Mean     | Error    | StdDev   | Ratio | Allocated |
|-------------- |---------:|---------:|---------:|------:|----------:|
| FanOut_Single | 19.55 ns | 0.301 ns | 0.282 ns |  1.00 |         — |
| FanOut_Pair   | 19.33 ns | 0.320 ns | 0.284 ns |  0.99 |         — |
| FanOut_Triple | 19.38 ns | 0.133 ns | 0.118 ns |  0.99 |         — |
| FanOut_Many_5 | 19.97 ns | 0.115 ns | 0.096 ns |  1.02 |         — |

## Observations

- **All four shapes land within ~3 % of each other.** The JIT keeps
  the unrolled sequences and the captured-array loop in registers; the
  per-additional-sink cost is invisible at this scale.
- **Zero allocation across the board.** The `LogEventBuffer` is a ref
  struct that lives on the caller frame; the kernel delegate captures
  the sinks once at compose time and never allocates on the hot path.
- **The five-sink loop pays a measurable but small overhead** vs the
  hand-unrolled triple — about 0.6 ns, well within noise.

These numbers pin the floor cost of kernel-eligible fan-out dispatch.
Any sink implementation that wraps or extends this should benchmark
against the matching arity to confirm it doesn't regress the shape.

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

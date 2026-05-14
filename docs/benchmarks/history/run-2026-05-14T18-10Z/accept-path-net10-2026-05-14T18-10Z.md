# Library: accept-path — net10 — 2026-05-14T18-10Z

End-to-end accept-path latency through a QuickLogBuilder pipeline
ending in `WithNullSink()`. Three property shapes: zero, one, three.

## What this measures

The bench builds a real Herald.OSS pipeline:

```csharp
QuickLogBuilder.Create()
    .WithNullSink()
    .WithMinimumLevel("trace")
    .BuildAndCommit();
```

The null sink is `NoOpLogger`, which implements `IKernelSink`. Events
take the kernel fast path: stack-allocated `LogEventBuffer`, no heap
`LogEvent` materialization, no property dictionary.

## Host

```
BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8246)
12th Gen Intel Core i9-12900K, 1 CPU, 24 logical and 16 physical cores
.NET SDK 10.0.203
  [Host]     : .NET 10.0.7 (10.0.726.21808), X64 RyuJIT AVX2
  DefaultJob : .NET 10.0.7 (10.0.726.21808), X64 RyuJIT AVX2
```

## Results

| Method                     | Mean     | Error    | StdDev   | Gen0   | Allocated |
|--------------------------- |---------:|---------:|---------:|-------:|----------:|
| Info_no_properties         | 25.32 ns | 0.332 ns | 0.310 ns |      — |         — |
| Info_with_one_property     | 29.44 ns | 0.216 ns | 0.191 ns | 0.0004 |      24 B |
| Info_with_three_properties | 33.11 ns | 0.621 ns | 0.581 ns | 0.0008 |      48 B |

## Observations

- **Zero properties is allocation-free.** The kernel keeps the buffer
  on the caller frame and the null sink discards in
  `Log(in LogEventBuffer)` without materializing anything.
- **One property adds 24 B.** That's the cost of boxing a single
  `Int32` to carry it through the property buffer.
- **Three properties adds 48 B total.** Three boxes (string is already
  a reference, so no boxing there) plus the property buffer itself.
- **Per-call latency scales linearly with property count** at a rate
  of ~4 ns per additional property. The kernel dispatch baseline is
  ~25 ns; each additional property adds template-token resolution and
  property-buffer construction cost.

These numbers match the Herald row in the competitive comparison
(`comparison-accept-call-net10-2026-05-14T18-10Z.md`) within bench
noise, which is the expected outcome — the comparison bench uses the
same builder shape and the same null sink.

## Earlier iteration note

A first draft of this bench used `.WithBridge(discardingILogger)`,
which forces the chain path (template parse + LogEvent + Dictionary
+ property array materialization on every event). Numbers from that
run: 132 / 473 / 732 ns at 0/1/3 props, with up to 984 B allocated.
The team identified the bench setup as the cause — bridge to a
foreign `ILogger` is not the "fast discard" shape — and the bench
was refit to use `WithNullSink()`. The numbers above reflect the
fixed shape.

## Reproduce

```bash
cd E:/dev/Herald.OSS
dotnet build benchmarking/library/net10/Herald.OSS.LibraryBenchmarks.csproj -c Release

dotnet benchmarking/library/net10/bin/Release/net10.0/Herald.OSS.LibraryBenchmarks.net10.dll \
  --filter "*AcceptPath*" \
  --artifacts benchmarking/library/net10/results
```

## Raw artifacts

`benchmarking/library/net10/results/results/MMP.Herald.OSS.Benchmarks.AcceptPathBenchmarks-report-github.md`

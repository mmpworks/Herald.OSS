# Kernel eligibility tax: cost of a non-IKernelSink sink — net10

What does mixing one `ILogger`-only sink into an otherwise
kernel-eligible pipeline cost? Every built-in Herald.OSS sink
implements `IKernelSink`; a custom sink that skips the interface
disqualifies the kernel for the whole pipeline, and every emit takes
the chain path instead of the kernel fast path.

## Host

```
BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8246)
12th Gen Intel Core i9-12900K, 1 CPU, 24 logical and 16 physical cores
.NET SDK 10.0.204
  [Host]     : .NET 10.0.8 (10.0.826.23019), X64 RyuJIT AVX2
```

## Setup

- **Pure kernel** — `WithNullSink()`. `NoOpLogger` implements
  `IKernelSink`. Pipeline takes the kernel fast path.
- **One non-IKernelSink sink mixed in** — same null sink, plus a
  `WithBridge` to a plain `ILogger` that does NOT implement
  `IKernelSink`. The kernel eligibility check fails on that bridge,
  and the whole pipeline takes the chain path.

Both pipelines emit the same 4-property `Info` call.

## Results

| Method | Mean | Allocated |
|---|---:|---:|
| PureKernel_FourProps | 27.67 ns | — |
| OneLegacySink_ForcesChainPath_FourProps | 691.05 ns | 1,160 B |

## Reading the table

- A pipeline whose sinks all implement `IKernelSink` emits at
  **27.67 ns / 0 B per call** — the kernel passes a stack-allocated
  `LogEventBuffer` directly to every sink, no heap allocation.
- A single sink that skips the interface forces the whole pipeline to
  the chain path at **691.05 ns / 1,160 B per call** — a **~25× tax**.
  The cost is the chain-path overhead: heap `LogEvent` construction,
  `IReadOnlyList<LogProperty>` materialization, `Dictionary` context
  copy, rendered message string. Every event pays this even when only
  one sink in the route set needs the heap shape.

## How to avoid the tax

Implement `IKernelSink` on every custom sink. The interface is one
method:

```csharp
using MMP.Herald.Events;
using MMP.Herald.Pipeline.Kernel;

public sealed class MyCustomSink : ILogger, IKernelSink
{
    public void Log(LogEvent logEvent) { /* heap-event path */ }

    public void Log(in LogEventBuffer buffer)
    {
        // Option A — sink doesn't need rendered Message: read template +
        // properties directly from the buffer. Zero allocation.
        WriteToWire(buffer.MessageTemplate, buffer.CompactProperties);

        // Option B — sink needs rendered Message: materialise at the
        // boundary using the KernelBufferAdapter helper.
        // Log(KernelBufferAdapter.MaterializeAndRender(in buffer));
    }
}
```

Every built-in Herald.OSS sink follows this pattern; that's why
default pipelines emit at kernel speed.

## How to find disqualifying sinks

`QuickLogResult.KernelDiagnostic` reports the eligibility verdict at
build time:

```csharp
var result = builder.BuildAndCommit();
var diag = result.KernelDiagnostic;
if (diag is { KernelEligible: false })
{
    Console.WriteLine($"Kernel disabled: {diag.RejectionReason}");
    // "sink 2 (MyCustomSink) does not implement IKernelSink"
}
```

The rejection reason names the specific rule that failed, so it's
straightforward to find the disqualifying sink.

## Reproduce

```bash
cd E:/dev/Herald.OSS
dotnet benchmarking/comparisons/net10/herald/bin/Release/net10.0/Herald.Comparison.dll \
  --filter "*KernelMixed*" \
  --artifacts benchmarking/comparisons/net10/herald/results
```

## Raw artifacts

`benchmarking/comparisons/net10/herald/results/results/MMP.Herald.OSS.Benchmarks.Comparisons.HeraldRow.KernelMixedSinkBenchmarks-report-github.md`

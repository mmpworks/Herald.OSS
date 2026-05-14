# MEL adapter overhead — net10

Per-call cost of logging through Herald via `ILogger<T>` from
Microsoft.Extensions.Logging, compared against native Herald and a
bare MEL pipeline. Same 4-property `LogInformation` call across all
three rows.

## Host

```
BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8246)
12th Gen Intel Core i9-12900K, 1 CPU, 24 logical and 16 physical cores
.NET SDK 10.0.203
  [Host]     : .NET 10.0.7 (10.0.726.21808), X64 RyuJIT AVX2
```

## Results

| Method | Mean | Gen0 | Allocated |
|---|---:|---:|---:|
| Herald_Native_FourProps | 35.83 ns | 0.0008 | 48 B |
| Herald_Via_Mel_Adapter_FourProps | 125.40 ns | 0.0029 | 168 B |
| Mel_Native_Active_Null_FourProps | 159.69 ns | 0.0036 | 208 B |

## What this measures

- **Herald native** — `StructuredLogger.Info(category, template, args)`.
  Direct call into Herald's typed-args path.
- **Herald via MEL adapter** — `HeraldLoggerProvider` wraps the same
  `StructuredLogger` and exposes `ILogger<T>`. The bench holds the
  `ILogger<T>` and calls `LogInformation`. The adapter takes a fast
  path (no exception, no eventId, state implements
  `IReadOnlyList<KeyValuePair<string,object?>>`): it extracts the
  template from `{OriginalFormat}`, fills a stack-allocated
  `LogPropertyBuffer16` from the remaining entries, and dispatches
  through `LogCompact` so the pipeline takes the kernel fast path.
  No heap dictionary, no `List<LogProperty>`, no `LogProperty[]`.
- **MEL native** — `LoggerFactory.Create` with an active null
  provider (formatter callback runs, output discarded). The
  baseline for "what does `ILogger<T>` cost when Herald isn't
  involved?"

## Reading the table

- Herald via the MEL adapter is faster than bare MEL (125 ns vs
  160 ns) and allocates less (168 B vs 208 B). Adopters who hold
  `ILogger<T>` get Herald's pipeline and a faster path than they'd
  have on MEL alone.
- The 90 ns delta between native Herald (35 ns) and MEL adapter
  (125 ns) is the cost of the MEL contract surface: iterating the
  KvP enumerable, mapping the level, and dispatching through the
  adapter shim.
- The 168 B Herald-via-MEL allocation comes from MEL's own per-emit
  objects (the state struct, boxed value-type args). The adapter
  itself no longer adds a dictionary or property-list array.

## Reproduce

```bash
cd E:/dev/Herald.OSS
dotnet benchmarking/comparisons/net10/herald/bin/Release/net10.0/Herald.Comparison.dll \
  --filter "*MelAdapter*" \
  --artifacts benchmarking/comparisons/net10/herald/results
```

## Raw artifacts

`benchmarking/comparisons/net10/herald/results/results/MMP.Herald.OSS.Benchmarks.Comparisons.HeraldRow.MelAdapterBenchmarks-report-github.md`

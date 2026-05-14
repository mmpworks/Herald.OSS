# MEL-adapter overhead — net10 — 2026-05-14T23-10Z

How much does it cost to log through Herald when the call site holds
`ILogger<T>` (the DI default) instead of Herald's native
`StructuredLogger`? Three rows on the same 4-property Info call.

## Host

```
BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8246)
12th Gen Intel Core i9-12900K, 1 CPU, 24 logical and 16 physical cores
.NET SDK 10.0.203
  [Host]     : .NET 10.0.7 (10.0.726.21808), X64 RyuJIT AVX2
```

## Results

| Method | Mean | Ratio | Gen0 | Allocated |
|---|---:|---:|---:|---:|
| Herald_Native_FourProps | 33.90 ns | 1.00× | 0.0008 | 48 B |
| Herald_Via_Mel_Adapter_FourProps | 293.76 ns | 8.68× | 0.0091 | 528 B |
| Mel_Native_Active_Null_FourProps | 157.10 ns | 4.64× | 0.0036 | 208 B |

## Observations

- **Native Herald lands at 34 ns / 48 B.** Matches the typed-args
  comparison row within noise. The reference.
- **Herald via MEL adapter lands at 294 ns / 528 B.** This is the
  honest cost of the current `HeraldLoggerProvider` implementation:
  the adapter extracts properties from MEL's `IReadOnlyList<KeyValuePair<string,object?>>`
  state, materializes a `Dictionary<string, object?>` per call for
  exception context, and dispatches through `StructuredLogger.Log`
  with a heap-allocated `LogProperty[]`. 8.68× the native cost,
  11× the allocation.
- **MEL native (active null provider) lands at 157 ns / 208 B.**
  For reference: a bare MEL pipeline with a provider that runs the
  formatter callback and discards the output. The shape any
  adopter who chooses MEL without Herald would pay.

## Honest reading

The Herald MEL adapter has optimization room. Today it allocates
a per-call dictionary + heap property array; future work could route
through Herald's typed-args path or use a struct-backed property
collector. Until that lands, adopters who hold `ILogger<T>` pay an
8× tax over native Herald *but still get the rest of Herald's
pipeline* (enrichers, decorators, hot reload, multi-sink fan-out,
flight recorder).

The decision tree for adopters:

1. **You write `logger.Info(LogCategory.App, "...")` directly →
   34 ns.** Use Herald's native API; you get the kernel fast path
   and zero adapter overhead.
2. **You hold `ILogger<T>` in shared libraries and don't want to
   refactor → 294 ns.** Use the MEL adapter; Herald's pipeline
   still runs underneath. The 294 ns is the price of keeping the
   MEL contract at the call site.
3. **You're starting fresh and care about the last 250 ns →**
   use Herald's native API. The MEL contract isn't worth the tax
   when you control the call sites.

## Follow-up

The Herald MEL adapter is a single 158-line file
(`src/Addons/MelAdapter/HeraldLoggerProvider.cs`). The current
implementation prioritizes correctness (full state extraction,
scope support, level mapping) over per-call cost. A follow-up
that swaps the per-call dictionary for a struct-backed
`KeyValuePair<string, object?>` enumerator and dispatches through
Herald's typed-args overloads should close most of the 250 ns gap.

## Reproduce

```bash
cd E:/dev/Herald.OSS
dotnet benchmarking/comparisons/net10/herald/bin/Release/net10.0/Herald.Comparison.dll \
  --filter "*MelAdapter*" \
  --artifacts benchmarking/comparisons/net10/herald/results
```

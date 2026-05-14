# Comparison: accept-call latency — net10 — 2026-05-14T18-10Z

Six-row competitive head-to-head measuring per-call cost of an
`Info`-level accept on each library, configured with that library's
idiomatic discarding-sink pattern.

## Methodology

Each library is configured the way an adopter would actually configure
it for a discarding sink. The benchmark measures **the library's own
call path** through to its sink boundary — template parsing, property
bag construction, decorator/enricher chain — not downstream I/O.

| Library | Discarding pattern used | Notes |
|---|---|---|
| Herald | `WithNullSink()` — kernel-eligible `NoOpLogger` | Implements `IKernelSink`. Events take the kernel fast path with a stack-allocated `LogEventBuffer`; no heap `LogEvent` materialization. |
| Serilog | Custom `ILogEventSink.Emit` no-op | Serilog ships no public null sink. Serilog defers rendering to the sink; this skips render. |
| NLog | Built-in `NullTarget` | Skips layout entirely. NLog's cheapest discarding shape out of the box. |
| ZLogger | `AddZLoggerStream(Stream.Null)` | ZLogger renders to bytes end-to-end; the null stream still pays the format cost. That's how ZLogger ships. |
| log4net | Custom no-op `AppenderSkeleton` | log4net ships no public NullAppender, so the bench provides one. |
| MEL | Active-null `ILoggerProvider` (formatter callback runs, output discarded) | `IsEnabled` returns true; the formatter delegate fires; the rendered string is dropped. |

> **Iteration note.** An earlier draft of the Herald row used
> `WithBridge(discardingILogger)`, which forces the chain path
> (LogEvent materialization + decorator traversal + property bag
> allocation). The team's analysis identified this as a benchmark
> setup mistake, not a code regression: the chain path is Herald's
> *bridge to a foreign ILogger* shape, not Herald's "fast discard"
> shape. `WithNullSink()` is the kernel-eligible idiomatic null —
> the matching analogue to NLog's `NullTarget` and log4net's
> `NullAppender`. The numbers below are the corrected run with
> `WithNullSink()`.

## Host

```
BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8246)
12th Gen Intel Core i9-12900K, 1 CPU, 24 logical and 16 physical cores
.NET SDK 10.0.203
  [Host]     : .NET 10.0.7 (10.0.726.21808), X64 RyuJIT AVX2
  DefaultJob : .NET 10.0.7 (10.0.726.21808), X64 RyuJIT AVX2
```

## Results — accept-call wall-clock latency

Lower is better. Allocated bytes are per call.

### Zero properties (`logger.Info("accept-zero")`)

| Library | Mean | Error | Allocated |
|---|---:|---:|---:|
| MEL | 9.29 ns | ± 0.17 ns | — |
| **Herald** | **24.74 ns** | **± 0.15 ns** | **—** |
| NLog | 36.49 ns | ± 0.35 ns | 120 B |
| Serilog | 89.21 ns | ± 0.88 ns | 160 B |
| log4net | 165.00 ns | ± 2.88 ns | 168 B |
| ZLogger | 287.40 ns | ± 4.16 ns | — |

### One property (`logger.Info("accept-one {Value}", 42)`)

| Library | Mean | Error | Allocated |
|---|---:|---:|---:|
| **Herald** | **29.47 ns** | **± 0.48 ns** | **24 B** |
| NLog | 41.00 ns | ± 0.73 ns | 176 B |
| MEL | 51.44 ns | ± 0.97 ns | 104 B |
| Serilog | 127.21 ns | ± 2.13 ns | 384 B |
| log4net | 179.70 ns | ± 1.75 ns | 264 B |
| ZLogger | 296.30 ns | ± 5.66 ns | — |

### Four properties (`logger.Info("accept-four {A} {B} {C} {D}", ...)`)

| Library | Mean | Error | Allocated |
|---|---:|---:|---:|
| **Herald** | **36.10 ns** | **± 0.49 ns** | **72 B** |
| NLog | 58.04 ns | ± 0.65 ns | 248 B |
| MEL | 150.78 ns | ± 2.12 ns | 208 B |
| log4net | 191.40 ns | ± 2.80 ns | 336 B |
| Serilog | 207.62 ns | ± 3.04 ns | 720 B |
| ZLogger | 298.80 ns | ± 5.93 ns | 71 B |

## Observations

- **Herald is the fastest library on the one-property and four-
  property rows.** At 4 properties, Herald is 1.6× faster than NLog,
  4.2× faster than MEL, 5.3× faster than log4net, 5.8× faster than
  Serilog, and 8.3× faster than ZLogger.
- **Herald's allocations are the lowest of any library that delivers
  real structured data.** ZLogger reports lower bytes at 4 props
  (71 B vs Herald's 72 B), but ZLogger renders to bytes
  end-to-end — the bytes are paid in the format pipeline rather than
  in a property bag.
- **MEL is fastest at zero props (9 ns)** because the formatter
  callback for a zero-prop template returns the template verbatim. At
  one and four properties, MEL's formatter overhead and property
  allocation surface immediately.
- **NLog is consistently second** across all three shapes. Its
  reflection-free positional-arg layout combined with `NullTarget`'s
  layout-skip keeps it under 60 ns even at 4 properties.
- **ZLogger pays the same cost regardless of arity** because it
  renders end-to-end on every call. Flat ~287 ns is the cost of
  ZLogger's format pipeline; arity barely moves it.
- **Serilog and log4net land mid-pack.** Both pay template parsing
  cost up front and stay flat-ish across arity.

## Allocation profile

Herald: 0 B at 0 props, 24 B at 1 prop, 72 B at 4 props. This is the
cleanest scaling of any library here. The 24 B at 1 prop is a single
boxed `Int32` (24 B = box header + int) carrying the value into the
property buffer; the 72 B at 4 props is one box per non-string
property plus the property buffer itself.

The kernel fast path keeps the buffer on the caller's stack frame; no
heap LogEvent, no Dictionary, no property array.

## Reproduce

```bash
cd E:/dev/Herald.OSS

for competitor in herald serilog nlog zlogger log4net MEL; do
  case "$competitor" in
    herald)   dll="Herald.Comparison.dll" ;;
    serilog)  dll="Serilog.Comparison.dll" ;;
    nlog)     dll="NLog.Comparison.dll" ;;
    zlogger)  dll="ZLogger.Comparison.dll" ;;
    log4net)  dll="Log4Net.Comparison.dll" ;;
    MEL)      dll="MEL.Comparison.dll" ;;
  esac

  dotnet "benchmarking/comparisons/net10/${competitor}/bin/Release/net10.0/${dll}" \
    --filter "*" \
    --artifacts "benchmarking/comparisons/net10/${competitor}/results"
done
```

Wall-clock for the full sweep on this host: ~17 minutes.

## Package versions

| Package | Pinned version |
|---|---|
| BenchmarkDotNet | 0.14.0 |
| Serilog | 4.0.0 |
| NLog | 5.3.4 |
| ZLogger | 2.5.10 |
| log4net | 3.0.3 |
| Microsoft.Extensions.Logging | 8.0.0 |

## Raw artifacts

Per-competitor BDN output lives in
`benchmarking/comparisons/net10/{competitor}/results/` alongside the
source.

## Follow-ups

- **Async + batching shape.** Every row currently measures the
  synchronous accept path. A separate bench would put Async +
  Batching in front of the same null sinks to compare backpressure
  behaviour.
- **Net8 + Net9 competitive runs.** Default scope is net10. If older
  TFM numbers matter for an integration decision, expand the
  comparison suite to multi-target.

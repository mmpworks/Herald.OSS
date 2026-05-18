# Phase 2 coverage gaps — V1.1 kernel rerun — net10

Three Phase 2 benches against the V1.1 kernel (release 0.4.0,
`HeraldNamingPolicyAssertion`-aware multi-policy interceptor with
per-call-site single-lane intrinsic): typed-args 8-prop scaling +
value-type shapes, 16-prop redaction with two rules wired, and
real-sink dispatch settlement.

## Host

```
BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8457)
12th Gen Intel Core i9-12900K, 1 CPU, 24 logical and 16 physical cores
.NET SDK 10.0.204
  [Host]     : .NET 10.0.8 (10.0.826.23019), X64 RyuJIT AVX2
  DefaultJob : .NET 10.0.8 (10.0.826.23019), X64 RyuJIT AVX2
```

## TypedArgs — eight-prop scaling and production value-type shapes

| Method | Mean | Allocated |
|---|---:|---:|
| Herald_TypedArgs_FourProps_AllStrings    | 33.03 ns | — |
| Herald_TypedArgs_FourProps_MixedTypes    | 31.46 ns | — |
| Herald_TypedArgs_EightProps_AllStrings   | 39.74 ns | — |
| Herald_TypedArgs_EightProps_MixedTypes   | 39.79 ns | — |
| Herald_TypedArgs_SixteenProps_AllStrings | 79.45 ns | — |
| Herald_TypedArgs_SixteenProps_MixedTypes | 58.79 ns | — |
| Herald_TypedArgs_FourProps_AuditShape    | 47.71 ns | 64 B |
| Herald_TypedArgs_FourProps_FinanceShape  | 53.79 ns | 96 B |

### Reading the table

- **Scaling has three anchors now.** The 4 → 8 → 16 prop curve for
  all-strings reads 33 → 40 → 79 ns. Per-property growth is
  ~0.8-1.5 ns through 8, then steeper from 8 → 16 as more slots in
  the stack-allocated `LogPropertyBuffer16` get touched. Eight
  properties is the realistic shape for RPC traces and web-request
  logs, and the bench now pins it directly instead of leaving
  consumers to interpolate.
- **Mixed-types beats all-strings at sixteen.** 58.79 ns vs
  79.45 ns. The primitive arms of `LogPropertyCompact.From<T>`
  compile to one or two instructions into `ScalarBits`; the string
  arm goes through `Unsafe.As<T, string?>` plus the `object?`
  ref-store. At four and eight properties the two shapes are
  inside noise.
- **Production value types box.** The audit and finance shapes
  carry `Guid`, `DateTimeOffset`, and `decimal` — all value types
  ≥ 8 bytes. Each one boxes at the typed-args dispatcher's
  `object?` boundary. The 64 B audit allocation is two boxes
  (`Guid` + `DateTimeOffset`); 96 B finance is three
  (`Guid` + `decimal` + `DateTimeOffset`). The cost is honest;
  consumers measuring against Herald's headline 4-prop number need
  to know that real Compliance and Finance shapes pay roughly
  47-54 ns and 64-96 B per emit, not the all-strings 31 ns / 0 B
  floor.

## Redaction — sixteen-prop shape with two rules wired

| Method | Mean | Ratio | Allocated |
|---|---:|---:|---:|
| Herald_Baseline_NoRedaction                            |    27.86 ns |   1.00 |     — |
| Herald_WithFastRedaction                               |    75.94 ns |   2.73 |     — |
| Herald_WithCompiledRedaction                           |   893.52 ns |  32.09 | 1368 B |
| Herald_Baseline_NoRedaction_SixteenProps               |    82.53 ns |   2.96 |     — |
| Herald_WithFastRedaction_SixteenProps_TwoRulesFire     |   245.11 ns |   8.80 |    56 B |
| Herald_WithCompiledRedaction_SixteenProps_TwoRulesFire | 3,403.16 ns | 122.21 | 4488 B |

### Reading the table

- **Fast redaction at the realistic compliance shape.** The new
  16-prop / two-rule row lands at 245 ns and 56 B. Against the
  16-prop no-redaction baseline (82.53 ns), the rule pass adds
  ~163 ns and one allocation for two rules fired in a 16-property
  event. That's the number to quote when sizing fast redaction
  for a production compliance pipeline — not the 1-prop / 1-rule
  76 ns / 0 B floor, which is the headline rather than the
  realistic shape.
- **Compiled redaction stays expensive at the realistic shape.**
  3.4 µs and 4488 B for the 16-prop / two-rule shape, versus
  ~894 ns / 1368 B for the 1-prop / 1-rule shape. Compiled
  redaction is the right tool when the rules need real regex
  semantics; it is not the right tool when fast redaction's
  name-pattern matching covers the requirement. The new row makes
  that trade-off visible at the shape consumers actually wire.

## Real sinks — does the dispatch gap survive past NullSink?

| Method | Mean | Ratio | Allocated |
|---|---:|---:|---:|
| Herald_FourProps_NullSink   | 32.02 ns | 1.00 | — |
| Herald_FourProps_MemorySink | 32.62 ns | 1.02 | — |
| Herald_FourProps_FileSink   | 31.55 ns | 0.99 | — |

### Reading the table

- **All three sinks land inside 1.1 ns of each other**, and inside
  one stddev of each other on the noisier MemorySink row
  (stddev 1.6 ns). The NullSink headline (the structural floor)
  predicts the real-sink numbers for both the atomic-counter
  MemorySink and the on-disk FileSink. Per-emit dispatch cost
  through the kernel is what the AcceptCall bench has been
  measuring all along; FileSink's I/O happens in Herald's
  async-buffered ring on a separate thread, so the producer-side
  emit doesn't pay for the disk write.
- **What that means for consumers.** Sink choice doesn't move
  per-emit dispatch cost in steady state. Throughput and tail
  latency under sustained load are sink-shaped (disk bandwidth,
  network, backpressure), but the producer-side AcceptCall number
  is the number you get in a kernel-eligible pipeline regardless
  of what sink the events drain into.

## Reproduce

```bash
cd E:/dev/Herald.OSS
dotnet build benchmarking/comparisons/net10/herald/Herald.Comparison.csproj -c Release
dotnet benchmarking/comparisons/net10/herald/bin/Release/net10.0/Herald.Comparison.dll \
  --filter "*TypedArgs*" "*Redaction*" "*RealSink*" \
  --artifacts docs/benchmarks/runs/run-2026-05-18-phase2-v1.1
```

## Raw artifacts

- `docs/benchmarks/runs/run-2026-05-18-phase2-v1.1/results/MMP.Herald.OSS.Benchmarks.Comparisons.HeraldRow.TypedArgsBenchmarks-report-github.md`
- `docs/benchmarks/runs/run-2026-05-18-phase2-v1.1/results/MMP.Herald.OSS.Benchmarks.Comparisons.HeraldRow.RedactionBenchmarks-report-github.md`
- `docs/benchmarks/runs/run-2026-05-18-phase2-v1.1/results/MMP.Herald.OSS.Benchmarks.Comparisons.HeraldRow.RealSinkBenchmarks-report-github.md`
- `docs/benchmarks/runs/run-2026-05-18-phase2-v1.1/BenchmarkRun-20260518-054628.log`

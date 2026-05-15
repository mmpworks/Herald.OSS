# MEL adapter overhead — net10

Per-call cost of logging through Herald via \`ILogger<T>\` from
Microsoft.Extensions.Logging, compared against native Herald and a
bare MEL pipeline. Same 4-property \`LogInformation\` call across all
three rows.

## Host

\`\`\`
BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8246)
12th Gen Intel Core i9-12900K, 1 CPU, 24 logical and 16 physical cores
.NET SDK 10.0.203
  [Host]     : .NET 10.0.7 (10.0.726.21808), X64 RyuJIT AVX2
\`\`\`

## Results

| Method | Mean | Allocated |
|---|---:|---:|
| Herald_Native_FourProps | 27.53 ns | — |
| Herald_Via_Mel_Adapter_FourProps | 149.15 ns | 168 B |
| Mel_Native_Active_Null_FourProps | 152.41 ns | 208 B |

## What this measures

- **Herald native** — \`StructuredLogger.Info(category, template, args)\`.
  Direct call into Herald's typed-args path. Zero allocation.
- **Herald via MEL adapter** — \`HeraldLoggerProvider\` wraps the same
  \`StructuredLogger\` and exposes \`ILogger<T>\`. The bench holds the
  \`ILogger<T>\` and calls \`LogInformation\`. The adapter takes a fast
  path (no exception, no eventId, state implements
  \`IReadOnlyList<KeyValuePair<string,object?>>\`): it extracts the
  template from \`{OriginalFormat}\`, fills a stack-allocated
  \`LogPropertyBuffer16\` from the remaining entries via the legacy
  2-arg constructor (MEL hands values as \`object?\`, already boxed
  before the adapter sees them), and dispatches through
  \`LogCompact\` so the pipeline takes the kernel fast path.
- **MEL native** — \`LoggerFactory.Create\` with an active null
  provider (formatter callback runs, output discarded).

## Reading the table

- Herald via the MEL adapter is marginally faster than bare MEL
  (149 ns vs 152 ns) and allocates less (168 B vs 208 B).
- The 122 ns delta between native Herald (28 ns) and MEL adapter
  (149 ns) is the cost of the MEL contract surface. Most of it is
  MEL's own per-call object allocation upstream of the adapter:
  the state struct boxing, the \`{OriginalFormat}\` enumerable, the
  delegate-typed formatter. None of these are reachable from
  Herald's side.
- The 168 B Herald-via-MEL allocation comes from those upstream
  MEL objects. The adapter itself adds no heap allocation.

## Reproduce

\`\`\`bash
cd E:/dev/Herald.OSS
dotnet benchmarking/comparisons/net10/herald/bin/Release/net10.0/Herald.Comparison.dll \
  --filter "*MelAdapter*" \
  --artifacts benchmarking/comparisons/net10/herald/results
\`\`\`

## Raw artifacts

\`benchmarking/comparisons/net10/herald/results/results/MMP.Herald.OSS.Benchmarks.Comparisons.HeraldRow.MelAdapterBenchmarks-report-github.md\`

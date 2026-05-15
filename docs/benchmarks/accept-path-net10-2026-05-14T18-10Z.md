# Library: accept-path — net10

End-to-end accept-path latency through a QuickLogBuilder pipeline
ending in \`WithNullSink()\`. Three property shapes: zero, one, three.

## What this measures

The bench builds a real Herald.OSS pipeline:

\`\`\`csharp
QuickLogBuilder.Create()
    .WithNullSink()
    .WithMinimumLevel("trace")
    .BuildAndCommit();
\`\`\`

The null sink is \`NoOpLogger\`, which implements \`IKernelSink\`. Events
take the kernel fast path: stack-allocated \`LogEventBuffer\`, no heap
\`LogEvent\` materialization, no property dictionary.

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
| Info_no_properties | 24.90 ns | — |
| Info_with_one_property | 26.04 ns | — |
| Info_with_three_properties | 26.64 ns | — |

## Reading the table

- All three shapes are zero-allocation. The typed-slot
  \`LogPropertyCompact\` stores primitive values directly in
  \`ScalarBits\` without boxing.
- Per-call cost scales at roughly 1 ns per additional property:
  three props at 27 ns is 2 ns over the no-prop 25 ns baseline.

## Reproduce

\`\`\`bash
cd E:/dev/Herald.OSS
dotnet build benchmarking/library/net10/Herald.OSS.LibraryBenchmarks.csproj -c Release

dotnet benchmarking/library/net10/bin/Release/net10.0/Herald.OSS.LibraryBenchmarks.net10.dll \
  --filter "*AcceptPath*" \
  --artifacts benchmarking/library/net10/results
\`\`\`

## Raw artifacts

\`benchmarking/library/net10/results/results/MMP.Herald.OSS.Benchmarks.AcceptPathBenchmarks-report-github.md\`

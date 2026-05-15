# Comparison: accept-call latency — net10

Six-row competitive head-to-head measuring per-call cost of an
\`Info\`-level accept on each library, configured with that library's
idiomatic discarding-sink pattern.

## Methodology

| Library | Discarding pattern |
|---|---|
| Herald | \`WithNullSink()\` (kernel-eligible \`NoOpLogger\`) |
| Serilog | Custom no-op \`ILogEventSink.Emit\` |
| NLog | Built-in \`NullTarget\` |
| ZLogger | \`AddZLoggerStream(Stream.Null)\` |
| log4net | Custom no-op \`AppenderSkeleton\` |
| MEL | Active-null \`ILoggerProvider\` (formatter callback runs, output discarded) |

## Host

\`\`\`
BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8246)
12th Gen Intel Core i9-12900K, 1 CPU, 24 logical and 16 physical cores
.NET SDK 10.0.203
  [Host]     : .NET 10.0.7 (10.0.726.21808), X64 RyuJIT AVX2
\`\`\`

## Results

Workload: \`logger.Info(template, "alpha", 7, true, 3.14)\` — one
string, three value-type properties.

### Zero properties

| Library | Mean | Allocated |
|---|---:|---:|
| MEL | 9.29 ns | — |
| **Herald** | **25.52 ns** | **—** |
| NLog | 36.49 ns | 120 B |
| Serilog | 89.21 ns | 160 B |
| log4net | 165.00 ns | 168 B |
| ZLogger | 287.40 ns | — |

### One property

| Library | Mean | Allocated |
|---|---:|---:|
| **Herald** | **26.00 ns** | **—** |
| NLog | 41.00 ns | 176 B |
| MEL | 51.44 ns | 104 B |
| Serilog | 127.21 ns | 384 B |
| log4net | 179.70 ns | 264 B |
| ZLogger | 296.30 ns | — |

### Four properties

| Library | Mean | Allocated |
|---|---:|---:|
| **Herald** | **26.64 ns** | **—** |
| NLog | 58.04 ns | 248 B |
| MEL | 150.78 ns | 208 B |
| log4net | 191.40 ns | 336 B |
| Serilog | 207.62 ns | 720 B |
| ZLogger | 298.80 ns | 71 B |

## Reading the table

- Herald is fastest at one and four properties, and second only to
  MEL at zero properties.
- MEL's nine-nanosecond zero-prop number is degenerate: the
  formatter callback returns the template string verbatim with no
  properties to render.
- Herald allocates zero per call across all three arities. The
  typed-slot \`LogPropertyCompact\` stores primitive value types
  directly in \`ScalarBits\` without boxing.
- NLog is the consistent runner-up across all three arities.
- ZLogger pays a flat ~290 ns regardless of arity because it
  renders end-to-end on every call.

## Reproduce

\`\`\`bash
cd E:/dev/Herald.OSS

for competitor in herald serilog nlog zlogger log4net MEL; do
  case "\$competitor" in
    herald)   dll="Herald.Comparison.dll" ;;
    serilog)  dll="Serilog.Comparison.dll" ;;
    nlog)     dll="NLog.Comparison.dll" ;;
    zlogger)  dll="ZLogger.Comparison.dll" ;;
    log4net)  dll="Log4Net.Comparison.dll" ;;
    MEL)      dll="MEL.Comparison.dll" ;;
  esac

  dotnet "benchmarking/comparisons/net10/\${competitor}/bin/Release/net10.0/\${dll}" \
    --filter "*" \
    --artifacts "benchmarking/comparisons/net10/\${competitor}/results"
done
\`\`\`

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

Per-competitor BDN output under
\`benchmarking/comparisons/net10/{competitor}/results/\`.

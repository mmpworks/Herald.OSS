# Destructure-policy shootout vs Serilog — net10

Both libraries support a "transform this type when captured under
`{@Name}`" projection. This bench measures the per-call cost of an
emit that triggers the policy, on the same 5-property POCO workload.

## Host

```
BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8246)
12th Gen Intel Core i9-12900K, 1 CPU, 24 logical and 16 physical cores
.NET SDK 10.0.203
  [Host]     : .NET 10.0.7 (10.0.726.21808), X64 RyuJIT AVX2
```

## Setup

The POCO:

```csharp
record Order(int Id, string Customer, double Total,
             DateTime PlacedUtc, OrderAddress ShipTo);
record OrderAddress(string Street, string City);
```

Both libraries register the same projection — flatten the order
into `{ Id, Customer, Total, City }` — and emit with `{@Order}`.

- **Herald**: `Destructure<Order>(o => new { o.Id, o.Customer, o.Total, City = o.ShipTo.City })`
- **Serilog**: `Destructure.ByTransforming<Order>(...)` with the same projection

Sinks: Herald → `WithNullSink()`. Serilog → custom no-op
`ILogEventSink.Emit`.

## Results

| Method | Mean | Allocated |
|---|---:|---:|
| Herald_DestructureOrder | 27.04 ns | — |
| Serilog_DestructureOrder | 533.14 ns | 1,320 B |

## Reading the table — architectural divergence

The numbers reflect a real difference in destructuring strategy:

- **Herald destructures lazily.** The projection runs only when a
  value of `Order` reaches the `{@Order}` capture mode *during
  rendering*. With a null sink, no rendering happens — the
  projection never fires. The bench therefore measures the cost of
  *a destructure-enabled emit when the sink doesn't ask for the
  rendered form*: 27 ns, zero allocation. The reference flows
  through the kernel typed slot like any other reference type.
- **Serilog destructures eagerly.** `ByTransforming` runs at
  `LogEvent` construction; the transformed value lands in the
  event before any sink sees it. Even with a null sink, the
  destructure work is already paid: 533 ns and 1,320 B per event.

This is a legitimate design choice on each side:

- Serilog's eager path means the destructured shape is captured
  at the emit site (stable regardless of which sink reads it later).
- Herald's lazy path means a pipeline whose sinks don't need the
  destructured value pays nothing.

## When the difference shows up

- **Null / discarding sink (this bench's shape):** Herald skips the
  policy entirely. Serilog runs it. 20× cost gap, all of it
  destructure overhead Serilog can't avoid.
- **Async sinks that materialize later:** Same picture. Herald's
  policy fires when the rendering sink decodes the buffer; Serilog
  paid up front.
- **Synchronous text sink (console / file):** Both libraries pay
  destructuring once per event. Costs converge — the bench shape
  for that scenario is "destructure + render" rather than
  "destructure on a null sink", and the numbers should be closer.

## What this bench does NOT measure

- The destructuring projection itself running. With Herald's null
  sink, the projection delegate is never invoked. A fair
  "projection cost when actually fired" bench would replace the
  null sink with a renderer (e.g., `Utf8JsonFormatter`); that's a
  separate measurement.
- Complex projections that capture nested state. The bench uses a
  flat anonymous shape; deeper projections may shift the picture.

## Reproduce

```bash
cd E:/dev/Herald.OSS
dotnet benchmarking/comparisons/net10/herald/bin/Release/net10.0/Herald.Comparison.dll \
  --filter "*Destructure*" \
  --artifacts benchmarking/comparisons/net10/herald/results
```

## Raw artifacts

`benchmarking/comparisons/net10/herald/results/results/MMP.Herald.OSS.Benchmarks.Comparisons.HeraldRow.DestructureBenchmarks-report-github.md`

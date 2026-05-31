---
gap-id: custom-sink
serilog-surface: ILogEventSink (WriteTo.Sink)
herald-status: carries-over (source-compiled sinks only)
population-rank: high
regression-test-id: G-CORPUS.4
---

<!-- Heather T-H2: STANDALONE companion. Earns its file — must OPEN with the hard
     boundary ("this absorbs source-compiled sinks, NOT pre-compiled community sinks")
     or a reader infers "custom sink works" => "Seq works". -->

# Migrating a Custom Sink

## The boundary, first

This companion covers **source-compiled, user-authored sinks** — a class in your own codebase that implements `ILogEventSink`. It does **not** cover pre-compiled community sinks (Seq, MSSqlServer, Datadog, and the long tail). Those are a hard wall. Source-compiling an adapter does not resolve the assembly-identity problem for pre-compiled packages.

If you are trying to keep Seq or a similar community NuGet sink, stop here and read [third-party-sinks.md](third-party-sinks.md) instead.

## What you have in Serilog

A class in your own codebase:

```csharp
using Serilog.Core;
using Serilog.Events;

public class MySink : ILogEventSink
{
    public void Emit(LogEvent logEvent)
    {
        // your sink logic — write to a database, queue, API, etc.
    }
}
```

Wired in configuration:

```csharp
Log.Logger = new LoggerConfiguration()
    .WriteTo.Sink(new MySink())
    .CreateLogger();
```

## What changes

The source file compiles against `MMP.Herald.Serilog.Core` instead of `Serilog.Core`. The type shapes are identical. One `using` line changes; the class body does not.

## Step-by-step

1. Add a package reference to the Layer-1 assembly (or the Layer-2 shim if you have already cut over):
   ```xml
   <PackageReference Include="MMP.Herald.Serilog" Version="x.y.z" />
   ```

2. Update the `using` in your sink file:
   ```csharp
   // Before
   using Serilog.Core;
   using Serilog.Events;

   // After (Layer 1)
   using MMP.Herald.Serilog.Core;
   using MMP.Herald.Serilog.Events;
   ```
   If you are already using a `global using` alias that maps `Serilog` → `MMP.Herald.Serilog`, no per-file change is needed.

3. Rebuild. The class compiles against the Layer-1 mirror. Herald's adapter hands your sink the mirrored `LogEvent` — same public shape as Serilog's.

4. Wire it the same way:
   ```csharp
   .WriteTo.Sink(new MySink())
   ```
   The `WriteTo.Sink(ILogEventSink)` verb is present in the compat surface.

## Verify

Run your test suite. Confirm:

- Your sink's `Emit` method is called once per log event that passes the minimum-level filter.
- Properties on the `LogEvent` match what you expect — name, value-model type (scalar, structure, sequence, dictionary), and capture mode for `{@}` / `{$}` holes.
- If your sink reads `logEvent.Properties`, confirm structured objects arrive as `StructureValue`, not as a flat string.

A single-event round-trip test is the fastest check:

```csharp
var captured = new List<LogEvent>();
var sink = new CapturingSink(captured);  // a one-liner test-double
Log.Logger = new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger();
Log.Information("Order {@Order}", new { Id = 1, Amount = 99.5m });
Assert.Single(captured);
Assert.IsType<StructureValue>(captured[0].Properties["Order"]);
```

## Deep dive

For the wire path, file layout, and implementation notes see [worked-examples/S1-custom-sink.md](../worked-examples/S1-custom-sink.md).

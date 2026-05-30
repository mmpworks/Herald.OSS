---
gap-id: custom-enricher
serilog-surface: ILogEventEnricher (Enrich.With)
herald-status: carries-over (source-compiled enrichers only)
population-rank: high
regression-test-id: G-CORPUS.4
---

<!-- Heather T-H2: STANDALONE companion. Carries the destructure-routing note
     ({@} props route through the value-model tree, not a flat string). -->

# Migrating a Custom Enricher

## What you have in Serilog

A class in your codebase:

```csharp
using Serilog.Core;
using Serilog.Events;

public class MyEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        var prop = propertyFactory.CreateProperty("TenantId", GetTenantId());
        logEvent.AddPropertyIfAbsent(prop);
    }
}
```

Wired in configuration:

```csharp
new LoggerConfiguration()
    .Enrich.With(new MyEnricher())
    .CreateLogger();
```

## What changes

Same as the custom-sink path — one `using` line changes; the class body does not. The signature of `ILogEventEnricher.Enrich(LogEvent, ILogEventPropertyFactory)` is identical in the compat surface.

## Step-by-step

1. Update the `using` directives in your enricher file:
   ```csharp
   // Before
   using Serilog.Core;
   using Serilog.Events;

   // After (Layer 1)
   using MMP.Herald.Serilog.Core;
   using MMP.Herald.Serilog.Events;
   ```

2. Rebuild. The class compiles against the Layer-1 mirror.

3. Wire it the same way:
   ```csharp
   .Enrich.With(new MyEnricher())
   ```

## Destructuring note

This is the one behavioral difference to check. When your enricher calls `propertyFactory.CreateProperty(name, value, destructureObjects: true)`, the property must route through the value-model **tree** (producing a `StructureValue`), not a flat string representation.

If this routing is wrong, `{@}` properties created by your enricher will arrive at the sink as a `ScalarValue` string instead of a `StructureValue`. The log event will not throw and will not be missing — it will silently carry the wrong shape.

The check is one assertion:

```csharp
var prop = logEvent.Properties["MyComplexObject"];
Assert.IsType<StructureValue>(prop);  // not ScalarValue
```

Run this check as part of your Step 2 parity verification (see [migration-runbook.md](../migration-runbook.md)).

## Verify

After rebuilding:

- Confirm your enricher's properties appear on every event at or above the minimum level.
- If you create properties with `destructureObjects: true`, confirm the value type is `StructureValue`, not `ScalarValue`.
- If your enricher is stateful (reads from `AsyncLocal`, ambient context, etc.), confirm it survives the `Reload(json)` round-trip — a stateful enricher's config cannot be serialized, so it must be re-registered after any pipeline rebuild.

## Deep dive

For the wire path, adapter implementation notes, and the `ILogEventPropertyFactory` shim behavior see [worked-examples/S2-custom-enricher.md](../worked-examples/S2-custom-enricher.md).

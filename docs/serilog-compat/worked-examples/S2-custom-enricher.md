# S2 — Custom Enricher Adapter

## What S2 does

S2 bridges a user-authored Serilog `ILogEventEnricher` into the Herald native enricher pipeline. The bridge has two pieces: an adapter that wraps the enricher as a native `ILogEnricher`, and a factory shim that forwards property construction to the native `LogEventEnrichmentContext`.

## Basic usage

```csharp
using MMP.Herald.Serilog.Configuration;
using MMP.Herald.Serilog.Core;
using MMP.Herald.Serilog.Events;

// 1. Implement ILogEventEnricher
public sealed class TenantEnricher : ILogEventEnricher
{
    private readonly string _tenantId;

    public TenantEnricher(string tenantId) => _tenantId = tenantId;

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        var prop = propertyFactory.CreateProperty("TenantId", _tenantId);
        logEvent.AddOrUpdateProperty(prop);
    }
}

// 2. Register via Enrich.With
var log = new LoggerConfiguration()
    .Enrich.With(new TenantEnricher("acme"))
    .WriteTo.Sink(mySink)
    .CreateLogger();

// 3. Every event carries TenantId
log.Information("user logged in");
// -> Properties["TenantId"] = ScalarValue("acme")
```

## Destructuring support

`ILogEventPropertyFactory.CreateProperty` accepts `destructureObjects: true`, which routes through `LogPropertyCaptureMode.Destructure`. The projector then walks the object reflectively and produces a `StructureValue` instead of a `ScalarValue`.

```csharp
public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
{
    var order = new { Sku = "A1", Qty = 2 };
    // destructureObjects:true → StructureValue on the sink-facing event
    var prop = propertyFactory.CreateProperty("Order", order, destructureObjects: true);
    logEvent.AddOrUpdateProperty(prop);
}
```

The test `Enricher_destructure_true_routes_to_StructureValue_not_scalar` pins this contract.

## How the enrichment-time LogEvent view works

At enrichment time (before the event is finalised), the adapter constructs an `LogEvent` view from the live `LogEventEnrichmentContext`. This view is not backed by a finalised native event — it exposes the current fields (level, template, already-added properties) and accepts mutations.

`AddOrUpdateProperty` on the enrichment-time view does two things:
1. Updates the view's projected dict (for enrichers that read back their own additions).
2. Calls `context.UpsertProperty` so the mutation reaches the native pipeline, even for enrichers that bypass the factory shim.

## Known gap — JSON round-trip

`SerilogEnricherAdapter.ToJsonConfig()` emits a bare token of the form `SerilogEnricherAdapter(UserEnricherTypeName)`. A rebuilt pipeline cannot reconstruct the user enricher from JSON alone because `ILogEventEnricher` has no Herald JSON-factory registration.

This is the same gap that affects any stateful native `ILogEnricher` that omits an override of `ToJsonConfig`. The gap is visible (not silent) and pinned by `CustomEnricherAdapterTests.ToJsonConfig_emits_bare_type_name_known_gap`.

If round-trip fidelity matters for your use case, register a native `ILogEnricher` instead, which has a first-class path in the Herald JSON config system.

## Files

| File | Purpose |
|------|---------|
| `src/Serilog/Core/ILogEventEnricher.cs` | Public interface consumers implement |
| `src/Serilog/Enrichers/LogEventPropertyFactoryShim.cs` | Factory shim — routes destructureObjects to CaptureMode |
| `src/Serilog/Enrichers/SerilogEnricherAdapter.cs` | Wraps user enricher as native ILogEnricher |
| `src/Serilog/Events/LogEvent.cs` | Enrichment-time constructor added (S2 seam) |
| `src/Serilog/Configuration/LoggerEnrichmentConfiguration.cs` | `With(ILogEventEnricher)` wired |
| `tests/Serilog/Seams/CustomEnricherAdapterTests.cs` | Behavioural tests for S2 |

# Rosanne Seam Inventory — Task 8 Value-Model Mirror (forward hooks for P4)

**Summary: Three seams must land in Task 8. Two things the plan's sketch gets wrong that would force a P4 retrofit of the one type Guard 1 forbids P4 from touching.**

Verified against real Serilog 4.3.1. Several provisional verdicts flipped once the actual signatures were in hand.

## Critical: what the plan's Task 8 sketch gets wrong

**The plan's `Properties` is `IReadOnlyDictionary` (frozen, lazy).** Serilog's `ILogEventEnricher.Enrich(LogEvent, ILogEventPropertyFactory)` gives the enricher a **finalized `LogEvent` that it MUTATES** (`AddOrUpdateProperty`, `AddPropertyIfAbsent`, `RemovePropertyIfPresent`). A read-only `Properties` can't receive enricher writes — S2 breaks at P4 without reopening the mirror, which Guard 1 forbids.

**The object→tree walk is a private helper in the plan.** Serilog's `IDestructuringPolicy.TryDestructure(object, ILogEventPropertyValueFactory, out LogEventPropertyValue)` passes a value factory to user policies — they USE it to build sub-values, then return a tree node. If the walk is private, S5 can't pass the factory to user code; S2's `CreateProperty(name, value, destructureObjects:true)` can't produce a `StructureValue`. Both silently flatten to `ScalarValue(ToString())` — PII redaction fails silently.

## Seams to land in Task 8

### Seam A — Value-node constructors public, byte-identical to Serilog
```csharp
public ScalarValue(object? value)
public StructureValue(IEnumerable<LogEventProperty> properties, string? typeTag = null)
public SequenceValue(IEnumerable<LogEventPropertyValue> elements)
public DictionaryValue(IEnumerable<KeyValuePair<ScalarValue, LogEventPropertyValue>> elements)
```
Cost: zero (just the `public` keyword — the types are being built anyway). **Mandatory**: consumer custom enrichers/formatters construct these directly. `InternalsVisibleTo` can't reach them (they're outside all Herald assemblies). Changing a ctor signature later is a breaking change.

### Seam B — Public interfaces for the value factory; internal concrete implementation
Define both interfaces public (consumer code names them in method signatures):
```csharp
public interface ILogEventPropertyValueFactory
{
    LogEventPropertyValue CreatePropertyValue(object? value, bool destructureObjects);
}

public interface ILogEventPropertyFactory
{
    LogEventProperty CreateProperty(string name, object? value, bool destructureObjects = false);
}
```
Expose the concrete `HeraldPropertyValueFactory : ILogEventPropertyValueFactory` as `internal` + `InternalsVisibleTo("MMP.Herald.Serilog.P4assembly")` (reversible — promoting internal→public is non-breaking; the reverse isn't).

The object→tree walk that `{@}` projection already needs (G-VM.1 parity requires it) is exactly `CreatePropertyValue`. S2 and S5 collapse onto this one factory — no duplication on the security-critical redaction path.

**Cost:** extract the walk you're already writing behind these two interface shapes. ~30 lines. If Task 8 buries it private, P4 duplicates the walk OR routes user policies through the string path (silent PII regression).

### Seam C — Mirror's `Properties` supports enricher mutation
Add to `Serilog.Events.LogEvent` (the mirror):
```csharp
public void AddOrUpdateProperty(LogEventProperty property);
public void AddPropertyIfAbsent(LogEventProperty property);
public void RemovePropertyIfPresent(string propertyName);
```
Implementation: promote `_projected` from `IReadOnlyDictionary?` to a `Dictionary<string, LogEventPropertyValue>?` backing store that the getter wraps read-only and the mutators write. The native event stays immutable; only the mirror's *overlay* mutates (Serilog's model).

**Cost:** ~15 lines. The projection-cache invariant (`_projected ??= Project(...)`) becomes "build-once, then mutable" — the lazy build still happens once on first read, subsequent reads return the accumulated dictionary. Guard 2 (native path never constructs the mirror) is unaffected.

**Why expensive later:** retrofitting mutation into a type designed read-only means re-deciding the projection-cache story AND re-running G-VM + Guard 2. Do it while the types are being written.

### Seam E (optional — consolidate in Task 8 to avoid triplication in P4)
Ensure the mirror exposes `Exception`, `MessageTemplate`, `RenderMessage()`, `Level`, `Timestamp` fully — P4's S1/S2/S3 adapters all read these off the event. Cheap now (native passthroughs), tripled cost if each P4 adapter re-derives them.

## Held seams (rejected)

- **Public `Project(LogProperty[])` overload** — no verified P4 call site holds bare properties without an event. YAGNI.
- **Public `LogEventValueProjector.Project`** — internal + InternalsVisibleTo is reversible and Guard-1-consistent. No consumer outside Herald assemblies should call it directly.

## Level-extras mapping (non-throwable in Task 8 — forces P4 decision now)

`logEvent.Level` on an event at `security`/`notice`/`metric`/`success` must return *something*, not throw. Task 8 must implement the mapping (Steve's ratification: `security→Warning`, rest→`Information`). A throwing getter crashes every P4 custom sink on those levels — a latent crash on the public first impression.

## Pre-mortem (what this prevents at six months)

P4 engineer builds S2. `mirrorEvent.AddOrUpdateProperty` has nowhere to land (Seam C not built). `factory.CreateProperty("Order", order, true)` produces a flat `ScalarValue` instead of a `StructureValue` (Seam B not built). Both are not P4 bugs — they're Task 8 shipping the mirror as read-only and string-terminal. Retrofit: re-open the one type Guard 1 forbids P4 from touching. **Land Seams A, B, C in Task 8.**

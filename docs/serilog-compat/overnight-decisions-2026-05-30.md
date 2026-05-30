# Overnight Architectural Decisions — 2026-05-30 (Richard + Jared)

Full authority delegated by Steve. These decisions are binding for P1 Tasks 4–12.

## Critical Task-4 Corrections (generated code must match the REAL engine, not the plan sketches)

These three are COMPILE ERRORS or silent perf regressions if wrong. The spec reviewer must check each.

### C-1: `SerilogLoggerAdapter` must be `partial` (COMPILE ERROR if not)
`SerilogLoggerAdapter.cs:33` is currently `public sealed class`. The generator emits a `partial class` body.
**Fix before Task 4 lands:** flip to `public sealed partial class`.

### C-2: `LogCompact` takes `(LogLevel, LogCategory, string, ReadOnlySpan<LogPropertyCompact>)` — the plan sketch omitted `LogCategory`
Real signature at `StructuredLogger.cs:474`:
```csharp
public void LogCompact(LogLevel level, LogCategory category, string messageTemplate,
                       ReadOnlySpan<LogPropertyCompact> properties)
```
Generated call must pass `LogCategory.None` as the category (same as native generator at `TypedArgsOverloadGenerator.cs:212`). The level must be a `LogLevel` OBJECT (`KnownLogLevels.Information` etc., NOT a string key) — `IsEnabled` uses `ReferenceEquals` fast path on the singleton; a non-singleton level falls to dictionary lookup.

### C-3: Generated guard must be `_herald.IsInformationAcceptable` (qualified, not bare)
The native generator emits `if (!IsInformationAcceptable) return;` because it generates *into* `partial class StructuredLogger` where the property is in scope. The Serilog generator generates *into* `partial class SerilogLoggerAdapter`, which holds `_herald : StructuredLogger` as a field. Must emit `if (!_herald.IsInformationAcceptable) return;`.

## Resolved Decisions

### R-DEFER: WithContext→ForContext rename — DEFERRED, post-P1
`SerilogLoggerAdapter.ForContext(...)` already says `ForContext` at the Serilog-compat boundary (the only boundary that matters for drop-in parity). `ILogger<T>.WithContext` is Herald's OWN native DI-typed-logger surface — a different audience. Renaming it mid-P1 would churn call sites while P1 is actively building. Post-P1: add `ForContext` as a forwarder alongside `WithContext` (additive), mark `WithContext` `[Obsolete]`, delete in a later sweep. Do NOT touch the adapter — it is correct as-is.

### R-GENERATOR: Hole names resolve once to a local, not per-slot
The generated overload resolves hole names to a local array ONCE (one `SerilogTemplateHoleIndex` lookup per call, returning a cached entry struct with `Names[]`, `AllDefaultMode` flag, and `CaptureModes[]`), then indexes into the local. NOT N separate `NameAt(i)` calls. Mirror `TypedArgsOverloadGenerator.cs:196-208` (resolve-to-local pattern).

### R-ALLDEFAULT: Precomputed per-template `AllDefaultMode` flag
The hole-index cache entry carries a precomputed `bool AllDefaultMode` flag (computed once at parse from the hole modes). The generated overload branches on ONE flag read: `if (holeInfo.AllDefaultMode) { compact path } else { full LogProperty[] path }`. NOT a per-call compound `&&` over N `IsDefaultModeAt(i)` calls (which would call into the index N times per dispatch).

### R-TRANSPORT-VS-CAPTUREMODE: Transport is all-or-nothing; capture-mode determination is per-hole
Transport decision (which CODE PATH to take for the whole call):
- If `AllDefaultMode` == true → compact span path (zero alloc for primitives)
- If `AllDefaultMode` == false → full `LogProperty[]` path (whole call goes full)

But within the full `LogProperty[]` path, each hole's `CaptureMode` is set individually: default holes get `CaptureMode.Default`, `{@}` holes get `Destructure`, `{$}` holes get `Stringify`. Task 6's mixed-hole test asserts all three per-property capture modes in ONE event — all consistent with this.

### R-BUFFERSIZE: Already extracted — use it, don't redefine
`GeneratorArityHelpers.BufferSizeFor` already exists. The Serilog generator calls `GeneratorArityHelpers.BufferSizeFor(arity)`, does NOT redefine the switch.

### R-GOLDEN-COUNT: Add a count assertion (96) to the golden test
`text.Should().Contain("OverloadResolutionPriority")` proves presence, not count. Add:
```csharp
System.Text.RegularExpressions.Regex.Matches(text, "OverloadResolutionPriority")
    .Count.Should().Be(96, "6 levels × 16 arities, every overload must have the priority attribute");
```
(Adjust if exception-bearing verb overloads are also generated — confirm the count against the interface.)

### R-HOLE-INDEX-CAP: Frozen-at-cap, NOT LRU
`SerilogTemplateHoleIndex` must mirror `NameResolverCache`'s EXACT cap discipline:
- `ConcurrentDictionary` + hard count check at `CapacityLimit = 8192`
- **Frozen-at-cap**: once `Count >= CapacityLimit`, stop inserting, fire cap-hit notice once, RETURN RESULT UNCACHED (correct behavior, just slower — never fails)
- **`string.IsInterned` guard**: non-interned (runtime-built) templates skip the insert; resolve-and-return without caching (prevents dead-entry march toward cap)
- NOT an LRU (LRU needs per-read write-lock → destroys zero-alloc path)
- NOT unbounded growth

### R-GUARD2-MECHANISM: Allocation probe primary; drop dedicated call-count counter
Guard 1 (architecture test) proves the engine can't reference the mirror at compile time. Guard 2 should be the allocation probe: log to a native sink, assert `BytesPerIteration == 0`. This is black-box and needs no internal test-only state on `LogEventValueProjector`. Drop `ResetCallCount()`/`ProjectionCount` UNLESS Task 8's lazy-fires-once positive test independently needs it. Don't add mutable test-only state to production types just for Guard 2.

### R-CANONICAL-SHAPE: Four-tuple equivalence, NOT render equality
Canonical-shape-equivalent means:
1. Same property name-set (set comparison, not ordered)
2. Same logical value-type per property, recursively (`ScalarValue`, `StructureValue`, `SequenceValue`, `DictionaryValue`)
3. Same scalar values (via `object.Equals`)
4. Same level mapping (via `SerilogLevelMap`)
NOT render-string equality (brittle, culture-dependent). NOT byte/CLEF-JSON equality.

### R-ALLOC-PROBE: Reliable for exact-zero, two operational guards
`AllocationProbe.BytesPerIteration` is sound (per-thread counter, 2000-iteration warmup, 100k measured iterations, floor-divided). Two guards implementers MUST honor:
1. `TemplateFor(arity)` must return an **interned, cached, compile-time string constant** — NOT interpolated/concatenated. Non-interned strings skip the hole-index cache → re-resolve every iteration → non-zero allocs → false failure.
2. The test pipeline must be **kernel-eligible** (no `DynamicLevelPolicy`, no per-event-allocating enrichers). Reuse the same pipeline shape as the native zero-alloc tests.

### R-CLOSEANDFLUSH-GAP: `FromBuild` adapter needs flush-on-dispose
`SerilogLoggerAdapter.Dispose()` is currently a no-op. Serilog users call `Log.CloseAndFlush()` in `finally` blocks to drain async sinks before process exit — a silent no-op breaks that contract. Decision: distinguish two construction paths:
- `FromBuild(PipelineBuildResult)` — adapter OWNS the pipeline → `Dispose` flushes + disposes
- `SerilogLoggerAdapter(StructuredLogger)` wrap-existing constructor — adapter does NOT own the pipeline → `Dispose` stays no-op
This is a real gap to close in Task 7 (when `CloseAndFlush` is implemented) and the double-CloseAndFlush test (R-5 pin) must pass with the live sink flushed exactly once.

### R-TASK11-COMMENT: The plan says "P0 renamed WithContext" — it didn't
Plan Task 11 intro says "the rename made Herald's WithContext→ForContext in P0." This is wrong — P0 did not rename it (deferred per R-DEFER above). The test assertions are correct; the comment is stale. Fix the comment when Task 11 is implemented.

### R-NAMESPACE: Already correct — close the OD
`MMP.Herald.Serilog.Events.LogEventLevel` is already in the tree. Layer-2 (P7) re-exports to bare `Serilog.Events`. Closed.

### R-OD1-LOG-LOGGER: Already ratified and built — close the OD
R-1 settled Layer-1 volatile holder. `SilentLogger` is already shipped (Task 3). No separate `StaticFacade` assembly. Closed.

## Echo Test-Gap Pre-Inventory Pending
Echo's Task-5–12 gap inventory is running in parallel. When it lands, fold its specific test prescriptions into the implementer briefs for each task.

## Rosanne Seam Review Pending
Rosanne's Task-8 seam review is running. When it lands, Task 8's implementer brief must incorporate: the `LogEventPropertyFactory` exposure decision, whether `LogEventValueProjector.Project` needs a `LogProperty[]` overload for P4 seams, and whether the Task-8 destructure logic needs to be refactored for P4's consumer policy path.

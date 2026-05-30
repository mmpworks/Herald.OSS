# Echo Pre-Implementation Test-Gap Inventory — P1 Tasks 5–12

**For implementing agents.** Every gap below is a *silent* failure — the happy-path test stays green while the bug ships. Grounded against the actual OSS tree on `feat/serilog-compat`.

**Two anchoring facts (verified):**
- `LogPropertyCompact.From<T>` (LogPropertyCompact.cs:170–191): zero-box for `int/long/double/bool/DateTime/string`; exactly one box at the `(object?)value` arm (line 190) for `decimal/Guid/TimeSpan/enum/struct`.
- `LogPropertyCaptureMode` is a **record** (value equality), not an enum — `.Should().Be(LogPropertyCaptureMode.Default)` works.

---

## Task 5 — G-HOT.2 exact-byte alloc sweep (arity 1..16)

### Highest-risk arities for missing `[OverloadResolutionPriority]`
Arities **3, 5, 6, 7, 9–15** (exactly Jared's matrix gaps). These are emitted by the same loop but never independently exercised by any correctness test. The full 1..16 sweep is the only thing that catches a one-arity priority typo. Do NOT optimize down to band representatives — the optimization IS the bug.

### "Exact 0 bytes" reliability
`AllocationProbe.BytesPerIteration` is reliable on net9/net10 (per-thread counter, 2000-iteration warmup, 100k iterations, floor-divided). **One trap:** `TemplateFor(arity)` must return an **interned, cached, compile-time `const` string** — NOT `$"..."` interpolation or concatenation (non-interned strings skip the hole-index cache, every iteration re-parses, test measures parse allocation and goes RED falsely). Hoist the template outside the lambda; the plan's `var template = TemplateFor(arity)` is correct only if `TemplateFor` returns a stable interned literal.

### Decimal/Guid boxing test — exact byte count
Do NOT hardcode `24` or `32` (arch-dependent). Compute empirically:
```csharp
long oneBox = AllocationProbe.TotalBytes(() => { object b = 3.14m; AllocationProbe.Consume(b); }, ...)
              / AllocationProbe.DefaultMeasuredIterations;
```
Assert the one-decimal-arg call allocates exactly `oneBox`. The xUnit gate should be self-referential (not Serilog-version-dependent); the parity claim ("equal to Serilog") belongs in the benchmark row, not the gate.

**Missing row the plan doesn't name:** mixed `(int, decimal)` call. Must read exactly `oneBox` (decimal boxes; int doesn't). A fallback that boxes ALL args when ANY is non-primitive reads `2 * oneBox` and the test goes RED — catching a real regression.

---

## Task 6 — G-HOT.3 per-hole routing

### Mixed-hole test: what does each property's CaptureMode look like?
**Per-hole capture modes in ONE event.** `"User {Id} ordered {@Order} at {$Time}"` with `(7, order, DateTime.UnixEpoch)` must produce:
- `Id` → `CaptureMode.Default`
- `Order` → `CaptureMode.Destructure`
- `Time` → `CaptureMode.Stringify`

All three in `captured[0]`. Add: `Id`'s **value** is still `7` (the full-path fallback didn't drop the primitive value while preserving the mode).

### `{$primitive}` edge case
`log.Information("id {$Id}", 7)` must produce `Id.CaptureMode == Stringify`, even though the runtime value is `int`. A "smart" optimization that strips `$` from primitives silently drops the mode and shifts rendering in downstream sinks.

### Same-name-different-mode
`"{X} {$X}"` with one `X` value — pin the behavior (which mode wins? one property or two?) against the oracle via `SerilogParityOracle.CaptureSerilog`. Do not guess; measure.

---

## Task 8 — G-VM.1/2 value-model parity

### Canonical-shape equivalence (concrete definition)
Four-tuple, recursive:
1. Same property name-set (set comparison, NOT ordered)
2. Same value-node subtype per property, recursively (`ScalarValue/StructureValue/SequenceValue/DictionaryValue`)
3. Same scalar values (`object.Equals`)
4. Same level mapping (via `SerilogLevelMap`)

NOT: render-string equality (brittle, culture-dependent), byte/CLEF-JSON equality.

**Three caveats for false REDs:**
1. **TypeTag** — compare leniently or separately; anonymous types may differ between Serilog and shim
2. **Numeric widening** — verify `int` isn't widened to `long` when reconstructed from `ScalarBits`
3. **Property ordering** — name-set comparison, NOT positional index comparison

### `CaptureShim()` implementation (Task 8 critical path)
Wire through `TestLoggers.CreateCapturing()`, run the action, take the captured native `LogEvent`, wrap in `MMP.Herald.Serilog.Events.LogEvent`, return it. The comparison reads `.Properties` — triggering lazy projection, exercising Guard-1's confined site. G-VM.1 passing proves the projector actually ran (positive control for Guard 2).

### G-VM.2 cycle-handling — two required inputs
1. Direct: `var node = new Node(); node.Next = node;` — proves termination on self-reference
2. **Indirect cycle** (the case most visited-set implementations miss): `a.Next = b; b.Next = a;`

Assert: (a) the call terminates (wrap in timeout — a hang is worse than a RED), (b) shim parity with Serilog's truncation depth via the oracle. Also add: `{@Obj}` where `Obj == null` (must project to null scalar, NOT throw); object with a null member (projector must not NRE).

---

## Task 9 — Guard 1 architecture test

### `GetReferencedAssemblies().NotContain("MMP.Herald.Serilog")` alone is insufficient
It's a tautology by construction (the engine doesn't reference the mirror; the linker elides unused references). Needed: **both** checks.

1. **Reference-name check** — keep it, but add: `typeof(StructuredLogger).Assembly != typeof(MMP.Herald.Serilog.Events.LogEvent).Assembly` (if they ever merge into one assembly, the reference check becomes meaningless; this sub-assertion goes RED to tell you why).

2. **Type-level walk (the load-bearing guard)** — walk ALL engine assembly types (public + non-public via `GetTypes()`); for each, check method parameters, return types, fields, properties for any type whose assembly is `MMP.Herald.Serilog`. Assert none. Recurse into `GetGenericArguments()` — `Func<Serilog.Events.LogEvent>` hides the forbidden type one level down. Also walk internal members (the `InternalsVisibleTo` grant makes internal back-references a live possibility).

The type-walk localizes *which* type broke confinement. The name-check alone can't fire. Frame the type-walk as the real test.

---

## Task 10 — Guard 2 + G-HOT.1

### Use BOTH probes; they're orthogonal
- **Allocation probe** (`0 B/op`) — the gate, proves the hot path stays allocation-clean. Doesn't prove the mirror was never *entered*.
- **Call-count probe** (`ProjectionCount == 0`) — proves the mirror was never *instantiated* on the native path. Structural claim, not just performance.

Both assert different things. Both can fail independently. Keep both wired in one `[Fact]`.

**Static counter isolation hazard:** xUnit runs test classes in parallel by default. A static `ProjectionCount` counter corrupted by a concurrent projection in another test produces a flaky RED. Fix: mark the Guard-2 class `[Collection]-serialized` OR make the counter `[ThreadStatic]`. Do NOT ship a plain static counter under default parallelism.

**Positive control (missing from the plan):** pair the `== 0` native-path assertion with a `== 1` custom-extension-path assertion (`[Fact]` sibling). Without it, a counter that's always zero (broken counter) passes the gate vacuously.

---

## Tasks 11–12 gaps the plan doesn't address

### Task 11 (ForContext)
1. **Parent unaffected** — `child = parent.ForContext(...)`, log through `parent`, assert parent event does NOT carry the child's context property. Catches context pushed onto shared/ambient scope.
2. **Chaining accumulates** — `parent.ForContext("A",1).ForContext("B",2)`, grandchild carries both. Catches single-context-slot implementations.
3. **`destructureObjects: true` actually destructures** — assert the property's `CaptureMode` or `StructureValue` shape, NOT just its presence.
4. **`ForContext<T>()` SourceContext format** — pin against the oracle; Serilog uses the **full** type name (`Namespace.Type`), not the short name. A consumer filtering on SourceContext breaks silently on the wrong format.

### Task 11 (template integration)
1. **Mixed positional + named holes** (`"{0} did {Action}"`) — Serilog has defined (discouraged) behavior; pin against oracle.
2. **Count mismatches** — more args than holes (extras as `__N` or dropped) and fewer args than holes. Never throw. Pin against oracle.
3. **Boundary inputs** — empty template `""`, template with no holes, arity 0 (the plan's alloc sweep starts at 1; a zero-arg call is a *correctness* gap).

### Task 12 (wave-close)
1. **AOT check must specifically exercise the projection path** — the native path's AOT-cleanliness (proved by Guard 2) says nothing about the projector's. Add an AOT test row that forces a projection (a custom extension reading `.Properties`). Otherwise the reflection-heavy path ships AOT-unverified.
2. **Both net9 and net10 must run the correctness gates** — benchmark is net10-only (correct), but alloc/parity/capture-mode tests must pass on BOTH TFMs. Jared's "no TFM fork" claim is only proved by running it.

---

## Cross-cutting for all Task 5–12 implementers

- **`CaptureShim()` is on the critical path** for Tasks 6, 8, and Task-11 parity assertions. Implement it early in Task 8; write the stub so later tasks can depend on it.
- **"Pins against the oracle" means capture real Serilog, don't hand-author the expected value.** Serilog 4.3.1's actual behavior on edge cases diverges from docs in places.
- **Every test must go RED when the production code is reverted.** Before committing a green test, mentally delete the guarded line and confirm it fails.

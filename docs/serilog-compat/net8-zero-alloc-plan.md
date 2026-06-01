# .NET 8 Serilog compat — zero-alloc plan

**Date:** 2026-06-01
**Authors:** Jared (runtime/JIT verdict), Richard (API/generator/packaging), red-teamed via the-fool
**Status:** Plan — one Steve decision gates implementation (Tier B, below)

---

## Headline

**.NET 8 hits true 0 B on the typed Serilog path, byte-identical to net9 — measured, not theorized.**

The net8 hold-back was never a runtime-capability gap. The zero-alloc path leans on
`[InlineArray]`, which is a **.NET 8 feature** — the stack-allocation guarantee is
language-level, not a JIT escape-analysis heuristic that could differ across runtimes.
Jared built a BenchmarkDotNet probe mirroring the exact shipped shape and measured net8
vs net9:

| Case | net8 Allocated | net9 Allocated |
|------|----------------|----------------|
| Arity 1, primitive | **0 B** | 0 B |
| Arity 3, all-primitive | **0 B** | 0 B |
| Arity 3, mixed (int/string/bool) | **0 B** | 0 B |
| Arity 8, all-primitive | **0 B** | 0 B |
| Arity 16, all-primitive (512 B buffer) | **0 B** | 0 B |
| Arity 3, one custom struct arg | 24 B | 24 B |

The 24 B case is the single box a value-type property always pays — irreducible and
identical on both runtimes. **net8 and net9 are byte-for-byte identical on allocation.**

---

## Why it works (the dependency analysis)

Every runtime primitive the typed path touches is net8-available:

| Feature | net8 status | On the critical path? |
|---------|-------------|----------------------|
| `[InlineArray]` | Shipped in net8 (it IS the .NET 8 feature). Struct-local stack residency is a language contract, not size-dependent JIT heuristic. | **Yes** — the load-bearing primitive |
| `[OverloadResolutionPriority]` | Compile-time only; C# 13 compiler honors it regardless of target TFM. Polyfill already ships. | **Yes** — for correct binding, see Tier B |
| `params ReadOnlySpan<T>` (net9 stack-allocated params-span) | net9+ only | **No** — the typed overloads take individual `T1..Tn` args and build the buffer themselves, slicing to a plain `ReadOnlySpan`. They never use params-span. |
| `Unsafe.As<T,TTo>` unboxed reinterpret | net8-available | **Yes** — avoids the box for int/long/double/bool/string |
| `readonly ref struct` (LogEventBuffer) | net6-era | **Yes** — guarantees the span can't escape to heap |

The "params-span heap-allocates on net8" concern is moot: the generated overloads already
use the explicit per-arity InlineArray fill that would have been the workaround. That's the
shipped mechanism.

---

## Scope correction

The Serilog source **merged into the `Herald.OSS` assembly** (0.12.x). `MMP.Herald.Serilog.csproj`
is now a forwarding shim with no source. The net8 lever is a single conditional in
`Herald.OSS.csproj`:

```xml
<!-- This is the entire blocker. -->
<ItemGroup Condition="'$(TargetFramework)' == 'net8.0'">
  <Compile Remove="src\Serilog\**" />
</ItemGroup>
```

`Herald.OSS` Core already multi-targets `net8.0;net9.0;net10.0` and ships net8 today.
Only the Serilog skin is fenced out.

---

## Three tiers (the honest contract)

"net8 support" is not one contract. It's three, split by what each needs.

### Tier A — kernel + compact path on net8 → **SHIP LOUDLY**
0-alloc-capable because `[InlineArray]` is a net8 feature. Measured 0 B at every arity 1–16.
Mechanical, correct, no asterisk. This is "net8 zero-alloc structured logging."

### Tier B — typed Serilog overloads on net8 → **needs ONE decision**
Compiles and runs 0-alloc. BUT correct overload dispatch requires the **consumer** to compile
at **C# 13** (`<LangVersion>13</LangVersion>`, available on SDK 9+ even when targeting net8).

On the default net8 SDK (C# 12), `[OverloadResolutionPriority]` is silently ignored, and a
call like `Information(tmpl, a, b, c, d, e)` binds to a lower-arity overload — consuming the
trailing args as `CallerArgumentExpression` name overrides. This is the exact silent-miscount
bug the attribute exists to prevent (documented in the polyfill header).

**This is a correctness hazard on C# 12 consumers, not a perf downgrade.** It's invisible on
net9 because net9 consumers ship the C# 13 SDK by default. On net8 a consumer can legitimately
still be on C# 12.

Cross-assembly dispatch is metadata-driven, so the polyfill being `internal` vs the BCL's
`public` attribute is invisible to the consumer — the footnote is purely "consumer must be C# 13,"
no second IL-divergence caveat.

**Decision (settled 2026-06-01, team + the-fool): B1 + targeted analyzer.**

The original framing was B1 (document) vs B2 (gate behind opt-in). Resolved:

- **B2 is not cleanly implementable.** `SerilogArityGenerator` bakes the overloads +
  `[OverloadResolutionPriority]` metadata into `Herald.OSS.dll` at Herald's compile. You cannot
  hide methods from a referenced assembly based on a downstream csproj property.
- **B2c (auto-bump consumer LangVersion via the buildTransitive props Herald already ships) is
  REJECTED.** It trades the narrow opt-in hazard for a broader invisible one: it silently overrides
  a consumer who deliberately pinned C# 11, breaks CI under `TreatWarningsAsErrors` via new C# 13
  diagnostics (error points at the consumer's code, not Herald), and the "non-silent" build message
  dies at default CI verbosity. A logging package that mutates your compiler version is the canonical
  CUPID Predictable violation. **Do not re-propose this as "the obvious low-friction win."**

**Chosen path — ship the typed overloads on net8, do NOT touch LangVersion, add a Roslyn diagnostic:**
A targeted `HRLDxxxx` analyzer fires ONLY when a net8 / C# < 13 consumer actually calls a typed
overload at an arity where the silent-miscount is possible. Message: *"binds to a lower-arity
overload because `[OverloadResolutionPriority]` requires C# 13 — set `<LangVersion>13</LangVersion>`
or use named arguments."*

Lower friction than plain B1: the compiler squiggles the exact call site instead of the user
discovering the requirement in docs. Safer than B2c: Herald never reaches into the consumer's
settings; the fix stays their explicit, visible choice. The existing `HRLD0001`/`HRLD0002` analyzer
infrastructure hosts it — one more diagnostic, not new machinery. Below C# 13 the params-object
fallback is correct (allocates, still cheaper than real Serilog); named-args are the escape hatch.
Document in both registers per the dual-register doc rule.

### Tier C — ITextFormatter console bridge, AspNetCore, Layer-2 → **stay net9+**
- **AspNetCore** — `FrameworkReference Microsoft.AspNetCore.App` + legacy Http.Abstractions.
  Legitimately net9+. Leave it.
- **Console `ITextFormatter` bridge** — currently net9-gated; a forward seam not wired to the
  console path today. net8 omission is a small drop-in-compat hole (a Serilog user calling
  `WriteTo.Console(myFormatter)` compiles on net9, fails on net8). Needs an explicit in/out ruling.
- **Layer-2 (`Serilog.Log.*` mirror)** — keep net9+ unless there's a concrete net8 drop-in
  customer. The Tier-B C# 12 hazard is WORSE here: the user didn't opt into Herald's surface and
  won't know to set `LangVersion`.

---

## Implementation path (once B1/B2 is chosen)

1. **`Herald.OSS.csproj`** — delete the `<Compile Remove="src\Serilog\**" />` net8 conditional
   block. The two whole-file net9 gates (`TextFormatterOutputTransformer`,
   `TextFormatterConsoleSinkProvider`) self-exclude on net8 via their `#if`.
2. **No `TargetFrameworks` change** — Herald.OSS already lists net8.
3. **`Configuration/LoggerSinkConfiguration.cs:78`** — the one `Console(ITextFormatter, …)`
   overload is net9-gated; the net8 build omits it cleanly (Tier C ruling).
4. **`MMP.Herald.Serilog.csproj`** — add `net8.0` to `TargetFrameworks` so net8 consumers
   referencing the old package id resolve a net8 asset.
5. **Generator** — no change. `SerilogArityGenerator` emits framework-neutral C#; the
   `[OverloadResolutionPriority]` binding resolves against the polyfill on net8, the BCL on net9.
   Same source text. No per-TFM conditional emission.
6. **Build-verify (don't reason about):** the net8 Serilog source under the AOT/trim analyzer —
   confirm no new IL2026/IL3050 fires. This is the one thing to actually compile-check.
7. **Update stale csproj comments** — "requires .NET 9+ runtime support" is wrong for the
   allocation contract. Reword to the C# 13-consumer-SDK requirement.
8. **Docs (Heather)** — dual-register net8 story: "0-alloc on the typed path 1–16, identical to
   net9, if the consumer builds with C# 13; without it the path still works but binds to the
   lower-arity overload — named-arg call sites are the escape hatch."

---

## Honest verdict

**Verdict A: net8 hits true 0 B**, mechanism = `[InlineArray]` stack residency (net8 language
feature) + explicit per-arity buffer fill + `Unsafe.As` unboxed reinterpret. The path is
currently disabled by a csproj exclusion whose rationale overstated a runtime requirement that
is actually a consumer-SDK requirement.

The work is a build-config change plus an honest documentation footnote — not a re-architecture.
The single real decision is Tier B's C# 12 consumer hazard (B1 document vs B2 gate).

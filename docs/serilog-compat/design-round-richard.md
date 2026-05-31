# Design Round — Richard (architecture lead)

- **Date:** 2026-05-29
- **Branch:** `feat/serilog-compat`
- **Charter:** two-layer structure, rename ADR, CUPID/DRY, vetting (the-fool ran live; Rosanne/Echo were not dispatchable in his subagent env — his §D.2/§D.3 are stand-ins to be ratified by the real agents).
- **Status:** complete; reconciliation with Jared + real Rosanne/Echo in progress.

## The architectural discovery — the value model is the one real fork

Herald's `LogEvent.Properties` is **flat**: `IReadOnlyList<LogProperty>`, `Value` is `object?`. Serilog's is a **recursive tree**: `IReadOnlyDictionary<string, LogEventPropertyValue>` where `LogEventPropertyValue = ScalarValue | SequenceValue | StructureValue | DictionaryValue`. Every custom `ILogEventSink.Emit`, `ILogEventEnricher.Enrich`, and `ITextFormatter.Format` walks that tree.

**Decision — "flat-fast, tree-on-demand" bounded mirror.** The mirror's `Serilog.Events.LogEvent` wraps Herald's native `LogEvent` (no copy). Its `Properties` are **lazily projected** to `LogEventPropertyValue` **only when a user-supplied extension reads them**. Scalars (the vast majority) project to `ScalarValue` with no walk; `{@}`-destructured objects project to Structure/Sequence/Dictionary via Herald's existing destructurer, only on access. **The hot path — events to Herald-native sinks (Console/File/HTTP/ES/OTLP) — never instantiates the mirror.** The allocation cost is paid only by consumers who opted into custom extension code, and is documented. This is the call Richard wants Jared's sign-off on (it abuts the zero-alloc lowering lane).

## Twice-confirmed honesty correction (Richard + Jared, independently)

The "1-to-1 / zero-code-change drop-in" headline is **structurally impossible** without Serilog's strong-name key (we won't spoof it). Mirrored types deliver **source compatibility on recompile, never binary drop-in**: the consumer's own code recompiles and runs; a pre-compiled third-party assembly built against real Serilog's identity will not load. **Defensible claim:** *"Swap the package, rebuild, and your standard Serilog code runs on Herald — popular sinks map over; the third-party-sink ecosystem and the expression DSL are a documented boundary."* The intent (low-friction adoption) is fully intact; only the unspoofable "binary identical" wording goes.

## Assembly topology (5 new assemblies)

```
MMP.Herald.Serilog            (Layer 1 — source-compat, canonical impl; static Log facade + arity generator live HERE)
MMP.Herald.Serilog.AspNetCore (Layer 1 — UseSerilog/AddSerilog/request logging over HeraldLoggerProvider)
Serilog                       (Layer 2 — literal mirror; ZERO behaviour; each type forwards to its Layer-1 twin)
Serilog.AspNetCore            (Layer 2 — literal mirror of the ASP.NET surface)
Herald.OSS.Serilog.Settings   (Apache-2.0, standalone — appsettings.json binding; reimplemented parser)
```

Naming: `MMP.Herald.*` (vendor-authored glue), not bare `Herald.*` (reserved for core). **Layer-2 mechanism = mirrored types, NOT `[TypeForwardedTo]`** (forwarding can't launder identity; converges with Jared).

## Rename ADR (ADR-SERILOG-RENAME-001)

**Tier 1 is SMALLER than the PRD sketch.** The only true native renames:
- `WithContext(dict)` → `ForContext(dict)` **+ add** Serilog's `ForContext<T>()` / `ForContext(Type)` / `ForContext(string,value,bool)` overloads (genuine native ergonomics win).
- Typed verbs `Info/Warn` → `Information/Warning` **+ add** `Verbose`/`Fatal` as first-class verbs (removes a native asymmetry).
- Inline `{@}`/`{$}` template parsing → sets existing `LogPropertyCaptureMode.Destructure/Stringify` (additive, no type rename).

Everything else is additive or compat-layer-only. **`PushProperty` stays compat-only** (native keeps `BeginScope`, which is also the MEL name). **`QuickLogBuilder` is NOT renamed** (see Dissent D-1).

**Tier 2 — level rename (cross-repo).** Confirmed mapping: `Info→Information`, `Warn→Warning`, `Critical→Fatal`, `Trace→Verbose`; `Debug`/`Error` already aligned. **Herald's extra levels (`Notice`/`Success`/`Security`/`Metric`) are KEPT untouched**, absent from the bidirectional Serilog map. Blast radius: `LogLevel.Key` values ripple into persisted pipeline JSON, SSE `level`/`levelKey` fields, Dashboard SPA level lane, DemoApp seeds, level-gating + level-mutation wire contracts.

**Lockstep sequence (so the SSE stream never desyncs from the SPA mid-deploy):**
1. Add a **transitional** level-key alias map (`info`↔`information` etc.) — scaffolding only, removed in step 6.
2. Rename `KnownLogLevels` + all native key emission to new keys.
3. Rename typed verbs `Info/Warn→Information/Warning`, add `Verbose/Fatal` (land verb+level convergence in one wave).
4. Update wire/SSE emitters to new keys (SPA still alias-tolerant).
5. Update Dashboard SPA + DemoApp seeds; flip SPA parsing to new keys primary.
6. Remove the transitional alias map; pin a regression test (old keys gone, four extra levels survive).

This is a **joint Herald.OSS + Dashboard (Nancy) + DemoApp (wire) commit wave**, Max on build/packaging.

## CUPID/DRY structure

Canonical impl = renamed Herald core. Both compat layers are mapping skins:
`Serilog.Log.Information` → `MMP.Herald.Serilog.Log.Information` (only facade logic) → `StructuredLogger.Information` → kernel fast path.
Layer-2 types contain **zero** behaviour — any `if`/loop/format in a Layer-2 type is the DRY tripwire, reject it. `Log.Logger` is one mutable slot in Layer 1 (not duplicated). The value-model mirror is a wrapper, not a copy. The `LoggerConfiguration` builder is a **translator onto `QuickLogBuilder`** (still produces JSON → JSON drives construction, honouring JSON-as-source-of-truth). **DRY restraint:** do NOT merge the static facade with the DI typed logger — ambient-singleton vs injected-instance are different concepts that merely look alike.

## the-fool dispositions (ran live) — the ones that matter

| # | Break-point | Disposition |
|---|---|---|
| 1 | Third-party sinks (Seq/MSSql/Datadog/community) | **Hard wall**; claim corrected; parity audit leads with Seq, not a footnote |
| 2 | Custom `ILogEventSink` | Resolved by the value-model mirror (confined cost) |
| 3 | Output templates `{Level:u3}`/`:lj` + `ITextFormatter`/CLEF | CLEF mirror-able; **output-template grammar is a v1 named gap** that degrades silently to wrong output — Richard flags higher-population than v1-optional implies (Dissent D-3) |
| 4 | `LoggingLevelSwitch` | **Native `LogLevelSwitch` is a structural match** — not a gap |
| 5 | `Filter.ByExcluding` / `Serilog.Expressions` | ByExcluding maps to processors; **Serilog.Expressions DSL is a hard wall** |
| 6 | Custom `ILogEventEnricher` | Resolved by value-model mirror + native `ILogEnricher` |
| 7 | `IDestructuringPolicy`/`Destructure.*` | Value-model family; security note: a no-op'd `ByTransforming` stripping a password field is a security regression — must not silently no-op |
| 8 | `Serilog.Debugging.SelfLog` | Cheap — honour it (high trust-during-incident value) |
| 9 | Sub-loggers + **`AuditTo`** | `AuditTo` MUST throw-on-failure (compliance contract) — distinct from `WriteTo`'s swallow path; silently swallowing an audit failure is the worst break given Herald's compliance positioning |

## Dissents (designed the in-scope path regardless)

- **D-1 — Do NOT rename `QuickLogBuilder`.** The Serilog-shaped builder is a separate compat type that translates onto it; renaming the native builder churns every pipeline/JSON/FakeServer reference for zero compat benefit. Keep it, map onto it.
- **D-2 — "1-to-1" is the wrong marketing headline** (claim-honesty, not scope). Recommend the corrected claim above to Steve, per the security-due-diligence-defensibility standard.
- **D-3 — Output-template grammar is higher-population than its v1-optional status implies.** If not v1, it must be the loudest line in the parity audit and the first fast-follow.

## Open items for reconciliation

1. **Jared sign-off** that the value-model lazy mirror (§ value model) doesn't perturb the fast path's escape analysis.
2. **Real Rosanne + Echo** must ratify/extend Richard's seam (§D.2) and test-gap (§D.3) stand-ins.
3. Steve decisions: claim-honesty wording, output-template priority (v1 vs fast-follow), `AuditTo` throw-semantics confirmation, value-model bounded scope.

# Test-Gap Inventory — Serilog-Compat (Echo)

- **Date:** 2026-05-29 · **Branch:** `feat/serilog-compat`
- Replaces Richard's §D.3 stand-in. Reconciled with Jared's lowering verdicts + the-fool's live red-team. Design-only; test-master implements.
- **Unifying principle:** every regression below leaves the happy path **green**. The tests that catch them are **negative, inter-state, exact-count, or oppositional-pair** assertions — none of which a "does it work?" suite contains by default. Build the cross-cutting fixtures first; they make the rest cheap.

## Tier 1 — silent failures (ship-blocking)

### Guards rename lockstep (Richard ADR Tier 2)
- **G-LEVEL.1 — old-persisted-JSON round-trips through the transitional alias map**, every renamed key. Trap: `critical→fatal` is a value rename to a *previously-nonexistent* key — most likely omitted from an alias map authored as "four lowercase→Serilog pairs." Observable: loaded `Key` is new; event survives ingest (not rejected → not vanished).
- **G-LEVEL.2 — replay-buffer spans the rename (inter-step desync, step 4→5).** Seed FlightRecorder with old-key events, swap to new-key emitter, connect a *fresh* SSE client, assert every replayed (old-key) AND live (new-key) event resolves to a severity. The break no at-rest per-step test catches — it lives in the *mixed stream*. Also guards step-6 alias removal.
- **G-LEVEL.3 — level-mutation round-trip, reverse direction.** SPA sends `{levels:[...names], minimumLevel, expectedETag}` with the new name; assert resolved `minimumLevel` == sent. Catches a server handler that hard-compares `=="info"` instead of using the alias map → mutation silently no-ops, filter quietly stops working.
- **G-LEVEL.4 — the four extra levels survive.** `Notice/Success/Security/Metric` keys unchanged + absent from the bidirectional Serilog map. Catches a "normalize all levels to Serilog names" pass silently deleting them.
- **G-LEVEL.5 — alias map fully removed at step 6.** Old keys post-step-6 are *rejected* (loud), not silently aliased; the alias symbol no longer exists. (Richard's step-6 regression test — pin it.)
- **G-LEVEL.6 — cross-table equivalence (high leverage).** `KnownLogLevelKeys` (analyzer table, drives HERALD007) and `LogLevelKeys` (runtime) are separately hand-authored and **already disagree today** (stray `Fatal`). One test in a suite referencing **both** projects asserts value-set equality against the *intended* post-rename state, with the `Fatal` exception resolved explicitly. Do NOT write naive "assert equal" (fails day one on `Fatal`, gets weakened to pass = theater).

### Guards hot-path / lowering (Richard value model + Jared lowering)
- **G-HOT.1 — native-sink events never instantiate the mirror (load-bearing).** Pair a call-count probe on the mirror's projection entry (== 0 on native path) with a 0-B/op alloc benchmark matching the pre-compat baseline. **This is Jared's Guard 2.** Plus **Guard 1**: an architecture test asserts no kernel/native-sink assembly references `Serilog.Events.LogEvent` (structural, not convention).
- **G-HOT.2 — `[OverloadResolutionPriority]` silent boxing — exact-byte, EVERY arity 1..16.** Jared's stated matrix `{0,1,2,4,8,16,17}` **skips 3,5,6,7,9-15** — a one-overload priority typo there is invisible. Sweep the full 1..16 (cheap loop). Assert **exact bytes**, split by source: 0 B for the six hot primitives (bug if not), exact-N for N value-type args (by design, equal to Serilog). Never a threshold gate (swallows one-box-per-call). Plus call-1-vs-call-2 cache-hit delta (catches a cached-template re-parse). Reject-path row: 0 B native + shim.
- **G-HOT.3 — `{@}`/`{$}` per-HOLE routing, mixed-hole template.** Load-bearing test is ONE template `"User {Id} ordered {@Order} at {$Time}"`: assert `Id` scalar AND `Order` destructured-structure AND `Time` stringified, simultaneously. Single-mode tests run separately **pass against a broken per-call router**. Also: `{$Id}` where `Id` is `int` (stringify-of-primitive must NOT compact — a "is value primitive?" check drops the `$`); same-name-different-mode (`"{X} {$X}"`) resolves to Serilog's behavior.

### Guards the-fool's correctness traps (security/compliance)
- **G-SEC.1 — redaction actually fires (negative, secret-in-fixture, full-output scan).** `ByTransforming<User>(u => new {u.Name})` stripping `Password`; fixture password = `hunter2`; assert `hunter2` appears **nowhere** in the fully-serialized event (scan all fields + message + exception ToString — a field-name check misses leak-into-other-field). **Acceptance for the test itself: it must go RED if the transform registration is deleted.** Ship-blocking.
- **G-SEC.2 — `AuditTo` throws while `WriteTo` swallows (oppositional pair).** Inject a `ThrowingSink`; assert `WriteTo` swallows (+ reports via SelfLog) and `AuditTo` propagates. Same failure, opposite outcomes — the only shape that catches `AuditTo` accidentally wired to the swallow path.
- **G-SEC.3 — redaction runs BEFORE audit capture (ordering).** Both `WriteTo` + `AuditTo` wired with redaction active; assert secret absent from **both** outputs (audit paths forget redaction-first).
- **G-SINK-WALL.1 — third-party sink fails LOUD + NAMED, never silent (SUITE).** Config naming Seq/MSSql/Datadog → named error containing the sink name + identity-wall reason (matching the parity-audit verbatim text); never a silent no-op. A gap-class → functionality suite.

## Tier 2 — value-model parity + corpus
- **G-VM.1 — value-model materialization parity (table-driven).** scalar→`ScalarValue`, `{@}`-object→`StructureValue`, sequence→`SequenceValue`, dictionary→`DictionaryValue`, with real Serilog as the parity oracle (Layer-1 only — Layer 2 can't coexist).
- **G-VM.2 — nested + cyclic + null destructure.** No stack overflow / infinite walk; parity with Serilog's depth/cycle handling.
- **G-CORPUS.1 — real-Serilog snippet corpus compiles AND runs unchanged (SUITE).** Instance API + static `Log` + `LoggerConfiguration` code config → canonical-shape-equivalent `LogEvent` (per ingress↔output canonical-equivalence rule).
- **G-CORPUS.2 — `appsettings.json` round-trip (SUITE).** `MinimumLevel/WriteTo/Enrich/Override` → parser → builder → JSON → construction; a Seq entry fails loud (ties G-SINK-WALL.1). Net-new parser = net-new bugs.
- **G-CORPUS.3 — ASP.NET wiring output shape (SUITE).** `UseSerilog()` + `AddSerilog()` (thin over `HeraldLoggerProvider`, verified) + `UseSerilogRequestLogging()` — the request-log line fields + exactly one per request (the one net-new component).
- **G-CORPUS.4 — custom sink/enricher/formatter compile-and-run.** The path that pays the mirror cost; exercises G-VM.1 from the consumer side.
- **G-LAYER2.1 — Layer-2 coexistence fails at COMPILE (`CS0433`), not runtime `InvalidCastException`.** Enforces "Layer 2 is the only Serilog in the graph" rather than documenting-and-hoping.

## Tier 3 — named-gap regression pins (every audited gap → a test)
- **G-GAP.1 — output-template grammar** (`{Level:u3}`/`:lj`): now v1 (Steve) — pin parity tests; until then, pin current behavior so silent-wrong-output is at least *known*.
- **G-GAP.2 — `Serilog.Expressions` DSL:** config using it fails loud + named.
- **G-GAP.3 — `LoggingLevelSwitch`** parity (structural match — pin so a future divergence is caught).
- **G-GAP.4 — `SelfLog`** receives the `WriteTo` swallow report (ties G-SEC.2).
- **G-GAP.5 — CLEF formatter** parity vs real Serilog.
- **G-GAP.6 — `{{`/`}}` escaping + positional holes** (table-driven parse).
- **G-GAP.7 — AOT-clean:** compat layer publishes with no new trim/AOT warnings vs the Herald.OSS baseline.

## Cross-cutting fixtures — build these FIRST (they make the inventory cheap)
- **`ThrowingSink`** (throw-on-cue, records calls) → G-SEC.2/3, G-GAP.4, hot-path failure isolation. Highest leverage.
- **`SecretBearingFixture` + full-output secret scanner** → G-SEC.1/2/3. The scanner (scans the *entire* serialized event for the secret value) is load-bearing; field-name checks miss leak-into-other-field.
- **Real-Serilog parity oracle harness** (Layer-1 coexistence; same input through real Serilog + shim, diff canonical shape) → G-VM.1/2, G-CORPUS.1, G-GAP.5.
- **Exact-byte allocation harness** (net10, BenchmarkDotNet InProcess; exact bytes not thresholds; full arity 1..16; call-1-vs-2 delta) → G-HOT.1/2.
- **Replay-ring + SSE inter-step harness** (seed pre-rename, swap emitter, fresh client, assert across mixed stream) → G-LEVEL.2/3. The piece most likely skipped under deadline — required, not optional.
- **Cross-table reflection fixture** (references BOTH the netstandard2.0 generator project AND the runtime project — they currently can't see each other, the root cause of the `Fatal` drift) → G-LEVEL.6.

## Already well-served (verify, don't rebuild)
- `HeraldLoggerProvider` ships the full MEL surface + reads `{OriginalFormat}` — G-CORPUS.3 only pins the net-new request-logging middleware.
- `LogPropertyCompact.From<T>` primitive specialization is existing/analyzer-enforced — G-HOT.2 pins the shim's *routing into* it.
- `TypedArgsOverloadGenerator` has a golden-test pattern (`tests/Generators/TypedArgsOverloadGeneratorGoldenTests.cs`); the new Serilog-hole-named arity generator needs its own golden test in that shape **plus** the G-HOT.2 runtime alloc test.

## Execution-phase note
Echo flags a **separate** risk surface for a the-fool pre-mortem before Glenn starts: a **half-applied rename across the ~40 key-referencing files** (mechanical-application risk), distinct from these test gaps. Run it before the mechanical rename phase.

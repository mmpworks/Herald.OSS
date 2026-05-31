# P4 Extension Seams — the-fool Pre-Mortem

> Generated: 2026-05-30. Input: the-fool skill, pre-mortem mode.
> Framing (Pass 1): "Four user-extension adapters bridge Serilog contracts onto Herald primitives.
> A regulated shop's `IDestructuringPolicy` strips a `password` field and their `AuditSink` is a
> compliance contract. Where does an adapter silently no-op a redaction, silently swallow an audit
> failure, or silently flatten a `{@}` tree — leaving the happy path green while PII leaks or an
> audit-loss goes unnoticed?"
>
> Framing (Pass 2): "A user who called `WriteTo.Sink(mySink)` and `AuditTo.Sink(myAuditSink)` expects
> the sink router to distinguish the two. Where is the boundary broken — and does a test catch it if
> the `auditMode` bool defaults to `false` on both paths?"

---

## Summary

| Severity | Count | All mitigated? |
|----------|-------|----------------|
| CRITICAL | 3     | 2 of 3 mitigated by existing tasks; **1 has no task closure** |
| HIGH     | 4     | 3 of 4 mitigated; **1 has no negative test row** |
| MEDIUM   | 3     | 2 of 3 mitigated; **1 partially mitigated only** |

**Two ship-blockers** (must each map to a negative test that goes RED when the mitigation is deleted):

- **CRIT-1** — S5 string-vs-tree fork: redaction policy never fires → PII leaks with no exception.
  Negative test: G-SEC.1 `Removing_the_policy_registration_makes_the_secret_leak`. **Mitigated by Task 4.**
- **CRIT-2** — S9 `auditMode` not threaded: `AuditTo.Sink(throwing)` silently swallows.
  Negative test: G-SEC.2 `WriteTo_swallows_sink_failure_AuditTo_propagates` (oppositional-pair).
  **Mitigated by Task 5.**

**Highest-severity unmitigated risk:** CRIT-3 — S5 bridge silent-degrade when P1 projector is not
publicly reachable (builder throws, but the throw path has no dedicated negative test).

---

## Risk Catalog

### CRIT-1: S5 string-vs-tree fork — redacting policy is silently no-op'd, PII leaks

**Description.**
Herald's native `IDestructuringPolicy.TryDestructure(object, out string?)` returns a **string**
(`src/Templating/IDestructuringPolicy.cs:12`). Serilog's `IDestructuringPolicy.TryDestructure(value,
factory, out LogEventPropertyValue tree)` returns a **tree**. They share the name but not the contract.

The silent failure: an adapter that registers the user's Serilog-shaped policy as a native
`IDestructuringPolicy` **compiles without error** but the tree return path is unreachable via the
native string interface — the redaction code inside the user's policy never executes. The event still
emits normally. No exception fires. A test that only asserts "the `Name` property is present in the
output" stays green. Meanwhile `password: hunter2` is present in every log entry flowing to the sink
and, if the sink is an AuditSink, to the compliance store.

This is not a hypothetical: the plan explicitly names this as a plausible implementation path
("if the bridge reuses the native string policy").

**Why invisible in a happy-path test.** The happy-path asserts `Name == "alice"`. The policy runs
(or doesn't); `Name` appears either way. The secret is never asserted.

**Mitigating task.** Task 4 — `SerilogDestructuringPolicyBridge`, G-SEC.1 full-output secret scan.

**Negative test required (ship-blocker):** `Removing_the_policy_registration_makes_the_secret_leak` —
wires the same logger WITHOUT `Destructure.With(...)` and asserts the scanner finds the secret. This
proves the positive test is not vacuously green. Task 4 Step 1 specifies this exact pair. Confirmed.

**Severity: CRITICAL.** PII leak + possible CVE under regulated data regimes.

---

### CRIT-2: S9 `auditMode` defaults to `false` on both `WriteTo` and `AuditTo` paths

**Description.**
The `AuditTo` compliance contract is: sink failure **throws** through to the caller. The `WriteTo`
contract is: sink failure is swallowed and reported via SelfLog. Both verbs wire through the same
`SerilogSinkAdapter`. If `auditMode` is never threaded from the `AuditTo` verb into the adapter — or
if `false` is hard-coded anywhere in the construction chain — `AuditTo.Sink(throwing)` silently
swallows every failure.

The dispatch trap (Pass 2): a developer who wires both:
```csharp
.WriteTo.Sink(mySink)
.AuditTo.Sink(myAuditSink)
```
expects distinct failure semantics. If both paths construct an adapter with `auditMode: false`, both
swallow. The distinction between the two nouns disappears entirely with no compile error, no runtime
exception, and no test failure on a happy path (where the sink doesn't throw).

**Why invisible in a happy-path test.** A happy-path test only verifies that the sink received the
event. It never triggers the failure branch. The `auditMode` bool is irrelevant until a sink throws.

**Mitigating task.** Task 5 — `AuditTo.Sink` verb + `auditMode` bool threading. G-SEC.2
oppositional-pair test.

**Negative test required (ship-blocker):** the oppositional pair in `AuditVsWriteFailureTests.cs` —
`WriteTo.Sink(throwing)` does NOT throw; `AuditTo.Sink(throwing)` DOES throw. Both assertions must
hold simultaneously in one `[Fact]`. Deleting the `AuditTo` verb (or hard-coding `auditMode:false`)
makes the second assertion fail. Task 5 Step 1 specifies this. Confirmed.

**Severity: CRITICAL.** Compliance audit-loss: a shop that uses `AuditTo` for regulatory logging
believes failures propagate; they don't. The audit trail is incomplete with no signal.

---

### CRIT-3: S5 bridge silent-degrade when P1 projector is not publicly reachable

**Description.**
Task 4 Step 3 specifies: "If P1 does not expose a public object-graph→tree projector, the bridge
throws at registration with a named, audited message — never silently degrades." This is the right
engineering choice. The gap: the **throw path** has no dedicated negative test.

If P1 does expose the projector, the bridge exercises it — the G-SEC.1 secret-scan test fires and
covers the path. But if P1's projector is *internal* or renamed and the bridge falls back to the
throw path, the only coverage is a `[Fact]` that asserts `Assert.Throws<InvalidOperationException>()`
when calling `Destructure.With(policy)`. That test does not exist in the current P4 plan.

The risk is not that the throw path produces a wrong log entry — it doesn't, it throws. The risk is
subtler: the throw path in Task 4 is described as the fallback, but there is no test that
independently verifies the bridge *correctly routes* to the throw path when the projector is absent.
A developer who accidentally wires to neither path (returns `false` from `TryDestructure` silently)
produces a no-op, not a throw. The G-SEC.1 test would not go RED on a silent-return-false path because
the test only verifies the *registered* case.

**Why invisible in current test plan.** G-SEC.1 tests the *registration-present* path. No test
verifies that an unbridgeable policy produces a named exception rather than a silent `false` return.

**Mitigating task.** NONE for the throw-path negative test. Task 4 Step 3 specifies the throw
behavior but does not add a test that verifies it.

**FLAG — NO MITIGATING TEST.** Required addition to Task 4:
```csharp
[Fact]
public void Destructure_With_unbridgeable_policy_throws_at_registration_not_silently_no_ops()
{
    // Simulate the "P1 projector absent" scenario by providing a policy whose
    // TryDestructure the bridge cannot satisfy.
    Assert.Throws<InvalidOperationException>(() =>
        new LoggerConfiguration()
            .Destructure.With(new AlwaysReturnFalsePolicy())   // never claims the type
            .CreateLogger());
    // OR: if the bridge design means "any registration attempt when P1 projector is
    // absent throws", verify that the throw message names the policy type.
}
```
The exact shape depends on the resolved OD (Task 4's open decision on whether P1 exposes a public
projector). Pin this test in Task 4 Step 3 alongside the implementation path choice.

**Severity: CRITICAL.** A no-op fallback on this path IS the CRIT-1 failure mode. Without this
test, CRIT-1's mitigation can silently regress.

---

### HIGH-1: S2 `destructureObjects:true` silently maps to flatten/ToString path

**Description.**
The `LogEventPropertyFactoryShim` bridges `ILogEventPropertyFactory.CreateProperty(name, value,
destructureObjects:true)` onto `context.AddProperty(name, value, LogPropertyCaptureMode.Destructure)`.
If the shim maps `destructureObjects:true` to `null` (the default capture mode) or to a stringify
path instead of `LogPropertyCaptureMode.Destructure`, a user enricher that does:
```csharp
factory.CreateProperty("Order", orderObj, destructureObjects: true)
```
silently emits `Order` as a flattened string (`"OrderRecord { ... }"`) rather than a
`StructureValue` tree. The `{@Order}` template hole renders the string. No exception. The happy-path
test only checks `Properties["Order"] != null` and passes.

**Concrete impact in a regulated shop:** an enricher that adds a `SecurityContext` object
(`{@SecurityContext}`) intending it to be a structured tree for log-analysis queries silently emits
a string blob. Downstream SIEM queries on `SecurityContext.UserId` find nothing.

**Mitigating task.** Task 3 Step 1 (b) — the destructure-routing test, explicitly:
`Assert.IsType<StructureValue>(orderProp)`. Specified in the plan.

**Negative test required:** the positive test itself IS the negative test here — if the shim routes
incorrectly the assertion fails (`DictionaryValue` or `ScalarValue` instead of `StructureValue`).
The test is already specified as `Enricher_CreateProperty_with_destructure_routes_to_tree_not_flatten`.
**Mitigated by Task 3.**

**Severity: HIGH.** Enricher-created `{@}` properties silently become unqueryable strings in any
structured log store.

---

### HIGH-2: S1 adapter hands the wrong mirror instance — stale/re-created, not Guard-2-pinned

**Description.**
P1's Guard 2 pins the tree projection to a single lazy call site so the hot path never instantiates
the mirror. The S1 adapter must hand the user's `ILogEventSink.Emit(LogEvent)` the **same** pinned
mirror — not a freshly constructed copy, not a separate projection of the same native event.

Two silent failure modes:

1. **Stale mirror:** the adapter constructs the mirror at sink-registration time, not at emit time.
   The mirror wraps a null or placeholder native event. User code reads a stale Level/Message.
   No exception; wrong data.

2. **Re-created mirror:** the adapter constructs a `new Serilog.Events.LogEvent(nativeEvent)` at each
   `Emit` call instead of passing P1's cached/lazy instance. This breaks the Guard-2 confinement
   claim (projection fires on every `Emit`, including native-path sinks that didn't request it)
   and the alloc-isolation test (G-HOT.1) goes RED — but only if the alloc test exists. If G-HOT.1
   is not yet written, the double-construction ships silently.

**Why invisible in a happy-path test.** A test that logs one event, checks one sink, and asserts
`Level == Information` passes regardless of whether the mirror was constructed once or N times.

**Mitigating task.** Task 7 — `SeamHotPathIsolationTests`, which asserts zero projection calls on
the native path and exactly 1 call on the custom-extension path. **Mitigated by Task 7 (the
call-count probe from Echo's Task 10 guidance), IF the positive-control assertion is included.**

**Conditional mitigation caveat:** Echo's Task 10 note is explicit: "pair the `== 0` native-path
assertion with a `== 1` custom-extension-path assertion." Without the positive control, a
counter that's always zero passes vacuously. Task 7 must include both probes.

**Severity: HIGH.** Double-projection perturbs the hot path for ALL users the moment any one consumer
wires a custom sink; the Guard-2 cost-confinement claim stops being true.

---

### HIGH-3: G-SEC.3 ordering — redaction must fire BEFORE AuditSink captures

**Description.**
If the fanOut dispatch order is not enforced, the `AuditSink` may capture the event before the
redacting `IDestructuringPolicy` runs its projection. Result: the audit compliance store holds the
unredacted secret (`password: hunter2`) while the write log carries the redacted form. From the
application's perspective, no exception fires, the write log looks clean, and the happy-path output
passes the SecretScanner. The audit store is the one that leaks — the one regulated shops treat as
the authoritative record.

The P4 plan acknowledges this as Task 5 Step 5 (G-SEC.3 ordering test). The test: wire both
`WriteTo` and `AuditTo` with the redaction policy active; assert the secret is absent from **both**
sinks' captured output.

**Mitigating task.** Task 5 Step 5 — G-SEC.3 ordering test. **Mitigated by Task 5, conditional on
the AuditSink being independently record-capable** (the `ThrowingSink` from Task 1 records received
events — the G-SEC.3 test must use a *recording* variant of the sink, not only the throwing variant,
so the assertion can read what the audit sink captured).

**Severity: HIGH.** Unredacted secret in the compliance audit store under a passing-test surface.

---

### HIGH-4: S1 `SelfLog` reporting on swallow path — no test that the swallow path reports

**Description.**
Task 5 Step 3 specifies: "the swallow path reports via SelfLog/health-report surface." The G-SEC.2
oppositional-pair test asserts `SelfLogCapture.HasReport` after `WriteTo.Sink(throwing)` does not
throw. This is correct and specified.

The gap: `SelfLogCapture` is not a standard Herald type — it must be wired in the test setup. If the
test scaffolding uses a `SelfLogCapture` stub that always returns `HasReport == true` (to avoid setup
complexity), the assertion is vacuously green. The swallow path could silently eat the failure with
no reporting and the test passes.

**Mitigating task.** Task 1 Step 1 (`ThrowingSink`) + Task 5 Step 1 (G-SEC.2). The plan specifies
`SelfLogCapture.HasReport` but does not specify the `SelfLogCapture` implementation. The Task 1
scaffolding note must include a real `SelfLog` output capture (not a mock that always returns true).

**FLAG — NO EXPLICIT TEST ROW FOR THE REPORT.** Required addition to Task 5 Step 1: after asserting
`write.Information("x")` does not throw, assert that `SelfLogCapture` received a message containing
the sink's exception message. This distinguishes "swallowed and silently lost" from "swallowed and
reported."

**Severity: HIGH.** A swallow path that reports nothing looks identical to one that reports correctly
on the G-SEC.2 happy face. The difference only surfaces during incident diagnosis.

---

### MED-1: S9 `auditMode` bool thread-safety — two concurrent audit loggers share one adapter instance

**Description.**
If `SerilogSinkAdapter` is constructed once and shared across multiple `LoggerConfiguration` builds
(e.g., a global singleton or a test fixture that reuses an adapter), the `auditMode` bool is a
mutable field that could race. In practice, the `LoggerConfiguration` builder pattern constructs
one adapter per logger — but if the adapter is accidentally registered as a shared provider (through
the `CustomSinkProvider` registration path, which holds a list of providers), two concurrent loggers
built from different configurations could hold the same adapter with conflicting `auditMode` values.

**Why lower priority.** The builder pattern makes this unlikely in production. In test infrastructure
it's more plausible (test fixtures that share a pre-constructed adapter). The fix is simple: make
`auditMode` readonly/init-only and set it at construction.

**Mitigating task.** Task 2/5 implementation — marked as LOW implementation discipline rather than
a test gap. The plan's C# coding rules (CODING_INSTRUCTIONS.md) require `readonly` fields and
`init`-only properties "when they improve immutability and clarity." Apply that to `auditMode`.

**Required addition (implementation note, not new task):** `SerilogSinkAdapter.auditMode` must be
`private readonly bool` set in the constructor. No setter. Task 2 Step 3 should call this out.

**Severity: MEDIUM.**

---

### MED-2: S2 enricher `ToJsonConfig` round-trip — stateful enricher silently loses its state on Reload

**Description.**
Native `ILogEnricher.ToJsonConfig()` defaults to `Kind`-only (`new(GetType().Name)`). A stateful
Serilog enricher wrapped by `SerilogEnricherAdapter` round-trips as a bare type name. When the
pipeline is rebuilt from the serialized JSON (e.g., on a live config reload), the enricher is
reconstructed as a default instance — identity tags, tenant context, customer routing keys, etc. are
silently dropped. Log calls still succeed; events still emit; the enricher appears to work. The
missing context only surfaces when downstream consumers query on the enriched field that is no longer
present.

The plan acknowledges this as a gap-to-pin (Task 3 Step 5). The task calls for a test that documents
the current behavior and marks the round-trip as a known gap.

**Mitigating task.** Task 3 Step 5 — pin-the-gap test. **Mitigated by acknowledging and documenting,
but NOT by fixing.** The pin test turns this from an invisible failure into a named, tracked gap.

**Severity: MEDIUM.** Silently breaks context-enriched pipelines on config reload. The naming and
pinning in Task 3 is the correct disposition for v1 — the risk is the gap goes unnamed.

---

### MED-3: S-NEW-1 by-name sink/enricher resolution absent from P4 — users in production configs hit a wall

**Description.**
Rosanne's seam inventory names S-NEW-1 as the highest-value customer risk: a shop whose
`appsettings.json` wires an in-house `AuditSink` or `PiiRedactingEnricher` **by name** has no
registration path. When they swap the package, Herald's settings parser fails loudly (correct — no
silent no-op). But the only escape is "fork the parser." There is no `LoggerSinkRegistry` or
`LoggerEnricherRegistry` with a `Register(name, factory)` path.

P4 is scoped to the four programmatic-API seams (S1/S2/S5/S9). S-NEW-1 belongs to the settings plan
(P5). The risk here is that P5's registry path is not named as a dependency gate for P4 — a user who
wires their `ILogEventSink` programmatically (S1, P4) and also wires it by name in `appsettings.json`
(S-NEW-1, P5) cannot close the migration without both plans.

**Mitigating task.** P5 (settings plan). **Not P4's scope, but the dependency is worth naming
explicitly** so the release gate (Task 8 P4 close) includes confirming P5's S-NEW-1 timeline.

**Severity: MEDIUM.** Not a P4 gap, but a gap between P4 and P5 that the P4 close task should
acknowledge. Add a note to Task 8 Step 4: "S-NEW-1 by-name resolution is P5. Do not mark the full
Serilog-compat initiative done until P5 ships S-NEW-1."

---

## Dispatch-Risk Summary (Pass 2 — `WriteTo` vs `AuditTo` boundary)

| Path | `auditMode` value | Failure behavior | Test that catches a wrong value |
|------|------------------|------------------|---------------------------------|
| `WriteTo.Sink(...)` | `false` (explicit) | swallow + SelfLog | G-SEC.2 second sub-assertion |
| `AuditTo.Sink(...)` | `true` (explicit) | re-throw | G-SEC.2 first sub-assertion |
| Default (unset) | `false` | swallow | G-SEC.2 second sub-assertion |

The boundary is broken when: (a) `AuditTo` constructs the adapter with `auditMode:false` (default),
or (b) the adapter's failure branch ignores `auditMode` and always swallows. The G-SEC.2 oppositional
pair catches BOTH because it asserts the throw **and** the no-throw in the same `[Fact]`. A
hard-coded `false` makes only the throw assertion fail; a hard-coded ignore makes both fail.

---

## Risks with NO Mitigating Test

| Risk | Severity | Required addition |
|------|----------|-------------------|
| CRIT-3: S5 throw-path not covered by a negative test | CRITICAL | Add `Destructure_With_unbridgeable_policy_throws_at_registration_not_silently_no_ops` to Task 4 Step 3 |
| HIGH-4: SelfLog report content not asserted | HIGH | Add a content-level assertion to G-SEC.2: `SelfLogCapture` message must contain the sink exception text, not just `HasReport == true` |

---

## Conditional Mitigations (Risk Survives if Condition Not Met)

| Risk | Condition for full mitigation |
|------|------------------------------|
| HIGH-2: Stale/re-created mirror | Task 7 must include the `ProjectionCount == 1` positive-control assertion alongside the `== 0` native-path assertion |
| HIGH-3: Redaction-before-audit ordering | G-SEC.3 test must use a recording variant of the AuditSink (not ThrowingSink only) to capture and assert on the audit sink's received event |
| MED-2: Stateful enricher round-trip | Task 3 pin-the-gap test must be committed and named; gap is known, not silently accepted |

---

## Open Decisions Requiring Resolution Before the Named Task Merges

| OD | Must resolve before | Risk if deferred |
|----|--------------------|--------------------|
| OD-P4-1: Does P1 expose a public object-graph→tree projector? | Task 4 (before Step 3) | S5 bridge shape is unknowable; wrong path chosen silently |
| OD-P4-2: Does `SerilogEnricherAdapter` emit `ToJsonConfig` for stateful enrichers? | Task 3 Step 5 | Reload-survival gap either ships unacknowledged or widens P4 scope |
| OD-P4-3: Is S-NEW-1 by-name resolution confirmed as P5 scope (not a P4 omission)? | Task 8 Step 4 | Release gate closes before the adoption-blocking wall is addressed |

---

## Relationship to Echo's G-SEC Requirements

| Echo requirement | Maps to | Status in P4 |
|-----------------|---------|-------------|
| G-SEC.1 — S5 redaction fires + SecretScanner walk | Task 4 Step 1 + `Removing_the_policy_registration` negative test | Mitigated (CRIT-1) |
| G-SEC.2 — S9 oppositional-pair audit-vs-write | Task 5 Step 1 | Mitigated (CRIT-2); HIGH-4 content-assertion gap noted |
| G-SEC.3 — redaction ordering before AuditSink | Task 5 Step 5 | Mitigated conditionally (HIGH-3); needs recording sink variant |

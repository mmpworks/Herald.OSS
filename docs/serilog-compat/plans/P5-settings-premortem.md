# P5 Settings Parser — the-fool Pre-mortem

- **Date:** 2026-05-30
- **Branch:** `feat/serilog-compat`
- **Framing:** "A reimplemented appsettings.json parser drives the Herald shim. Where does it
  silently produce a different logger than the same config on real Serilog, or silently drop a
  configured sink/enricher instead of failing loud?"
- **Method:** Pre-mortem + red-team hybrid (caller pre-defined deliverable).

---

## Steelmanned Thesis

The reimplemented parser is the right call. The strong-name identity wall makes referencing the
real `Serilog.Settings.Configuration` impossible; a clean reimplementation against the same JSON
schema is the only honest path. The design already anticipates the hardest failure: unresolved
names throw loud rather than silently producing a zero-sink logger. The registry seam (S-NEW-1)
lets in-house sinks resolve without forking. The eight named risks are real, enumerable, and
testable — which means they are fixable before a line of production code ships.

---

## Risk Register

> Severity key: **CRITICAL** = silent wrong behavior, data loss, or security regression.
> **HIGH** = silent divergence a developer would not notice until production.
> **MEDIUM** = wrong behavior observable with effort; less likely to reach production silently.

---

### Risk 1 — Typo'd `Name` in `WriteTo`: null/blank name not guarded

**Severity: CRITICAL**

Real Serilog throws `InvalidOperationException: Could not find a configuration method called '...'`
on a typo'd or blank sink name. The registry-miss path in the Herald parser throws on an
*unregistered* name — but there are two distinct code paths: (a) a non-empty name that is absent
from the registry, and (b) a `Name` key that is `null`, empty, or whitespace.

Path (b) reaches the registry lookup with an empty string. Depending on whether the
`OrdinalIgnoreCase` dictionary treats `""` as "not found" and whether the not-found branch throws
or returns, the parser could silently skip the entry and produce a logger with zero sinks — no
exception, no output.

**Mitigating task:** Task 4 (loud-named failure) + Task 3 (WriteTo parsing).

**Test goes RED if mitigation removed?** PARTIAL. Task 4's `ThirdPartySinkFailsLoudTests` covers
known third-party names (`Seq`, `MSSqlServer`, `Datadog`). It does NOT cover a `WriteTo` entry
with `Name = null` or `Name = ""`. The null/blank-name guard is a separate code path requiring its
own test row.

**Required test addition:** A `WriteTo` entry with `null` name and a `WriteTo` entry with empty
string name must each produce a loud, named exception — not a silent skip.

---

### Risk 2 — `MinimumLevel.Override` cross-plan gap: "loud or recorded" is underspecified

**Severity: CRITICAL**

If P2's `LoggerConfiguration` does not expose a per-source-context override entry point, the
parser cannot implement `Override` correctly. The P5 plan (Task 3, Step 1) acknowledges this as an
open decision and says to "pin a test asserting the override is not silently dropped (loud or
recorded)."

"Recorded" is the dangerous word. Logging a miss to `SelfLog` satisfies "recorded" while
remaining functionally silent to the developer. In production, `SelfLog` output is not wired by
default. A developer with `Override: { "Microsoft": "Warning" }` gets a logger that passes all
`Debug`-level Microsoft framework logs — wrong filtering, no exception, no visible signal.

**Mitigating task:** Task 3, but only if the cross-plan gap is resolved as "throw, not log." The
current plan leaves the resolution open.

**Test goes RED if mitigation removed?** NO. If the implementation logs to `SelfLog` and the test
only asserts the override was "not silently dropped" (no exception thrown), the test passes even
when filtering is wrong. The test must assert the correct filtering *outcome* (e.g., a logger
configured with `Override: { "Microsoft": "Warning" }` does not accept a `Debug` event from
source context `Microsoft.Extensions.Hosting`) — not just that no exception was thrown.

**Required action:** Resolve the P2 override API gap explicitly before Task 3 implementation.
If P2 lacks the API, it is a cross-plan blocker. The test must assert observable filtering
behavior, not merely absence of an exception.

---

### Risk 3 — Custom name bypasses collision check via empty-registry construction

**Severity: HIGH**

The collision check (throw-on-overwrite) guards against a user registering `"Console"` on an
instance that already has the built-in `"Console"`. This is correct.

The bypass: if `LoggerSinkRegistry` has a public parameterless constructor that creates an *empty*
registry (no pre-seeded built-ins), a user who constructs `new LoggerSinkRegistry()` and registers
`"Console"` with a custom factory sees no throw — the entry doesn't exist yet, so no collision.
They pass this instance to `ReadFrom.Configuration(config, customRegistry)`. The built-in Console
factory is absent because it was never seeded. Their custom factory fires; the pre-seeded Console
factory does not. No error, wrong behavior.

**Mitigating task:** Task 2 (registry implementation).

**Test goes RED if mitigation removed?** PARTIAL. Task 2's test suite covers `CreateDefault()` and
`Default`. It does not cover a user constructing `new LoggerSinkRegistry()` without pre-seeding
and then registering a name that shadows a built-in.

**Required action:** Either make `LoggerSinkRegistry` unsealed with a `protected` constructor
(preventing empty public construction), or add a test asserting that constructing without
`CreateDefault()` + registering a built-in name either pre-seeds automatically or documents that
built-ins are absent.

---

### Risk 4 — `Args` type coercion: typo'd level name silently ignored

**Severity: HIGH**

A typo in `"restrictedToMinimumLevel": "Wraning"` (Serilog display name, miscased) gets
case-folded to `"wraning"` — not a valid level key. If the sink factory passes the string through
to the underlying level parser and that parser returns "no minimum" on an unknown key (rather than
throwing), the sink silently accepts all events regardless of the configured restriction.

Real Serilog throws `ArgumentException: The value Wraning is not a valid log event level` at
configuration time. The Herald parser must validate level strings from `Args` *before* passing to
the sink factory — not after.

**Mitigating task:** Task 3 (parsing).

**Test goes RED if mitigation removed?** NO. The current Task 3 test fixtures cover correct level
names only. No test covers a typo'd level name in `Args` and expects a loud error.

**Required test addition:** A `WriteTo` entry with `Args: { "restrictedToMinimumLevel": "Wraning" }`
must produce a loud, named exception identifying the invalid level string.

---

### Risk 5 — Seq entry: loud-fail on every unresolved name (adequately covered)

**Severity: CRITICAL**

The registry lookup returns false for `"Seq"`. The plan requires the parser to throw a
`SinkResolutionException` with the sink name and the identity-wall reason. Task 4's test suite
covers `Seq`, `MSSqlServer`, and `Datadog` explicitly.

The residual risk: the implementation adds a `SelfLog`-only escape valve for unresolved names
rather than throwing — a defensive-programming instinct that the original Serilog parser does not
share. The plan should explicitly state: no `SelfLog`-only path for unresolved names is permitted;
the throw is mandatory.

**Mitigating task:** Task 4 — adequately covered *if* the throw path is the only path.

**Test goes RED if mitigation removed?** YES — provided no `SelfLog` escape valve is added.

**Required action:** Add a comment in `SinkResolutionException` and `SerilogConfigurationReader`
explicitly banning the `SelfLog`-only path for this case. The plan currently leaves it implicit.

---

### Risk 6 — `Using` + `WriteTo` coupling: compound case not tested

**Severity: HIGH**

The plan handles `Using` as advisory (skip assembly load, continue to `WriteTo`). The silent
divergence case: the parser reads `Using`, decides to skip the assembly load, and exits early
rather than continuing to the `WriteTo` section. This produces a logger with zero sinks and no
exception.

A config with `Using: ["Serilog.Sinks.Seq"]` + `WriteTo: [{ "Name": "Seq" }]` must produce a
`SinkResolutionException` on `Seq` — not a zero-sink silent success. The two sections must be
processed independently; `Using` skip must not short-circuit `WriteTo` resolution.

**Mitigating task:** Task 3 (parsing), Task 4 (loud-named failure).

**Test goes RED if mitigation removed?** NO. Task 4's test fixtures cover `WriteTo` with
unresolved names in isolation. No test covers the compound case where `Using` precedes `WriteTo`
in the same config. A parser that exits on `Using` would pass the Task 4 tests (which don't
include `Using`) and fail silently on real production configs.

**Required test addition:** A config with both `Using: ["Serilog.Sinks.Seq"]` and
`WriteTo: [{ "Name": "Seq" }]` must throw `SinkResolutionException` identifying `Seq`.

---

### Risk 7 — Dotted namespace keys in `Override`: silent truncation

**Severity: HIGH**

`IConfiguration` represents `"Override": { "Microsoft.Extensions": "Warning" }` as a single child
key `Microsoft.Extensions` (dot is not a path separator in this position). A parser that reads
override child keys via `GetSection("Override").GetChildren()` and takes each child's `Key`
verbatim handles this correctly.

The dangerous case: a parser that further splits override keys on `.` (treating them as nested
`IConfiguration` paths) tries `GetSection("Override:Microsoft:Extensions")` and reads nothing —
the dotted-key override is silently dropped.

`"Microsoft"` (no dot) and `"Microsoft.Extensions"` (dotted) are both valid Serilog override keys
and must both be applied. The current Task 3 test fixtures specify `{"Microsoft":"Warning",
"System":"Error"}` but do not include a dotted-key case.

**Mitigating task:** Task 3 (parsing).

**Test goes RED if mitigation removed?** NO. No test fixture uses a dotted override key.

**Required test addition:** An `Override` fixture with `"Microsoft.Extensions": "Warning"` must
apply the override at the correct granularity.

---

### Risk 8 — `MinimumLevel` string vs object: dual-form handling (adequately covered)

**Severity: CRITICAL**

`IConfiguration` represents `"MinimumLevel": "Debug"` as a leaf value
(`GetSection("Serilog:MinimumLevel").Value != null`) and `"MinimumLevel": { "Default": "Debug" }`
as a parent section with a `Default` child. A parser that only checks `Default` silently ignores
the string shorthand and applies the unconfigured default level.

**Mitigating task:** Task 3 (Step 1, first two test fixtures explicitly cover both forms).

**Test goes RED if mitigation removed?** YES — both forms are in the test plan. Adequate.

---

### Risk 9 — `WriteTo` entry with no `Name` key (distinct from Risk 1)

**Severity: HIGH** — No mitigating task in current plan.

A `WriteTo` array entry with only `Args` and no `Name` key at all (key absent, not blank):

```json
"WriteTo": [{ "Args": { "path": "log.txt" } }]
```

`GetSection("Name").Value` returns `null`. This is structurally different from Risk 1 (where
`Name` is present but typo'd). The parser must treat a completely absent `Name` as a loud error.
If it falls through to the registry lookup with a null key, the behavior is implementation-defined
and likely wrong.

Real Serilog throws on this malformed entry. The Herald parser must do the same.

**Mitigating task:** None in the current plan.

**Test goes RED if mitigation removed?** NO — no test covers this case.

**Required action:** Add a test for `WriteTo` with absent `Name` key. Add a guard in the parser
before the registry lookup: if `Name` is null or absent, throw with message identifying the
malformed `WriteTo` entry (include the array index for debuggability).

---

### Risk 10 — `Enrich` string shorthand silently ignored

**Severity: HIGH** — No mitigating task in current plan.

Serilog's `Enrich` array accepts two forms:
- Object form: `{ "Name": "WithMachineNameEnricher", "Args": {} }`
- String shorthand: `"FromLogContext"` (string element, not an object)

Nearly every real Serilog tutorial and production config uses:

```json
"Enrich": ["FromLogContext", "WithMachineName", "WithThreadId"]
```

A parser that iterates `GetSection("Serilog:Enrich").GetChildren()` and expects each child to have
a `Name` sub-key will either throw on a string element (wrong — it's a valid Serilog form) or
silently skip it (also wrong — it's intentional configuration).

Silently ignoring `FromLogContext` means ambient context enrichment — correlation IDs, request IDs,
scoped properties — stops working with no error. This is the single most common `Enrich` pattern
in production Serilog apps.

**Mitigating task:** None in the current plan.

**Test goes RED if mitigation removed?** NO — no test covers string-form enricher names.

**Required action:** Add string-shorthand support for `Enrich` entries: if the child section
has a `Value` (not children), treat it as a name-only enricher resolved via the enricher registry.
Add a test: `"Enrich": ["FromLogContext"]` must apply the matching enricher (or fail loud if
`FromLogContext` is unregistered — but it should be pre-seeded as a Herald built-in).

---

### Risk 11 — `Destructure` section silently ignored: PII regression risk

**Severity: MEDIUM** — No mitigating task in current plan.

Serilog's full schema includes a `Destructure` key. The Herald parser targets
`MinimumLevel`/`WriteTo`/`Enrich`/`Using` and has no `Destructure` handler. A config with:

```json
"Destructure": [{ "Name": "ToMaximumDepth", "Args": { "maximumDestructuringDepth": 3 } }]
```

is silently ignored. The developer gets different object representations in their logs.

The security case (from seam-inventory S5): a `Destructure` entry wiring a password-stripping
policy is silently ignored, and PII flows to the sink with no exception.

The correct behavior when encountering an unsupported top-level section is to throw or emit a loud
diagnostic — not silently accept a config that will produce wrong output.

**Mitigating task:** None in the current plan.

**Test goes RED if mitigation removed?** NO.

**Required action:** The parser must enumerate the sections it handles
(`MinimumLevel`, `WriteTo`, `Enrich`, `Using`) and throw or emit a loud, named diagnostic for any
unrecognized top-level key under `Serilog:`. This prevents silent config divergence from
unsupported sections without requiring implementations of those sections.

---

### Risk 12 — `Filter` section silently ignored: security-relevant

**Severity: MEDIUM** — No mitigating task in current plan.

Same structure as Risk 11. Serilog supports `"Filter": [{ "Name": "ByExcluding", "Args": {...} }]`.
A filter intended to drop sensitive log entries is silently no-op'd, producing a logger that is
more permissive than configured. No exception, no visible signal.

**Mitigating task:** None in the current plan. Addressed by the same unknown-section loud
diagnostic as Risk 11 — one fix covers both.

---

### Risk 13 — `WriteTo.Async` wrapper: inner sinks lost silently on loud error

**Severity: MEDIUM** — No mitigating task in current plan.

`"Name": "Async"` is unregistered and throws `SinkResolutionException` — correct behavior. But
the `configure` arg contains a nested `WriteTo` array with inner sinks that are also unresolved.
The error message for `Async` should note that inner sinks within the `configure` arg are also
unresolved, so the developer doesn't assume fixing the `Async` reference will restore both.

The failure is not silent (it throws), but the message quality misleads the developer about the
full scope of the resolution failure.

**Mitigating task:** None in the current plan. Low priority vs the CRITICAL/HIGH items.

---

### Risk 14 — `"Verbose"` / `"Trace"` aliasing: unknown level name behavior

**Severity: HIGH** — No mitigating task in current plan.

Real Serilog rejects `"Trace"` as an unknown level name and throws. A developer migrating an
`appsettings.json` from `Microsoft.Extensions.Logging` (which uses `Trace`) writes
`"MinimumLevel": "Trace"`. The Herald parser must throw on unrecognized level names — not silently
default to a different level or silently accept `Trace` as a synonym.

The level name validation must apply to: `MinimumLevel.Default`, `MinimumLevel.Override` values,
and `Args.restrictedToMinimumLevel`. All three paths must validate and throw on unknown names.

**Mitigating task:** None in the current plan for the explicit "unknown level name → throw" case.

**Test goes RED if mitigation removed?** NO.

**Required action:** Add a test: `"MinimumLevel": "Trace"` must throw a loud, named exception
identifying `Trace` as an unrecognized Serilog level name (not a silent default).

---

### Risk 15 — `WriteTo` ordering with 10+ sinks: key sort order

**Severity: MEDIUM** — No mitigating task in current plan.

`IConfiguration` array children have string keys `"0"`, `"1"`, `"2"`, ... `"10"`. String sort
puts `"10"` before `"2"`. If the parser iterates children in `GetChildren()` order without
converting keys to integer for sort, a config with 10+ sinks applies them in wrong order
(`0, 1, 10, 2, 3...`).

Real production apps with 10+ sinks are rare but the failure is silent and order-dependent.

**Mitigating task:** None in the current plan.

**Required action:** When iterating `WriteTo` children, sort by integer value of the child `Key`,
not lexicographic string order. One line; prevents a latent bug in large sink configs.

---

## Summary Table

| # | Risk | Severity | Mitigating Task | Test RED if Removed? |
|---|------|----------|-----------------|----------------------|
| 1 | Typo'd Name (null/blank case) | CRITICAL | Task 3 + 4 | PARTIAL — null/blank case missing |
| 2 | Override cross-plan gap underspecified | CRITICAL | Task 3 (open) | NO — outcome not asserted |
| 3 | Custom name bypasses collision via empty ctor | HIGH | Task 2 | PARTIAL — empty-ctor path not tested |
| 4 | Args level name typo silently ignored | HIGH | Task 3 | NO — no typo'd level test |
| 5 | Seq loud-fail adequately covered | CRITICAL | Task 4 | YES |
| 6 | Using + WriteTo compound case not tested | HIGH | Task 3 + 4 | NO — compound case missing |
| 7 | Dotted namespace Override key silently dropped | HIGH | Task 3 | NO — no dotted-key fixture |
| 8 | MinimumLevel dual-form adequately covered | CRITICAL | Task 3 | YES |
| 9 | WriteTo missing Name key (no task) | HIGH | **None** | NO |
| 10 | Enrich string shorthand silently ignored (no task) | HIGH | **None** | NO |
| 11 | Destructure section silently ignored (no task) | MEDIUM | **None** | NO |
| 12 | Filter section silently ignored (no task) | MEDIUM | **None** | NO |
| 13 | WriteTo.Async inner sinks: message quality (no task) | MEDIUM | **None** | NO |
| 14 | Unknown level name "Trace" silently defaulted (no task) | HIGH | **None** | NO |
| 15 | WriteTo ordering with 10+ sinks (no task) | MEDIUM | **None** | NO |

**Risks with no mitigating task:** 9, 10, 11, 12, 13, 14, 15 (7 risks).

**Risks with inadequate tests:** 1 (null/blank), 2 (outcome unasserted), 3 (empty-ctor path),
4 (typo'd level), 6 (compound case), 7 (dotted key).

---

## Required Plan Additions

The following gaps must be addressed before Task 1 proceeds.

### Add to Task 3 test fixtures
- `WriteTo` with `Name = null` (key present, null value) → loud throw
- `WriteTo` with `Name = ""` (blank) → loud throw
- `WriteTo` entry with no `Name` key at all (Risk 9) → loud throw with array index in message
- `Args: { "restrictedToMinimumLevel": "Wraning" }` (typo'd level) → loud throw
- `Override: { "Microsoft.Extensions": "Warning" }` (dotted key) → override applied
- `Enrich: ["FromLogContext"]` (string shorthand) → enricher applied (Risk 10)
- `Using: ["Serilog.Sinks.Seq"]` + `WriteTo: [{ "Name": "Seq" }]` (compound) → `SinkResolutionException`
- `"MinimumLevel": "Trace"` (unknown level name) → loud throw (Risk 14)

### Add to Task 4
- Explicit prohibition on `SelfLog`-only path for unresolved names (enforce via test: if the
  registry-miss path logs to SelfLog instead of throwing, an unresolved-name test must fail)

### Add to Task 3 parser design note
- The parser must enumerate the top-level Serilog section keys it handles and throw a loud,
  named diagnostic for any unrecognized key (catches Risk 11, 12 — `Destructure`, `Filter`
  sections silently dropped)
- `GetChildren()` on `WriteTo` must sort children by integer key value before iteration (Risk 15)

### Resolve before Task 3 implementation
- **Cross-plan blocker (Risk 2):** confirm whether P2 `LoggerConfiguration` exposes a
  per-source-context override API. If it does not, this is a cross-plan dependency that must be
  tracked as a separate task, not as an internal P5 decision. The test for Override must assert
  observable filtering behavior (a log event at a filtered level is not accepted), not merely that
  no exception was thrown.

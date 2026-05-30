# P3 Output-Template Grammar — the-fool Pre-Mortem

> Generated: 2026-05-30. Input: the-fool skill, pre-mortem mode.
> Framing: "Herald already ships a native `OutputTemplateFormatter`. We add a parallel Serilog grammar
> in the compat assembly. A user configuring
> `"[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}"` must get Serilog-identical
> output. Where does a user silently get wrong output?"

---

## Summary

| Severity | Count | All mitigated? |
|----------|-------|----------------|
| CRITICAL | 4     | 3 of 4 mitigated by existing tasks; **1 has no task closure** |
| HIGH     | 3     | 1 mitigated; **2 have no explicit test rows** |
| MEDIUM   | 1     | Partially — OD-3 open decision exists but no non-UTC test |

**Highest-severity unmitigated risk:** CRIT-4 — Two-formatter dispatch for sinks other than `WriteTo.Console`.

---

## Risk Catalog

### CRIT-1: `{Level:u3}` — `Warning` abbreviates to `WRN` not `WAR`; digit-on-`w` silently truncates

**Description.** The six canonical Serilog abbreviations are well-known but the failure mode is
invisible on a happy-path test that only uses `Information` and `Error`:

- `Warning` must map to `WRN`. A developer writing a table by hand commonly writes `WAR` (plausible,
  three letters from "Warning"). No compile error. No runtime error.
- `{Level:w3}` — Serilog ignores the digit on the lowercase `w` specifier; it always renders the full
  lowercase name. An implementation that applies the digit width uniformly silently truncates:
  `war` instead of `warning`.
- `{Level:u}` (no digit) must render the full uppercase name (`INFORMATION`). If width-parsing assumes
  a digit follows `u`, the no-digit case falls through to a default and renders wrong silently.

**Why invisible in a happy-path test.** The test corpus uses `Information` and `Error`. Both are
unambiguous. `Warning`'s trap only surfaces when `Warning` is in the corpus. The `w`-with-digit
truncation only surfaces when that exact specifier is tested.

**Mitigating task.** Task 3 — oracle-pinned level table. **Mitigated *if* the oracle corpus
explicitly exercises `Warning`, `{Level:w}`, `{Level:w3}`, and `{Level:u}` (no digit).** The plan's
`[InlineData]` table currently lists those cases. Confirm they ship in the final test file.

---

### CRIT-2: `{Level:u3}` — Herald extra levels (Notice/Success/Security/Metric) have no oracle row

**Description.** Serilog has no abbreviation for Herald's four extra levels. Any fallback table is a
guess. The silent failure: the test suite only exercises the six Serilog levels, so the fallback is
never run — and it ships with a wrong 3-char truncation (`NOT`, `SUC`) or worse, an
`IndexOutOfRangeException` on a misaligned substring in production when a `Notice`-level event fires.

**Why invisible in a happy-path test.** Happy-path emits only the Serilog-level corpus. The extras are
never touched.

**Mitigating task.** Task 3 — `SerilogLevelMoniker` must pin the fallback as a hard constant with a
dedicated `[Fact]` that asserts the constant never changes (named divergence, no oracle row). **Task 3
exists and calls this out. Mitigated *if* the pin test is committed alongside the moniker table.**

---

### CRIT-3: `{Message:lj}` compound specifier silently falls to default or `:l` only

**Description.** `lj` is a two-character compound: `l` (render string values without surrounding
quotes) combined with `j` (render structured/destructured values as JSON). Three silent failure modes:

1. `lj` treated as unknown → default rendering. String values get quotes: `"hello"` instead of
   `hello`. Looks reasonable. Only surfaces with a string-valued property.
2. `lj` decoded as `l` only. Strings render correctly but a `{@user}` destructured value renders
   `UserRecord { Name=Alice }` instead of `{"Name":"Alice"}`. Only surfaces with a destructured
   property.
3. `lj` aliased to `:j`. Strings gain incorrect quotes. Only surfaces with a string-valued property.

In all cases: `logger.Information("hello {X}", 1)` (integer property) produces identical output under
`:l`, `:j`, `:lj`, and default. The divergence is completely invisible with a scalar-only corpus.

**Why invisible in a happy-path test.** The canonical happy-path uses integer properties. All four
rendering branches produce `1` for an integer.

**Mitigating task.** Task 4 — token render tests include `/* event with string + int props */`. **The
test fixture MUST include: (a) a string-valued property and (b) a destructured object (`{@user}`).
Without both, the `lj` compound specifier's JSON branch and the no-quotes branch are untested.** This
is a conditional mitigation — the risk survives Task 4 if the fixture uses only scalar properties.

Flag: add a note to Task 4 Step 1 requiring `{@user}` in `CanonicalEventSpec.RepresentativeCorpus()`.

---

### CRIT-4: Two-formatter dispatch trap — `WriteTo.*` verbs other than Console use native grammar

**Description.** `WriteTo.Console(outputTemplate: "...")` is closed by Task 9 (S3 seam): the compat
verb constructs a `MessageTemplateTextFormatter` wrapping `SerilogOutputTemplateRenderer`. But every
other `WriteTo.*` verb that accepts an `outputTemplate` parameter has the same trap. If `WriteTo.File`,
`WriteTo.Seq`, or any future compat verb resolves `outputTemplate` through a shared Herald factory
that constructs the native `OutputTemplateFormatter`, the user gets:

- `{Level:u3}` → `Information` (format specifier ignored; native handler discards `token.Format`)
- `{Message:lj}` → pre-rendered string (specifier ignored)
- `{Properties}` → all properties, no residual filtering
- `{Exception}` → no trailing newline

Every token looks "mostly right." No error fires. A developer glancing at the console output sees
`Information` in the level column, shrugs, and ships.

**The dispatch question (OD-2).** The P3 plan acknowledges OD-2 and defers it to Task 9. But Task 9
covers only `WriteTo.Console`. There is no task that (a) prevents the native formatter from being
constructed via any Serilog-compat configuration path, or (b) extends the dispatch guard to verbs
added after Task 9.

**Why invisible in tests.** An integration test that renders through the full pipeline and checks for
"mostly right" output will pass even on the native formatter — because the output IS mostly right
except for specifier semantics.

**Mitigating task.** NONE for the multi-verb case. Task 9 closes `WriteTo.Console` only.

**FLAG — NO MITIGATING TASK.** Required additions:

1. An architecture test (fits Task 10 or Task 11) asserting that no Serilog-compat configuration path
   constructs `MMP.Herald.Formatting.OutputTemplateFormatter` directly — the compat verbs must always
   go through `SerilogOutputTemplateRenderer`.
2. A factory/construction guard: the Serilog-compat verb surface must own all `outputTemplate`
   construction paths. Any new `WriteTo.*` verb that accepts `outputTemplate` must be plumbed through
   `MessageTemplateTextFormatter`, not through a shared Herald formatter factory.
3. Task 9's integration test should assert the formatter *type* actually instantiated (not just that
   the output looks right).

---

### HIGH-1: `{Properties}` residual selector excludes output-template holes but misses message-template holes

**Description.** Serilog's residual rule: exclude properties named in the output template AND properties
named in the message template. A naïve implementation excludes only output-template holes.

Template: `"[{Level:u3}] {Message:lj} {Properties}"`, message: `"User {UserId} logged in"`, event
carries `UserId=42, RequestId="abc"`.

- Serilog: `{ RequestId: "abc" }` (UserId excluded via message-template hole)
- Naïve: `{ UserId: 42, RequestId: "abc" }` (UserId double-rendered)

The failure is silent and plausible. A user watching the log sees redundant `UserId` in `{Properties}`
and may not recognise it as wrong.

**Mitigating task.** Task 5 — `ResidualPropertySelector`. The plan's test case covers this (`UserId` in
the message template). **Mitigated *if* `ISerilogEventView` exposes the parsed message-template hole
names (not just the rendered message string), so the residual selector can intersect both sets.**
If `MessageTemplate` is a raw string, the selector must parse it — introducing a second parser that
could diverge. Confirm `ISerilogEventView.MessageTemplate` exposes hole names directly (or a parsed
form), not just the raw string.

---

### HIGH-2: Alignment edge cases — over-width, zero-width, sign direction — no explicit test rows

**Description.** Serilog pads; it never truncates. Edge cases:

- **Over-width:** `{Level,-11}` with value `Information` (11 chars) = `Information` (no change, no
  truncation). A developer who reads "width" and implements truncation produces `Informa` for width 7.
  Silent. Plausible.
- **Zero-width:** `{Level,0:u3}` — Serilog treats 0 as "no alignment," renders without padding. An
  implementation that calls `string.PadLeft(0)` returns the string unchanged (correct by accident). An
  implementation that branches on zero and returns empty string is wrong.
- **Sign direction:** negative = left-align (pad on right); positive = right-align (pad on left). This
  is standard .NET composite formatting, but a developer implementing from scratch commonly inverts it.
- **Apply order:** render-with-format first, then pad-to-width. If padding is applied to the raw
  property name and format is applied after, the width is wrong for formatted values.

**Why invisible in the current plan's test table.** The G-GAP.1 suite in Task 6 uses `{Level,-11}`
(left-pad, exact-width). It does not cover over-width, zero-width, or right-align (positive sign).

**Mitigating task.** Task 6 covers alignment generally. **FLAG — NO EXPLICIT TEST ROWS** for the three
sub-cases above. Add to Task 6 Step 1:

```csharp
[InlineData("{Level,7:u3}",   "Information", "    INF")]   // right-align, 7 wide
[InlineData("{Level,-7:u3}",  "Information", "INF    ")]   // left-align, 7 wide
[InlineData("{Level,-3:u3}",  "Information", "INF")]       // exact width, no pad
[InlineData("{Level,-2:u3}",  "Information", "INF")]       // over-width: NO truncation
[InlineData("{Level,0:u3}",   "Information", "INF")]       // zero-width: no padding
```

Each row verified against `SerilogParityOracle`.

---

### HIGH-3: `{Exception}` trailing-newline rule — present-case must be in the oracle corpus

**Description.** Serilog emits the exception's `ToString()` output followed by a trailing `\n`.
Exception-absent emits nothing (zero bytes). The native Herald `AppendException` does the right thing
for the absent case but produces exception text without a trailing newline in the present case.

For the template `{Message:lj}{NewLine}{Exception}` with an exception:

- Serilog: `hello\r\nSystem.Exception: oops\n   at Main()\n`  (trailing `\n` after stack trace)
- Herald (if trailing newline missing): `hello\r\nSystem.Exception: oops\n   at Main()`

In a log stream this means Serilog produces a blank separator line between exception-carrying entries
(the `{NewLine}` token + the exception's trailing newline). Herald produces no blank line. The
difference is visible in a terminal but a developer may dismiss it as "formatting preference" rather
than a parity failure.

**Mitigating task.** Task 4 — token render tests include `{Exception}` present and absent. **Mitigated
*if* the test corpus includes an event with a real exception with a real stack trace, and the assertion
is exact string equality against the oracle (not `Contains`).** The present-case row must fire.

---

### MED-1: `{Timestamp}` UTC vs local — diverges in non-UTC timezones; CI always passes

**Description.** Serilog stores `DateTimeOffset` in local time. Herald stores `TimeUtc` as UTC
`DateTime`. For `{Timestamp:HH:mm:ss}` on a machine at UTC-5:

- Serilog: `14:30:00` (local)
- Herald: `19:30:00` (UTC)

CI runs in UTC. All tests pass regardless of implementation. A user in Tokyo ships wrong timestamps
from day one.

The `:o` round-trip format compounds this: Serilog emits `2024-01-15T14:30:00.000-05:00` (local
offset). Herald emits `2024-01-15T19:30:00.0000000Z` (UTC). Log aggregators (Seq, Elastic, Splunk)
parse the offset for time correlation — a UTC-vs-local divergence breaks cross-service correlation for
non-UTC users.

**OD-3 is the right flag.** The decision (pin UTC or honour local) must be made before Task 4 merges,
not deferred to Task 11. Whichever is chosen, it must be a named divergence with a doc link.

**Mitigating task.** Task 4 Step 3 flags OD-3. **FLAG — NO NON-UTC TEST.** Required addition:

A test that constructs a `DateTimeOffset` with a non-zero offset (e.g., `+09:00`) and asserts the
rendered timestamp matches Serilog's oracle output for that offset. Without this, CI always passes
even if the implementation is wrong for every non-UTC user.

---

## Dispatch-Risk Summary (OD-2)

| Verb | Task that closes the dispatch | Status |
|------|------------------------------|--------|
| `WriteTo.Console(outputTemplate:)` | Task 9 | Planned |
| `WriteTo.Console(ITextFormatter)` | Task 9 | Planned |
| `WriteTo.File(outputTemplate:)` | None | **UNMITIGATED** |
| Any future `WriteTo.*` verb | None | **STRUCTURAL GAP** |

The structural fix: the compat verb surface must never delegate `outputTemplate` construction to a
shared Herald formatter factory. Add an architecture test (Task 10 or Task 11) that asserts the
`MMP.Herald.Formatting.OutputTemplateFormatter` type is not referenced from any compat
`WriteTo.*` verb implementation.

---

## Risks with NO Mitigating Task

| Risk | Severity | Required addition |
|------|----------|-------------------|
| CRIT-4: Multi-verb dispatch guard | CRITICAL | Architecture test + construction guard for all `WriteTo.*` verbs |
| HIGH-2: Alignment over-width, zero-width, sign direction | HIGH | Add 5 explicit `[InlineData]` rows to Task 6 Step 1 |
| MED-1: Non-UTC timestamp test | MEDIUM | Add a non-UTC offset fixture to Task 4 |

---

## Conditional Mitigations (Risk Survives if Condition Not Met)

| Risk | Condition for full mitigation |
|------|------------------------------|
| CRIT-1: `{Level:w3}` digit-on-w | Oracle corpus must include `{Level:w}`, `{Level:w3}`, `{Level:u}` (no digit) |
| CRIT-3: `{Message:lj}` compound | `CanonicalEventSpec.RepresentativeCorpus()` must include a string-valued property AND a `{@user}` destructured object |
| HIGH-1: `{Properties}` message-template exclusion | `ISerilogEventView.MessageTemplate` must expose parsed hole names, not a raw string |
| HIGH-3: `{Exception}` trailing newline | Oracle corpus must include an event with a real exception and stack trace; assertion must be exact equality |

---

## Open Decisions Requiring Resolution Before the Named Task Merges

| OD | Must resolve before | Risk if deferred |
|----|--------------------|--------------------|
| OD-2: Dispatch — which formatter for which verb | Task 9 | Every non-Console verb silently uses native grammar |
| OD-3: UTC vs local timestamp convention | Task 4 | Non-UTC users get wrong timestamps from day one |
| OD-4: CLEF "byte-parity" resolved as field/value parity | Task 8 | Test assertion written against wrong contract |

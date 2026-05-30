# P8 — Parity Audit + Migration Docs Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. **This plan produces documentation, not code** — but every task still has exact file paths, a section outline, and a verification step.

**Goal:** Write the three consumer-facing documentation deliverables for the Serilog-compat initiative — the friction-mapped parity audit, the consumer "how to" README with per-gap migration runbooks, and the honest-claim marketing copy stub — so a Serilog user can decide whether Herald drops in, swap the package, and migrate each gap with a step-by-step path instead of a fork.

**Architecture:** Three docs, layered. `parity-audit.md` is the engineering reference — every Serilog surface tagged *carries-over* / *maps-to-Herald-equivalent* / *hard-wall*, ranked by how much of the real Serilog base each gap blocks, carrying Jared's verbatim third-party-sink statement. `README.md` is the consumer guide — the honest claim, the swap-and-rebuild steps, the Layer-1-stage-then-Layer-2-cutover runbook, and a **per-gap migration table** whose rows link to companion docs under `docs/serilog-compat/migrations/`. The `honest-claim.md` stub is the single source of the headline wording, handed to Dawn for the website. Every gap named in `test-inventory.md` gets a matching migration entry; every hard wall references its loud-fail regression test by ID.

**Tech Stack:** Markdown only. Plainspoken-engineer voice per `CLAUDE.md` writing-voice + the `mmpworks-writing-voice` skill. No code, no benchmarks run in this plan — but the docs **quote** numbers and behaviour produced by P1–P7.

**Sequencing — drafted now, finalized last.** P8 runs alongside P0–P7 (the roadmap shows it parallel). The structure, outlines, gap tables, and migration runbooks are drafted now against the design docs. The **numbers and observed behaviour are filled in only after P1–P7 land** — allocation rows, the request-log line shape, the exact loud-fail error text, the corpus pass/fail counts. A doc that quotes a benchmark before the benchmark exists is a lie waiting to ship. Each task below flags which fields are "draft now" vs "fill after P-n."

**Documentation Owner consult (Heather) — required, not optional.** Per the scope PRD §"The parity audit (#4 deliverable)", the per-gap migration plans are produced *in consult with Documentation Owners*. Heather owns Herald docs structure (docs-as-database, markdown+frontmatter for prose, CUPID/DRY promotion). Three explicit touchpoints are called out below: **(T-H1)** structure sign-off before drafting, **(T-H2)** migration-table-vs-companion-doc split decision, **(T-H3)** final prose + dual-register review before the docs are marked done.

**Honest-claim wording (binding — from scope PRD §Design-round outcomes).** The only sanctioned headline is:

> *"Swap the package, rebuild, and your standard Serilog code runs on Herald — popular sinks map over; the third-party-sink ecosystem and the expression DSL are a documented boundary."*

Never "1-to-1", never "binary drop-in", never "100% compatible". Mirrored types give **source compatibility on recompile, not binary identity** — we do not spoof Serilog's strong-name key. Every doc in P8 holds this line.

---

### Task 0: Heather structure consult + scaffold (T-H1) — no prose yet

**Files:**
- Create: `docs/serilog-compat/migrations/` (empty dir + `.gitkeep`)
- Create: `docs/serilog-compat/parity-audit.md` (skeleton — headings only)
- Create: `docs/serilog-compat/README.md` (skeleton — headings only)
- Create: `docs/serilog-compat/honest-claim.md` (skeleton — headings only)

- [ ] **Step 1 (T-H1 — Heather consult):** Surface the three skeletons + the proposed migration-companion split to Heather (Documentation Owner). Ask three things: (a) does the `parity-audit.md` / `README.md` / `migrations/*.md` split match the Herald docs-as-database model, (b) frontmatter schema for the migration companion docs (each gap = one record: `gap-id`, `serilog-surface`, `herald-status`, `population-rank`, `regression-test-id`), (c) whether the honest-claim stub lives here or in a shared copy-deck Dawn already owns. Capture her answers inline at the top of each skeleton as a `<!-- Heather T-H1: ... -->` note. **Gate:** no prose is written until T-H1 lands — a structure rework after drafting is the expensive mistake.

- [ ] **Step 2:** Write the heading-only skeletons (outlines below in Tasks 1–3). No body text yet.

- [ ] **Step 3: Verify** the three files exist with their full heading tree and the `migrations/` dir is present.

```bash
cd E:/dev/Herald.OSS
ls docs/serilog-compat/parity-audit.md docs/serilog-compat/README.md docs/serilog-compat/honest-claim.md docs/serilog-compat/migrations/.gitkeep
grep -c "^#" docs/serilog-compat/parity-audit.md docs/serilog-compat/README.md docs/serilog-compat/honest-claim.md
```
Expected: all four paths exist; each doc shows its full heading count.

- [ ] **Step 4: Commit.**

```bash
git add docs/serilog-compat/parity-audit.md docs/serilog-compat/README.md docs/serilog-compat/honest-claim.md docs/serilog-compat/migrations/.gitkeep
git commit -m "docs(serilog-compat): P8 doc skeletons + Heather structure consult (T-H1)"
```

---

### Task 1: Parity audit — the friction map (`parity-audit.md`)

The engineering reference. Every Serilog surface tagged and ranked by installed-base impact. Carries Jared's verbatim third-party-sink statement.

**Files:**
- Read first: `docs/serilog-compat/seam-inventory.md` (the five seams + hard walls), `docs/serilog-compat/design-round-jared.md` (the verbatim text at §"Parity-audit text — third-party sinks", lines ~35–37), `docs/serilog-compat/2026-05-29-scope-prd.md` (§In scope / §Out of scope).
- Write: `docs/serilog-compat/parity-audit.md`

**Section outline:**
1. **What this is** — a friction map, not a defect list. Ranked by how many real Serilog customers each gap blocks; highest-population blocker named first; keeps the marketing claim honest.
2. **How to read it** — the three tags: `carries-over` (source-compatible on recompile), `maps-to-Herald-equivalent` (different name/shape, same behaviour, documented), `hard-wall` (structural, no drop-in path). Plus the population-rank column.
3. **The honest claim** (quote `honest-claim.md` — single source; do not re-author here).
4. **Friction map table** — one row per Serilog surface, columns: `Serilog surface | Tag | Herald equivalent / boundary | Population rank | Migration entry | Regression test`. Rows to include (from the design docs):
   - Instance `ILogger` verbs, static `Log` facade, message templates, `{@}`/`{$}`, level map, `ForContext`/`PushProperty`, value model, `LoggerConfiguration` code config, `appsettings.json` (`ReadFrom.Configuration`), ASP.NET `UseSerilog`/`AddSerilog`/`UseSerilogRequestLogging` → **carries-over / maps-to-equivalent**.
   - Popular sinks (Console/File/Elasticsearch/OTLP/HTTP/TCP/UDP/Null) → **maps-to-equivalent**.
   - Custom user-authored sink (S1), custom enricher (S2), custom destructuring policy (S5), `AuditTo` vs `WriteTo` (S9), sink/enricher-by-name in config (S-NEW-1), custom formatter/CLEF (S3), sub-loggers (S6), `LoggingLevelSwitch` (S4), `SelfLog` (S7) → tag each per seam-inventory's verdict.
   - **Hard walls:** pre-compiled community sinks (Seq/MSSql/Datadog/long tail), `Serilog.Expressions` string DSL → **hard-wall**.
5. **Third-party sinks — the identity wall** — **paste Jared's verbatim block** from `design-round-jared.md` §"Parity-audit text — third-party sinks (drop in verbatim)". Do not paraphrase; the wall is the precise legal/technical statement (`PublicKeyToken=24c2f752a8e58a10`, unsigned shim, CS0433/InvalidCastException, "will not spoof a signing key we do not have").
6. **`Serilog.Expressions` DSL — the second wall** — predicate `Filter.ByExcluding(...)` maps to processors; the string-DSL form does not. Named as an open RFC to the OSS community (per the open-source-dilemma rule).
7. **Population-rank rationale** — one paragraph per "high" rank explaining why it blocks the most users (e.g., `appsettings.json`-configured apps are a large share of production Serilog; sink-by-name in config is day-one friction for any shop with an in-house sink).

- [ ] **Step 1 (draft now):** Write sections 1–7 with every row's tag and the verbatim wall text. Tags come from the design docs and do **not** depend on P1–P7.
- [ ] **Step 2 (fill after P5/P6/P7):** Backfill the exact loud-fail error text (after P5 ships the named throw), the request-log line field list (after P6), and the CS0433 coexistence wording (after P7). Mark each such cell `<!-- FILL AFTER P5 -->` etc. until then.
- [ ] **Step 3 — population ranking:** Order the table so the highest-population blocker is first. Cross-check the ranking against the seam-inventory pre-mortem (§"Pre-mortem") — S-NEW-1 and S5 are called out as landing on the highest-value customer; they rank high.

- [ ] **Step 4: Verify — every hard wall references a loud-fail test.** Each `hard-wall` row's "Regression test" cell must name a real test ID from `test-inventory.md`.

```bash
cd E:/dev/Herald.OSS
# Every hard-wall row must cite G-SINK-WALL.1 or G-GAP.2 (the loud-fail suites).
grep -n "hard-wall" docs/serilog-compat/parity-audit.md
grep -nE "G-SINK-WALL\.1|G-GAP\.2" docs/serilog-compat/parity-audit.md
# The verbatim wall text must be present.
grep -n "24c2f752a8e58a10" docs/serilog-compat/parity-audit.md
```
Expected: every hard-wall line has a sibling test reference; the PublicKeyToken string is present (proves the verbatim block landed).

- [ ] **Step 5: Verify — honest claim only.** No banned wording.

```bash
grep -niE "1-to-1|binary drop-in|100% compatible|fully compatible|drop-in replacement" docs/serilog-compat/parity-audit.md || echo "clean"
```
Expected: `clean`.

- [ ] **Step 6: Commit.**

```bash
git add docs/serilog-compat/parity-audit.md
git commit -m "docs(serilog-compat): parity audit friction map + verbatim third-party-sink wall"
```

---

### Task 2: Consumer README + per-gap migration companions (`README.md` + `migrations/*.md`)

The "how to" guide a Serilog user reads to migrate. The honest claim, the swap-and-rebuild steps, the Layer-1-stage-then-Layer-2-cutover runbook, and the per-gap migration table linking to companion docs.

**Files:**
- Read first: `docs/serilog-compat/2026-05-29-scope-prd.md` (§"What we are building" — the two-layer model; §"The parity audit" — the migration-plan addition), `docs/serilog-compat/seam-inventory.md` (per-gap migration substance).
- Write: `docs/serilog-compat/README.md`
- Write: `docs/serilog-compat/migrations/<gap-id>.md` (one per practical gap — see table)

**README section outline:**
1. **The honest claim** (quote `honest-claim.md`).
2. **Is Herald a drop-in for you?** — a 4-line decision check: do you use only popular sinks? only the call surface + `appsettings.json` + ASP.NET wiring? no `Serilog.Expressions` DSL? no pre-compiled community sink? If yes to all → pure swap. If no → see your gap's migration companion.
3. **Layer 1 vs Layer 2** — what each is, why two layers exist (Layer 1 = `MMP.Herald.Serilog.*`, one `using` change, **coexists with real Serilog** so you can stage and verify; Layer 2 = `Serilog.*` shim, swap the package reference, change nothing, but **must be the only Serilog in the graph**).
4. **Swap-and-rebuild — the fast path** (Layer 2): swap the package reference, rebuild on net9/net10, run the corpus check. The literal zero-code-change path.
5. **The staged migration runbook** (Layer 1 → Layer 2 cutover) — the safe path for a shop that wants to verify before cutover:
   - Stage 1: add `MMP.Herald.Serilog.*` alongside real Serilog; change one global `using`; rebuild; both coexist.
   - Stage 2: run behavioural-parity verification (point to the corpus suite / the parity oracle).
   - Stage 3: cut over to the Layer-2 `Serilog.*` shim; remove the real Serilog package; confirm CS0433 does **not** fire (proves only one Serilog identity remains).
   - Call out the **Layer-2 coexistence rule** loudly: Layer 2 + real Serilog in the same graph is a compile error by design (G-LAYER2.1), not a runtime surprise.
6. **Per-gap migration table** — one row per practical gap, columns: `Gap | Population rank | Migration companion`. Each companion is a link into `migrations/`. **Hard walls (Seq, expression DSL) appear here too** — their "migration" is honest: "there is no drop-in path; here are the realistic alternatives (re-host the sink behind a Herald equivalent / wait for the community RFC / keep that one path on real Serilog in a separate process)."
7. **Reporting a gap we missed** — pointer to the OSS RFC process (open-source-dilemma rule).

**Per-gap companion docs (one file each, `migrations/<gap-id>.md`).** Each is a short step-by-step "how a Serilog user migrates THIS gap to Herald." Structure each as: *what you have in Serilog → what changes → step-by-step → verification → if it's a hard wall, the honest alternatives.* The set (gap-id ← seam/test inventory):

| gap-id (companion file) | Serilog surface | source | migration shape |
|---|---|---|---|
| `custom-sink.md` | user-authored `ILogEventSink` (S1) | seam S1 | `WriteTo.Sink(new MySink())` works on recompile; the worked example **opens with** the boundary "this absorbs source-compiled sinks, NOT pre-compiled community sinks" |
| `custom-enricher.md` | `ILogEventEnricher` (S2) | seam S2 | `Enrich.With(...)` recompiles; note `{@}` props route through the value-model tree |
| `destructuring-policy.md` | `IDestructuringPolicy` (S5) | seam S5 | `ByTransforming` is the worked example; raw-policy bridges to the tree; **redaction-must-fire callout** (ties G-SEC.1) |
| `audit-sinks.md` | `AuditTo` vs `WriteTo` (S9) | seam S9 | the `auditMode` semantics — `AuditTo` throws, `WriteTo` swallows |
| `config-by-name.md` | sink/enricher by name in `appsettings.json` (S-NEW-1) | seam S-NEW-1 | register your in-house sink via `LoggerSinkRegistry.RegisterSink("MyCompanySink", ...)` — the one call that avoids forking the parser |
| `custom-formatter.md` | `ITextFormatter` / CLEF (S3) | seam S3 | `ILogFormatter` bridge |
| `sub-loggers.md` | `WriteTo.Logger(lc => ...)` (S6) | seam S6 | nested-pipeline composition |
| `level-switch.md` | `LoggingLevelSwitch` (S4) | seam S4 | constructor/property alias onto `LogLevelSwitch` |
| `output-template.md` | `{Level:u3}` / `:lj` grammar | G-GAP.1 | the v1 grammar — what's supported, what degrades |
| `third-party-sinks.md` | Seq/MSSql/Datadog/long tail | hard wall | **no drop-in path** — honest alternatives only; links the parity-audit wall |
| `expressions-dsl.md` | `Serilog.Expressions` string DSL | hard wall | predicate form maps; string-DSL does not — community RFC pointer |

- [ ] **Step 1 (T-H2 — Heather consult):** Confirm the table-→-companion split with Heather. Decision to settle: do the *small* gaps (level-switch, sub-loggers) stay **inline** in the README, and only the *substantial* ones get companion files? Or is every row a companion for uniformity? Record her call as a `<!-- Heather T-H2: ... -->` note in the README. Default if undecided: substantial gaps (S1/S2/S5/S9/S-NEW-1, both hard walls) get companions; small structural-match gaps stay inline.

- [ ] **Step 2 (draft now):** Write README sections 1–7 and every companion's *structure + steps that don't depend on shipped behaviour* (the "what you have / what changes / step shape" — these come from the seam inventory, which is design-final).

- [ ] **Step 3 (fill after P-n):** Backfill the verification snippets that need real artifacts — the corpus check command (after P1), the `RegisterSink` exact signature (after P5), the CS0433 cutover proof (after P7), the redaction-fires verification (after P4/G-SEC.1). Mark each `<!-- FILL AFTER P-n -->`.

- [ ] **Step 4: Verify — every practical gap in `test-inventory.md` has a matching migration entry.** This is the load-bearing completeness check.

```bash
cd E:/dev/Herald.OSS
# Each seam + hard wall must have a companion file OR an inline README anchor.
ls docs/serilog-compat/migrations/
# Cross-check: the gap-ids below must each resolve to a file or a README heading.
for g in custom-sink custom-enricher destructuring-policy audit-sinks config-by-name custom-formatter sub-loggers level-switch output-template third-party-sinks expressions-dsl; do
  test -f "docs/serilog-compat/migrations/$g.md" && echo "companion: $g" || grep -qi "$g" docs/serilog-compat/README.md && echo "inline: $g" || echo "MISSING: $g"
done
```
Expected: every gap reports `companion:` or `inline:` — zero `MISSING:`.

- [ ] **Step 5: Verify — hard-wall companions are honest.** `third-party-sinks.md` and `expressions-dsl.md` must state there is no drop-in path and link the parity-audit wall; they must NOT imply a workaround that doesn't exist.

```bash
grep -niE "no drop-in|hard wall|structural identity wall|cannot bind" docs/serilog-compat/migrations/third-party-sinks.md docs/serilog-compat/migrations/expressions-dsl.md
```
Expected: each hard-wall companion names the wall plainly.

- [ ] **Step 6: Verify — honest claim only, across README + companions.**

```bash
grep -rniE "1-to-1|binary drop-in|100% compatible|fully compatible|seamless drop-in" docs/serilog-compat/README.md docs/serilog-compat/migrations/ || echo "clean"
```
Expected: `clean`.

- [ ] **Step 7: Commit.**

```bash
git add docs/serilog-compat/README.md docs/serilog-compat/migrations/
git commit -m "docs(serilog-compat): consumer README + per-gap migration runbooks"
```

---

### Task 3: Honest-claim marketing copy stub (`honest-claim.md`) — hand off to Dawn

The single source of the headline wording. Engineering owns the *truth* of the claim; Dawn (website) owns the *presentation*. This stub gives Dawn exact, pre-approved copy she cannot drift from.

**Files:**
- Write: `docs/serilog-compat/honest-claim.md`

**Section outline:**
1. **The approved headline** — the exact PRD wording (block-quoted, verbatim, the single canonical string).
2. **The one-line version** (for a hero/CTA) — a tightened form that still holds the boundary. Draft candidate (Dawn refines tone, not truth): *"Swap the package, rebuild — your Serilog code runs on Herald. Popular sinks map over; the third-party-sink ecosystem and the expression DSL are a documented boundary."*
3. **What we may say** — source-compatible on recompile; popular sinks map over; `appsettings.json` + ASP.NET wiring drop in; no measurable allocation/perf regression on Herald's hot paths (cite the benchmark **after P1 lands**).
4. **What we may never say** (the banned list) — "1-to-1", "binary drop-in", "100% / fully compatible", "seamless", anything implying Seq/community sinks or the expression DSL work, anything implying binary identity / strong-name compatibility.
5. **The boundary, stated for marketing** — one plain paragraph a non-engineer can repeat: popular sinks are covered; pre-compiled community sinks (Seq and the long tail) and the expression DSL are a named, documented boundary, not a bug.
6. **Hand-off note to Dawn** — Dawn owns tone + reading-level (per `mmpworks-writing-voice`, hero copy = simple sentences, positive framing); she does **not** edit the truth conditions in §3/§4. Any new claim goes back through engineering.

- [ ] **Step 1 (draft now):** Write all six sections. The wording is design-final from the PRD; only the perf-citation in §3 waits on P1.
- [ ] **Step 2 (fill after P1):** Replace the perf placeholder with the real net10 benchmark figure + provenance (runtime pinned per the .NET-10-only rule). Until then: `<!-- FILL AFTER P1: net10 alloc/throughput figure + provenance -->`.

- [ ] **Step 3: Verify — the approved headline is byte-identical to the PRD.**

```bash
cd E:/dev/Herald.OSS
# The canonical sentence must match the PRD source.
grep -n "Swap the package, rebuild" docs/serilog-compat/honest-claim.md docs/serilog-compat/2026-05-29-scope-prd.md
# Banned wording must be absent from the claim doc itself (the §4 list NAMES them as banned, so allow the "never say" section to mention them once — verify by hand if grep trips).
grep -niE "binary drop-in|1-to-1" docs/serilog-compat/honest-claim.md
```
Expected: the headline appears in both files; banned terms appear only inside the §4 "never say" list.

- [ ] **Step 4: Commit + flag Dawn hand-off.**

```bash
git add docs/serilog-compat/honest-claim.md
git commit -m "docs(serilog-compat): honest-claim copy stub (hand-off to Dawn)"
```

---

### Task 4: Finalize after P1–P7 land + Heather final review (T-H3)

P8 docs **finalize last**. The numbers and observed behaviour must be real before the docs are marked done. This task is the gate that turns the drafted docs into shipped docs.

- [ ] **Step 1: Confirm P1–P7 are landed.** Check the roadmap's done-state for each sub-plan; do not proceed while any `FILL AFTER P-n` placeholder still has no real artifact behind it.

```bash
cd E:/dev/Herald.OSS
# No placeholder may survive into the final docs.
grep -rn "FILL AFTER" docs/serilog-compat/parity-audit.md docs/serilog-compat/README.md docs/serilog-compat/honest-claim.md docs/serilog-compat/migrations/
```
Expected (at finalize time): no matches. Every placeholder replaced with a real number / behaviour / error string from the shipped layers.

- [ ] **Step 2: Backfill the real artifacts** — the net10 allocation rows + provenance (from P1's exact-byte harness), the loud-fail error text (P5), the request-log line field shape (P6), the CS0433 cutover proof (P7), the redaction-fires confirmation (P4/G-SEC.1). Quote the *current* shipped numbers — never narrate how they got there (per the reviewers-have-no-prior-iteration rule).

- [ ] **Step 3 — completeness re-verify (the two structural checks):**

```bash
cd E:/dev/Herald.OSS
# (a) Every gap in the test inventory has a migration entry (re-run Task 2 Step 4 loop).
for g in custom-sink custom-enricher destructuring-policy audit-sinks config-by-name custom-formatter sub-loggers level-switch output-template third-party-sinks expressions-dsl; do
  test -f "docs/serilog-compat/migrations/$g.md" || grep -qi "$g" docs/serilog-compat/README.md || echo "MISSING: $g"
done
# (b) Every hard wall in the parity audit cites a loud-fail regression test that actually exists.
grep -nE "G-SINK-WALL\.1|G-GAP\.2" docs/serilog-compat/parity-audit.md
grep -rlnE "G-SINK-WALL\.1|G-GAP\.2" tests/ 2>/dev/null || echo "WARN: confirm the loud-fail tests shipped in P4/P5"
```
Expected: zero `MISSING:`; the hard-wall test IDs are cited in the audit AND exist in `tests/`.

- [ ] **Step 4 (T-H3 — Heather final review):** Heather does the final prose pass — voice (plainspoken engineer per `CLAUDE.md`), dual-register where a gap is subtle (technical + analogy at the right reading level, per the dual-register rule), CUPID/DRY of the doc set (no duplicated migration steps across companions; shared steps factored to the README). She signs off that the docs are done. Record sign-off as a `<!-- Heather T-H3: approved YYYY-MM-DD -->` note at the top of the README.

- [ ] **Step 5: Verify — honest claim holds across the whole doc set one last time.**

```bash
cd E:/dev/Herald.OSS
grep -rniE "1-to-1|binary drop-in|100% compatible|fully compatible|seamless drop-in|drop-in replacement" \
  docs/serilog-compat/parity-audit.md docs/serilog-compat/README.md docs/serilog-compat/migrations/ \
  | grep -viv "never say" || echo "clean"
```
Expected: `clean` (only the `honest-claim.md` §4 "never say" list may name the banned terms).

- [ ] **Step 6: Final commit — P8 done.**

```bash
git add docs/serilog-compat
git commit -m "docs(serilog-compat): P8 finalize — real numbers backfilled, Heather sign-off (T-H3)"
```

---

## Self-review notes

- **Spec coverage:** P8 implements scope-PRD deliverable #2 (`parity-audit.md` friction map, Jared's verbatim wall), the README "how to" with the per-gap migration plans the PRD added (§"The parity audit"), and the honest-claim stub for Dawn. The per-gap entries are structured as a table → companion docs per the task instruction.
- **Heather (Documentation Owner) consult is threaded, not bolted on:** T-H1 (structure, before drafting), T-H2 (table-vs-companion split), T-H3 (final prose + dual-register + CUPID/DRY review + sign-off). The PRD requires the consult; this plan makes it three named gates.
- **Finalize-last is enforced mechanically:** every behaviour/number cell is a `FILL AFTER P-n` placeholder until the layer ships, and Task 4 Step 1 refuses to finalize while any placeholder is unbacked. Final numbers depend on P1–P7 — flagged in the header and at every fill point.
- **Completeness is a grep, not a vibe:** Task 2 Step 4 + Task 4 Step 3 prove every `test-inventory.md` gap has a migration entry, and every parity-audit hard wall cites a real loud-fail test (G-SINK-WALL.1 / G-GAP.2).
- **Honest claim is gated at every doc:** banned-wording grep in Tasks 1, 2, 3, and 4 — the only place the banned terms may appear is the `honest-claim.md` §4 "never say" list.
- **Plan-only:** this writes the plan. The docs themselves are written when an agent executes Tasks 0–4 against the shipped P1–P7 artifacts.

## Open decisions (resolve with Heather / Dawn before/while executing)

1. **(T-H2) Inline vs companion for small gaps** — do `level-switch` and `sub-loggers` (structural matches) stay inline in the README, or get companion files for uniformity? Plan default: substantial gaps + both hard walls get companions; small ones inline. Heather's call.
2. **honest-claim.md home** — does the stub live in `docs/serilog-compat/` (engineering-owned source of truth) or move into Dawn's shared copy-deck? Plan default: source of truth here, Dawn references it. Confirm at T-H1.
3. **Old-config migration scope** — if P0's alias map was removed (P0 Task 9) and old on-disk configs must still load via a one-time migration shim, the README's config-by-name companion may need a "migrating old-key configs" note. Depends on the P0 Task 9 open decision (resolve with Richard); flag in `config-by-name.md` if the shim ships.

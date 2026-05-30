<!-- Heather T-H1 (2026-05-30): STRUCTURE SIGN-OFF — APPROVED with one adjustment.

  The three-doc split (parity-audit / README / migrations/*) matches the Herald
  docs-as-database model. Friction map = structured records (the table is the data
  spine); README + companions = markdown-with-frontmatter prose. Good fit.

  ADJUSTMENT — avoid the README/runbook duplication. By the time of this consult,
  migration-runbook.md (200 lines) was already authored by Task 8 and IS the consumer
  "how to" prose (decision check, two-layer model, step-by-step cutover, hard
  constraints). Re-authoring that body here would be a DRY violation — the same fact in
  two renders, exactly the drift trap docs-as-database exists to kill.
  So this README is the ENTRY-POINT / index, not a second runbook:
    - it states the honest claim (quote, never re-author)
    - it routes to the runbook (the how-to), the parity audit (the friction map),
      and the per-gap companions
    - the per-gap table here is the INDEX; the steps live in migration-runbook.md
      (shared) + the companions (per-gap detail).
  One how-to body, many entry points. That is the docs-as-database win.

  FRONTMATTER SCHEMA — CONFIRMED for companion docs (gap-id, serilog-surface,
  herald-status, population-rank, regression-test-id). See any migrations/*.md stub.

  WORKED-EXAMPLES RECONCILIATION (flagged, not yet executed): docs/serilog-compat/
  worked-examples/ already holds S1/S2/S5 written from the IMPLEMENTER's POV (wire
  path / files / impl notes). migrations/*.md is the CONSUMER's POV (what you have →
  what changes → steps → verify). These are two audiences, not duplicates — but the
  CODE in both must not drift. T-H3 resolves: migrations/* link to worked-examples/*
  for the deep implementation view rather than re-pasting the code. -->

<!-- Heather T-H2 (2026-05-30): MIGRATION-TABLE SPLIT — DECIDED.

  STANDALONE COMPANIONS (substantial — need step-by-step, opens with a boundary, or
  carries a security/compliance callout):
    custom-sink.md, custom-enricher.md, destructuring-policy.md, audit-sinks.md,
    config-by-name.md, third-party-sinks.md (hard wall), expressions-dsl.md (hard wall).

  INLINE in migration-runbook.md (one-to-two-line structural matches, no boundary):
    custom-formatter (S3), level-switch (S4), sub-loggers (S6), output-template (G-GAP.1).

  RATIONALE: a companion earns its file when it (a) opens with a hard boundary a reader
  could misread (custom-sink: "this is NOT Seq"), (b) carries a security/compliance
  callout (destructuring-policy: redaction-must-fire; audit-sinks: throw-vs-swallow),
  (c) has a non-obvious registration call (config-by-name: RegisterSink), or (d) is a
  hard wall needing honest alternatives (third-party-sinks, expressions-dsl). The four
  inline gaps are structural aliases — a constructor rename or a verb map — and a row +
  two lines in the runbook says everything. Splitting them into files would be ceremony,
  not clarity (YAGNI).

  NOTE: parity-audit.md currently links migrations/custom-formatter.md, sub-loggers.md,
  level-switch.md, output-template.md — those links must be repointed to runbook anchors
  in Task 2 (or those four companions created as thin redirects). Flagged for the
  drafting agent; not a structure change, a link-target fix. -->

# Serilog Drop-In Compatibility — Start Here

- **Date:** 2026-05-30
- **Branch:** `feat/serilog-compat`
- **Status:** Skeleton (T-H1 scaffold). Index prose drafted in Task 2.

<!-- Heather T-H3: approved YYYY-MM-DD  (filled at final review after P1-P7 land) -->

---

## The honest claim

<!-- Quote honest-claim.md §1. Single source — do not re-author here. -->

## Is Herald a drop-in for you?

<!-- The 4-question decision check. Short. Routes a "yes-to-all" reader straight to the
     fast path and a "no" reader to their gap's companion. The full check lives in
     migration-runbook.md §"Is Herald a drop-in for you?" — quote the four questions,
     link the runbook for the staged path. -->

## How the docs are organized

<!-- One paragraph orienting the reader:
       - migration-runbook.md  — the step-by-step how-to (stage on Layer 1, verify, cut to Layer 2)
       - parity-audit.md       — the friction map (every surface tagged + ranked)
       - migrations/*.md        — one companion per substantial gap
       - honest-claim.md        — the claim wording (engineering-owned source) -->

## Per-gap migration index

<!-- The index table: Gap | Population rank | Where to go.
     "Where to go" = a companion link for the seven standalone gaps, OR a runbook anchor
     for the four inline gaps. This table is the single navigational spine; the parity
     audit's friction map is the engineering spine. Keep them consistent (same gap names,
     same ranks) — they render from the same gap set. -->

| Gap | Population rank | Migration path |
|---|---|---|
<!-- custom-sink | H | migrations/custom-sink.md -->
<!-- custom-enricher | H | migrations/custom-enricher.md -->
<!-- destructuring-policy | H | migrations/destructuring-policy.md -->
<!-- config-by-name | H | migrations/config-by-name.md -->
<!-- audit-sinks | M | migrations/audit-sinks.md -->
<!-- custom-formatter | M | migration-runbook.md (inline) -->
<!-- sub-loggers | M | migration-runbook.md (inline) -->
<!-- level-switch | M | migration-runbook.md (inline) -->
<!-- output-template | H | migration-runbook.md (inline) -->
<!-- third-party-sinks | H | migrations/third-party-sinks.md (hard wall) -->
<!-- expressions-dsl | M | migrations/expressions-dsl.md (hard wall) -->

## Reporting a gap we missed

<!-- Pointer to the OSS RFC process (open-source-dilemma rule). The runbook already has a
     "Reporting a gap" section — quote/link it, don't duplicate. -->

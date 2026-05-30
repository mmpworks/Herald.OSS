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

<!-- Heather T-H3: approved YYYY-MM-DD  (filled at final review after P1-P7 land) -->

---

## The honest claim

> *"Swap the package, rebuild, and your standard Serilog code runs on Herald — popular sinks map over; the third-party-sink ecosystem and the expression DSL are a documented boundary."*

Source compatibility on recompile. Not binary identity — Herald does not have Serilog's strong-name key and will not spoof it. That single fact draws the hard edge of what carries over and what doesn't.

---

## Is Herald a drop-in for you?

Four questions. Answer them before picking a migration path.

1. Do you use only the standard sinks Herald ships — Console, File, HTTP, TCP, UDP, Elasticsearch, OTLP, Null?
2. Is your code configured in C# (`LoggerConfiguration().WriteTo...`) or `appsettings.json`? No `Serilog.Expressions` string DSL?
3. Are your custom sinks and enrichers **source-compiled** in your own repo — not pre-compiled community NuGet packages like Seq or MSSqlServer?
4. Are you targeting net9 or net10?

If the answer is yes to all four, the fast path works straight through. Go to [migration-runbook.md](migration-runbook.md).

If any answer is no, find your gap in the [per-gap table](#per-gap-migration-index) below before you start.

---

## How the docs are organized

- **[migration-runbook.md](migration-runbook.md)** — the step-by-step how-to. Decision check, two-layer model, the staged runbook (Layer 1 alongside real Serilog → verify → cut to Layer 2), hard constraints, and community sink equivalents.
- **[parity-audit.md](parity-audit.md)** — the friction map. Every Serilog surface tagged as *carries-over*, *maps-to-Herald-equivalent*, or *hard-wall*, ranked by how much of the Serilog user base each gap blocks. Engineering reference.
- **[migrations/\*.md](migrations/)** — one companion per substantial gap. Step-by-step migration for a specific Serilog extension surface.
- **[honest-claim.md](honest-claim.md)** — the claim wording. Engineering-owned source of truth for marketing copy.

---

## Per-gap migration index

The seven substantial gaps have companion files with step-by-step migration paths. The four structural-match gaps are covered inline in [migration-runbook.md](migration-runbook.md) — they are constructor aliases or verb maps and do not need a separate file.

| Gap | Population rank | Migration |
|---|---|---|
| Custom user sink (`ILogEventSink`) | High | [migrations/custom-sink.md](migrations/custom-sink.md) |
| Custom enricher (`ILogEventEnricher`) | High | [migrations/custom-enricher.md](migrations/custom-enricher.md) |
| Sink/enricher by name in `appsettings.json` | High | [migrations/config-by-name.md](migrations/config-by-name.md) |
| Pre-compiled community sinks (Seq, MSSqlServer, Datadog, long tail) | High | [migrations/third-party-sinks.md](migrations/third-party-sinks.md) — hard wall, no drop-in path |
| Custom destructuring policy / redaction | Medium-high | [migrations/destructuring-policy.md](migrations/destructuring-policy.md) |
| `AuditTo` vs `WriteTo` failure semantics | Medium | [migrations/audit-sinks.md](migrations/audit-sinks.md) |
| `Serilog.Expressions` string DSL | Medium | [migrations/expressions-dsl.md](migrations/expressions-dsl.md) — hard wall, no drop-in path |
| Output-template grammar (`{Level:u3}`, `{Message:lj}`, etc.) | High | [migration-runbook.md](migration-runbook.md) (inline) |
| Custom `ITextFormatter` / CLEF | Medium | [migration-runbook.md](migration-runbook.md) (inline) |
| Sub-loggers (`WriteTo.Logger(lc => ...)`) | Low | [migration-runbook.md](migration-runbook.md) (inline) |
| `LoggingLevelSwitch` | Low | [migration-runbook.md](migration-runbook.md) (inline) |

---

## Reporting a gap we missed

If you hit a Serilog surface this documentation does not cover, open an issue on Herald.OSS with the label `serilog-compat`. See [migration-runbook.md § Reporting a gap](migration-runbook.md) for the details we need.

If the gap is structural (assembly identity or strong-name), the [parity audit](parity-audit.md) explains why and links to the community RFC discussion. Structural gaps are presented to the OSS community as open problems — not treated as silent deferrals.

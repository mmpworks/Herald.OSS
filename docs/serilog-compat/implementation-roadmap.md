# Serilog-Compat — Implementation Roadmap (decomposition)

- **Date:** 2026-05-29 · **Branch:** `feat/serilog-compat`
- This initiative is too large for one plan. It decomposes into the sequenced sub-plans below; each is its own spec→plan→implement cycle and each produces working, testable software on its own. Detailed plans live in `docs/serilog-compat/plans/`.

## Sequencing & dependencies

```
P0 Rename wave ──┬─> P1 Layer-1 core ──┬─> P2 LoggerConfiguration ──┬─> P5 Settings (Apache-2.0)
   (PREREQ)      │                     ├─> P3 Output-template grammar│
                 │                     └─> P4 Seams                   ├─> P6 ASP.NET Core
                 │                                                    │
                 └────────────────────────────────────────> P7 Layer-2 mirror (after surface stable)
P8 Parity audit + migration docs (README "how to") — runs alongside; finalizes last (consult Heather)
Cross-cutting test fixtures — built inside P0/P1, reused everywhere
```

**Why P0 is first:** both architects + Echo agree the rename lands before anything else, so the Serilog-hole-named arity generator and every compat type is authored against final names (`Information`/`Warning`/`Fatal`/`Verbose`), never names that change under it. P0 is also the highest *mechanical* risk (cross-repo lockstep) — it gets a the-fool pre-mortem before the sweep.

## Sub-plans

| ID | Scope | Key deliverables | Depends | Primary owners |
|---|---|---|---|---|
| **P0** | **Rename wave** (THIS plan first) | Transitional alias map; rename `KnownLogLevels`/`KnownLogLevelKeys`/`LogLevelKeys` + typed verbs; cross-table drift fix; mechanical sweep (~19 OSS files + Dashboard + DemoApp + wire) in 6-step lockstep; G-LEVEL regression suite; alias map removed at end | — | Glenn (sweep), Richard (arch), Nancy (Dashboard), Max (build), Echo→test-master (tests) |
| **P1** | **Layer-1 core call surface** | `MMP.Herald.Serilog` ILogger + static `Log` facade; level map; templates incl `{@}`/`{$}`; **Serilog-hole-named arity generator** (over `LogPropertyCompact.From<T>`, golden + exact-byte tests); **value-model mirror** (flat-fast/tree-on-demand) + **Jared's 2 guards** | P0 | Jared (lowering+mirror), Richard (facade), test-master |
| **P2** | **LoggerConfiguration builder** | `MinimumLevel.*`/`WriteTo.*`/`Enrich.*`/`.CreateLogger()` translating onto `QuickLogBuilder`; sink mapping (Console/File/HTTP/TCP/UDP/ES/OTLP/Null); `LoggingLevelSwitch`→`LogLevelSwitch` | P1 | Richard, Glenn |
| **P3** | **Output-template grammar** | `{Timestamp:fmt}`/`{Level:u3}`/`{Message:lj}` parser + renderer; built-in `ITextFormatter`s; CLEF mirror; **S3 formatter seam** | P1 | Jared/Richard, test-master |
| **P4** | **Seams** | **S1** custom `ILogEventSink`; **S2** custom `ILogEventEnricher` (+ `ILogEventPropertyFactory` routing to tree); **S5** raw `IDestructuringPolicy` string↔tree bridge (redaction security); **S9** `AuditTo` throw vs `WriteTo` swallow | P1 | Rosanne (seam review), Richard, test-master |
| **P5** | **Settings (Apache-2.0)** | `Herald.OSS.Serilog.Settings` — reimplemented `ReadFrom.Configuration` parser; **S-NEW-1** `LoggerSinkRegistry`/`LoggerEnricherRegistry`; loud-named fail on unresolved/Seq | P2 | Richard, Glenn |
| **P6** | **ASP.NET Core** | `MMP.Herald.Serilog.AspNetCore` — `UseSerilog`/`AddSerilog` over existing `HeraldLoggerProvider`; **`UseSerilogRequestLogging` middleware** (net-new); output-shape tests | P2, P5 | Richard, test-master |
| **P7** | **Layer-2 mirror** | `Serilog` + `Serilog.AspNetCore` — zero-behaviour mirror types over Layer 1; **CS0433 coexistence test** (Layer-2 = final-cutover only) | P1–P6 | Glenn (mechanical mirror), Richard (review) |
| **P8** | **Parity audit + migration docs** | The friction map (`parity-audit.md`); **per-gap migration plan** in `README.md` (inline or companion link) — the consumer "how to" guide; honest-claim copy | all | Heather (Documentation Owner), Dawn (claim copy), Rick (outreach later) |

## Cross-cutting test fixtures (build inside P0/P1, reuse everywhere)

`ThrowingSink` · `SecretBearingFixture` + full-output secret scanner · real-Serilog parity oracle (Layer-1 coexistence) · exact-byte alloc harness (net10 InProcess, full arity 1..16, call-1-vs-2 delta) · replay-ring+SSE inter-step harness · cross-table reflection fixture. Details in `test-inventory.md`.

## Standing rules that bind every sub-plan

- **net9 + net10 only.** Benchmark + publish on **net10** only.
- **No alloc/perf regression** on Herald hot paths — exact-byte gates, not thresholds.
- **CUPID/DRY** — Layer 2 holds zero logic; compat is mapping over one canonical impl.
- **Every audited gap → a regression test** asserting the known limit; gap-classes get a suite.
- **Honest claim** — "swap + rebuild," never "1-to-1." Coexistence is Layer-1 only.
- **Per-gap migration plan** in the README (Steve's addition), in consult with Heather.

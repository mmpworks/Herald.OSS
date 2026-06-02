# PRD — Four-Project Serilog→Herald Migration + Website Results

- **Date:** 2026-06-01 (overnight autonomous run)
- **Driver:** main Claude orchestrating the team (Richard, Jared, Glenn, Max, Dawn)
- **Trigger:** Steve — "we need NuGet packages that can be used to migrate the 4 projects, and
  the website needs to show the results of those migrations. Richard and Jared and the rest of
  the team need to drive this to success." (asleep — proceed on best judgment, flag assumptions.)

## DECISION (locked 2026-06-01) — lowest migration friction wins

Steve: "we want the lowest migration friction possible." The migration vehicle is therefore the
**renamed Layer-2 NuGet package** (`MMP.Herald.Compat.Serilog`, assembly renamed off `Serilog.dll`
but keeping `namespace Serilog`). The consumer **swaps one package reference and changes zero
source** — no per-file edits, no consumer-side rebuild of Herald. Extend the rename to the
AspNetCore (and, where feasible, Expressions) compat assemblies so all four projects are zero-edit.

- **Primary vehicle:** renamed `MMP.Herald.Compat.*` packages → zero source change.
- **Fallback / staging only:** Layer-1 `MMP.Herald.Serilog` find-replace (`using Serilog;` →
  `using MMP.Herald.Serilog;`), for gradual migration where real Serilog must coexist.
- **Bin-swap `Serilog.dll`:** kept for can't-recompile cases only.

**Alias technique is DEAD — empirically disproven against the published 0.12.5 package:**
| Tried | Legacy `using Serilog;` kept? | Result |
|---|---|---|
| `global using Serilog = MMP.Herald.Serilog;` | yes | CS0246 |
| per-file `using Serilog = MMP.Herald.Serilog;` + unqualified `Log` | — | CS0103 |
| `global using MMP.Herald.Serilog;` + delete `using Serilog;` lines | no | ✓ but per-file edit |
| **Layer-2 assembly (declares `namespace Serilog`)** | **yes** | **✓ zero-edit (proven)** |

Layer-1 has no namespace literally named `Serilog`, so an alias can never make legacy
`using Serilog;` compile. Only a Layer-2 assembly that declares `namespace Serilog` gives zero-edit;
renaming its assembly file (one Herald-side rebuild) makes it NuGet-shippable. The runbook line-78
per-file-alias claim is wrong and must be corrected once the canary confirms the renamed package.

## What / Why

Prove the Serilog→Herald compat story end-to-end on **four real, buildable reference projects**,
migrated with the **published `MMP.Herald.Serilog` 0.12.5** NuGet packages, and surface the
**measured results** on the public website's migration + stories pages.

The migration *capability* is shipped (0.12.5, CLEAN-SWAP-OK on a trivial repro). What is missing:
concrete migrated reference projects with captured results, and the website presentation of them.

## The four reference projects (ASSUMPTION — flagged for Steve's review)

Not pre-defined anywhere; defined here to cover the full migration surface. Each is a small but
real Serilog app, then migrated via the one-namespace find-replace to `MMP.Herald.Serilog` 0.12.5.

1. **Ref1.Worker** — .NET worker/console. Basic `Log.Information/Warning/Error`, Console + File
   sinks, `appsettings.json` via `ReadFrom.Configuration`. The bread-and-butter case.
2. **Ref2.WebApi** — ASP.NET Core minimal API. `UseSerilog`, `UseSerilogRequestLogging`,
   `appsettings.json`, one property enricher. The web case.
3. **Ref3.CustomExt** — console with a source-compiled **custom sink + custom enricher**
   (worked-examples S1 + S2). The extension-author case.
4. **Ref4.Filtering** — console with a **destructuring policy** (S5) + a `Serilog.Expressions`
   string filter migrated via the `MMP.Herald.Serilog.Expressions` companion. The advanced case.

These map 1:1 to the existing migration playbooks (`migrations/*.md`) and worked examples.

## Acceptance criteria

1. **Builds migration-ready.** Herald.OSS / the 0.12.5 packages restore and build; a fresh
   consumer can reference `MMP.Herald.Serilog` 0.12.5 from the local feed and build.
2. **Four projects migrated.** Each project exists in a Serilog baseline form that builds + runs,
   and a migrated form that builds + runs on Herald via the one-namespace find-replace
   (`using Serilog;` → `using MMP.Herald.Serilog;`).
3. **Results captured** to `docs/serilog-compat/migrations/results/migration-results.json`
   (schema below) + a short per-project markdown. No silent failures — record partial/failed
   outcomes honestly.
4. **Website navigable + shows results.** `MigrateFromSerilogPage.vue` surfaces the four results;
   the reviews/stories pages present the 9 reviews and are reachable from nav. On a **branch with
   a localhost preview** — NO production push (that gate needs Steve awake).

## Results JSON schema (canonical — every project emits one record)

```json
{
  "schemaVersion": 1,
  "package": "MMP.Herald.Serilog",
  "packageVersion": "0.12.5",
  "runDate": "2026-06-01",
  "projects": [
    {
      "id": "ref1-worker",
      "title": "Worker / Console",
      "serilogShape": "Log.* + Console/File sinks + appsettings.json",
      "filesChanged": 0,
      "linesChanged": 0,
      "namespaceOnlyDiff": true,
      "serilogBaseline": { "builds": true, "runs": true },
      "migrated": { "builds": true, "runs": true, "testsPass": true },
      "playbook": "migrations/config-by-name.md",
      "notes": "",
      "gotchas": []
    }
  ]
}
```

## Plan / waves (dependencies are real — sequenced)

- **Wave 1 — Richard (lead, .NET architect-implementer):** scaffold the 4 Serilog reference apps,
  migrate each via 0.12.5, build+run both sides, capture the results JSON + per-project notes.
  Do Ref1 first as the canary; if the package can't migrate a realistic app, STOP and report.
  May dispatch Glenn for the mechanical namespace passes. Resolve the Layer-2-NuGet question
  definitively (csproj exists at `src/Compatibility/Layer2/`; honest-claim says bin-swap-not-NuGet).
- **Wave 2 — Jared (independent verification):** fresh-consumer reproduction of ≥2 migrations from
  the local 0.12.5 feed; red-team the captured numbers for honesty; verdict doc.
- **Wave 3 — Dawn (website):** enhance `MigrateFromSerilogPage.vue` to show the 4 results; make the
  reviews/stories pages navigable with the 9 reviews; nav wiring. Branch + preview, no prod push.
  Consult Richard (architecture) / Glenn (mechanical) per the no-guess rule. Max assists on
  packaging/build if a package gap surfaces.
- **Wave 4 — main:** verify preview builds, write status, push-notify Steve.

## Reversibility / guardrails

- All work on branches; local commits only; NO NuGet publish, NO prod website push overnight.
- The four-project definition is an assumption — documented here, reviewable, changeable at dawn.
- Honest results only; failures recorded, not hidden.

# JARED — independent Wave-2 verification of Richard's Serilog-compat migration

- **By:** Jared (Go/Rust systems engineer, here as independent .NET verifier), 2026-06-02
- **Branch:** `feat/four-project-migration` (`E:\dev\Herald.OSS`)
- **Posture:** adversarial reproduction. Every claim below I built and ran myself in fresh
  off-repo temp dirs (`C:\Users\smuch\jared-verify\*`) against the local 0.12.5 feed. I did not
  trust the report; I reproduced it. Changed nothing in the repo except this doc.
- **Environment:** dotnet SDKs 8.0.421 / 9.0.314 / 10.0.204; net9 + net10 runtimes present.

## Bottom line

| Target | Verdict |
|---|---|
| 1. Renamed-assembly NuGet technique | **CONFIRMED** (net9 + net10, plus CS0433 coexistence) |
| 2. HIGH/SECURITY destructure native-sink leak | **CONFIRMED** — real redaction bypass, secret leaks |
| 3. Results JSON honesty | **CONFIRMED with caveats** — no dishonesty; two notes oversell parity |
| 4. Edges (AspNetCore / Seq / AOT) | **PARTIAL** — Seq + AOT confirmed; AspNetCore rename NOT done (PRD goal unmet) |

The security finding reproduces. Say it plainly: a `Destructure.ByTransforming<T>` that strips a
secret is **silently ignored on `WriteTo.Console()`**, and the secret reaches the output.

---

## Target 1 — Renamed-assembly NuGet technique: CONFIRMED

Fresh consumer `C:\Users\smuch\jared-verify\t1-consumer`, off-repo, `nuget.config` = local feed +
nuget.org. Verbatim unchanged Serilog source (`using Serilog;` + `new LoggerConfiguration()
.WriteTo.Console().CreateLogger();` + `Log.Information("x {N}", 1)`), referencing ONLY
`MMP.Herald.Compat.Serilog` 0.12.5.

- **net10.0:** build succeeded, **0 warnings**. Ran: `INF:2 ... - x 1`. Output dir contains
  `MMP.Herald.Compat.Serilog.dll` + `MMP.Herald.Serilog.dll` + `Herald.OSS.dll`, and **NO bare
  `Serilog.dll`**. Grep for `Serilog.dll` (exact) → absent.
- **net9.0:** same source, retargeted. Build succeeded, **0 warnings**. Ran: `INF:2 ... - x 1`.
  **NO bare `Serilog.dll`**; `MMP.Herald.Compat.Serilog.dll` present.
- **Coexistence constraint:** added real `Serilog` 4.3.1 back. Build **FAILED with CS0433**,
  verbatim as reported:
  > error CS0433: The type 'Log' exists in both 'MMP.Herald.Compat.Serilog, Version=0.12.5.0,
  > ...PublicKeyToken=null' and 'Serilog, Version=4.3.0.0, ...PublicKeyToken=24c2f752a8e58a10'

The package internals match the doc: `lib/net9.0` + `lib/net10.0` only, no `Serilog.dll`, depends on
`MMP.Herald.Serilog` 0.12.5. The technique is real and works on both TFMs. honest-claim §5 is indeed
wrong as written; the renamed-package vehicle ships and is zero-source-change for inline-wired apps.

## Target 2 — Destructure redaction native-sink leak: CONFIRMED (security)

Reproduced the 8-line repro in a fresh consumer (`t2-leak`, Layer-1 `MMP.Herald.Serilog` 0.12.5),
driving BOTH sink paths in one program with a sentinel secret `sk_SENTINEL_LEAK_CANARY_8842`.

- **PATH A — `WriteTo.Console()` (native):**
  `INF:2 ... Customer { Name = Ada, Email = ada@acme.test, ApiKey = sk_SENTINEL_LEAK_CANARY_8842 }`
  → grep for the sentinel: **PRESENT. The secret LEAKS.**
- **PATH B — `WriteTo.Sink(customSink)`:**
  `CUSTOMSINK-RENDER: Customer { Name = Ada, Email = ada@acme.test }`
  → grep for the sentinel: **ABSENT. Correctly stripped.**

The `Destructure.ByTransforming<Customer>` policy fires on the custom-sink mirror path and is
**bypassed on the native pipeline**. This is a silent redaction bypass on the single most common
sink shape. The finding stands, and it is HIGH-severity correctly. The regression test
`REG-SERILOG-DESTRUCTURE-NATIVE-SINK` (redaction-coverage suite, every sink kind, sentinel grep) is
the right shape — build it before this ships.

**One documentation nit (does not weaken the finding):** the finding cites class names
`SerilogDestructuringApplicator` / `SerilogSinkAdapter`. Those exact identifiers are not what I found
in the tree; the real applicator call is `applicator.Apply(rawValue)` at
`src/Serilog/Events/LogEvent.cs:172` (the line citation is correct; the path is the Layer-1
`src/Serilog/` tree, not `src/Compatibility/Layer2/`). The diagnosis is behaviorally accurate — the
class names in the writeup are imprecise. Fix the names when the fix lands so the next reader greps
successfully.

## Target 3 — Results JSON honesty: CONFIRMED, with two oversold-parity caveats

I diffed and ran every project. No project's `runs:true` is dishonest, and the one `runs:false` is
honestly conservative. But two "identical to baseline" notes paper over visible output differences.

- **Ref3 "BYTE-IDENTICAL Program.cs": TRUE.** `cmp before/after Program.cs` → zero byte difference.
  Only the csproj changed (`Serilog` 4.3.0 → `MMP.Herald.Compat.Serilog` 0.12.5). Built + ran the
  after/: custom sink fires, counts 4 events, enricher loads. `migrated.runs:true` is honest.
  - Ran the before/ (real Serilog) too: the ONLY divergence is string quoting in `RenderMessage()`
    — Serilog renders `"init"` / `"Acme Corp"` (quoted), Herald renders them unquoted. The JSON
    calls this "Not a behavior change." That's slightly overstated: it IS a rendered-output text
    change (it would break a consumer asserting on rendered strings); it is not a *semantic* change.
    Minor, but "cosmetic only" undersells it.

- **Ref4 `runs:false`: HONEST and conservative.** The after/ actually **builds clean AND runs to
  completion** — it doesn't crash. It's marked `false` because behavior diverges from baseline, and
  I confirmed both divergences independently: (1) `sk_live_SECRET` leaks on the native console sink
  (same bug as Target 2), (2) the `/health/live` line is NOT dropped (no `LoggerConfiguration.Filter`
  fluent wiring). Marking it `false` is the honest call, not an overstatement. The in-code comments
  in `Ref4.Filtering/after/Program.cs` are candid about both.

- **Ref1 "Output semantically identical to baseline": TRUE at the level/message layer, OVERSOLD at
  the format layer.** Levels (INF/WRN/ERR) and message properties match line-for-line. But the
  console FORMAT is materially different:
  - Serilog: `[23:58:51 INF] Worker starting...` (local time, Serilog default template)
  - Herald: `INF:2 2026-06-02T...+00:00  - Worker starting...` (UTC ISO, numeric level suffix)
  - `{StartedAt}` also renders with a different DateTimeOffset culture (`06/02/2026 04:58:51 +00:00`
    vs `6/2/2026 4:58:55 AM +00:00`).
  Any app with log-scraping/alerting regexes keyed on the `[HH:mm:ss LVL]` template would break.
  "Semantically identical" is defensible for level+message; it undersells a real console-format
  divergence. Diff confirms `filesChanged:2, linesChanged:5, zeroSourceChange:false` — accurate.

- **Ref2: HONEST.** Diff confirms `UseSerilog` + `UseSerilogRequestLogging` carry over, but TWO
  source edits were required (`CreateBootstrapLogger()` → `CreateLogger()`,
  `CloseAndFlushAsync()` → `CloseAndFlush()`) plus the namespace swap and the added
  `using Herald.OSS.Serilog.Settings;`. `zeroSourceChange:false, linesChanged:7` is accurate.

Falsification attempt: I tried to find a project the report calls a faithful success that isn't. I
could not. The two caveats above are parity-wording softness, not dishonesty. The report records
the leak and the filter gap openly rather than hiding them.

## Target 4 — Edges: PARTIAL

- **AspNetCore renamed/zero-edit variant: DOES NOT EXIST. PRD goal UNMET.** The PRD's locked
  decision says "extend the rename to the AspNetCore compat assemblies so all four projects are
  zero-edit." That did not happen. `src/Compatibility/Layer2/Serilog.AspNetCore/` still has
  `<AssemblyName>Serilog.AspNetCore</AssemblyName>` with NO `IsPackable`/`PackageId` — it is a
  **bin-swap** mirror, not a renamed NuGet package. There is no `MMP.Herald.Compat.Serilog.AspNetCore`
  in the feed (only the Layer-1 find-replace `MMP.Herald.Serilog.AspNetCore`). So `UseSerilog`
  migrates via **find-replace only** (namespace swap + the two API parity edits), NOT zero-source.
  Richard's results are honest about this per-project (Ref2 = find-replace), but the PRD's
  "all four zero-edit" objective is not achieved and should be flagged to Steve as open.

- **Seq / strong-name wall: CONFIRMED true, by identity reasoning (no Seq install needed).** The
  compat assembly is `PublicKeyToken=null` (unsigned — confirmed empirically in the CS0433 output).
  Real Serilog is `PublicKeyToken=24c2f752a8e58a10` (strong-named). A precompiled community sink is
  compiled against the strong-named identity; the CLR binds by full identity including public-key
  token, so it cannot resolve to the unsigned mirror. The wall is real and unchanged. Reasoned from
  assembly identity, not from a Seq runtime test — which is the correct way to establish it.

- **AOT/trim on the renamed package: essentially CLEAN.** `dotnet publish -c Release -r win-x64
  /p:PublishTrimmed=true` on the t1 consumer: publish **succeeded** (produced `t1.exe`), exactly
  **one** IL2026, and it originates in Herald.OSS Core's `MMP.Herald.Enrichers.ExceptionDetailEnricher`
  (an annotated, opt-in `RequiresUnreferencedCode` path — `WithExceptionDetails()`), **not** the
  renamed compat assembly. Zero trim/AOT warnings attributable to `MMP.Herald.Compat.Serilog` itself.

## What Richard overstated or missed

1. **PRD "all four zero-edit" is unmet** — the AspNetCore rename was not done; Ref2 is find-replace.
   This is the one place the *plan's* stated goal diverges from what shipped. Flag for Steve.
2. **Ref1 "semantically identical"** undersells a real console-format divergence (template + UTC/ISO
   + level suffix + DateTimeOffset culture). Fine at the level/message layer; not at the text layer.
3. **Ref3 "Not a behavior change"** for string-quoting is slightly overstated — it's a rendered-text
   change, just not a semantic one.
4. **Finding class names** (`SerilogDestructuringApplicator`/`SerilogSinkAdapter`) don't match the
   tree; the line citation is right. Cosmetic, but fix before publishing the finding.

None of these change the headline: the renamed-package technique works, and the security leak is
real. The migration results are honest. Two parity claims need a one-word softening.

## Reproduction artifacts (temp, off-repo — safe to delete)

- `C:\Users\smuch\jared-verify\t1-consumer` (net10), `t1-net9`, `t1-coexist` (CS0433), `t1-aot` (trim)
- `C:\Users\smuch\jared-verify\t2-leak` (the security repro, both sink paths)

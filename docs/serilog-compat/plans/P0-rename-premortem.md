# P0 Rename Wave — the-fool Pre-Mortem (the GATE)

- **Date:** 2026-05-29 · **Branch:** `feat/serilog-compat`
- **Mode:** Pre-mortem + second-order thinking (the-fool). Caller pre-defined the deliverable; mode selection skipped.
- **Scope:** The mechanical rename `info→information`, `warn→warning`, `critical→fatal`, `trace→verbose` (level keys + display names) across ~40 files in three repos — Herald.OSS (`E:\dev\Herald.OSS`), Dashboard SPA (`E:\dev\herald\Modules\Dashboard`), DemoApp seeds. Herald's extra levels `notice`/`success`/`security`/`metric` are KEPT.
- **The premise of a pre-mortem:** the sweep already failed. It is half-applied at the moment of failure, it compiled, it passed a casual smoke test, and it is *silently* corrupting or dropping data on the wire or in storage. This doc enumerates *where* that half-applied state bites, ties each risk to the P0 task that mitigates it, and FLAGS any risk no task covers.

---

## GATE VERDICT — unmitigated risks (read this first)

The plan in `2026-05-29-P0-rename-wave.md` covers most hazards. The pre-mortem surfaced **four risks that no current P0 task mitigates**. Each is a *silent* failure (compiles, no throw, passes smoke test). **The sweep should not start until the controller decides on these — either add a mitigating step or accept the risk in writing.**

| # | Unmitigated risk | Why no task covers it | Suggested fix |
|---|---|---|---|
| **U-1** | **Non-level literals collide with level literals and get blind-swept.** `tone: "info"` (toast tone), `<Icon name="info" />` / `name="warn"` (Material glyph names) in the SPA, and the OTLP `"trace"`/`"info"` severity-TEXT map vs the W3C trace-id field — all share the exact string a level sweep targets. Task 5 Step 1 says "triage" for Herald.OSS `src`/`native` only. **Nothing scopes the same triage to the Dashboard SPA, and the OTLP severity-text map is a deliberate KEEP that the grep list will surface as a false positive.** | Task 5 triage is scoped to `src native` (Herald.OSS). Task 7 (Dashboard) gives no triage discipline — it says "make parsing alias-tolerant" then "flip emission," with no instruction to separate level-keys from icon-names/toast-tones. | Add a Task-7 Step-0: enumerate every `"info"/"warn"/"critical"/"trace"` hit in the SPA source and tag each as LEVEL / ICON-NAME / TOAST-TONE / COMMENT. Sweep only LEVEL. Add an explicit KEEP note for `OtelLogRecord.cs`'s severity-text map and the W3C trace-id field. |
| **U-2** | **The Dashboard ships a pre-built minified bundle (`wwwroot/assets/index-*.js`); editing `.jsx` source without rebuilding leaves the OLD keys live in the served artifact.** The server can emit `information` while the *actually-served* SPA bundle still parses `info` — the source diff looks complete, the runtime is broken. | Task 7 Step 4 says "verify end-to-end against a running server" but never says **rebuild the SPA bundle and confirm the served `wwwroot` artifact is the new one** (hash-named file changes). A reviewer reading the `.jsx` diff sees green. | Add to Task 7: an explicit "rebuild SPA, confirm new `index-<hash>.js` is served, old hash is gone" step before the end-to-end verify. The end-to-end test (U-2's only real catch) must hit the *served bundle*, not the source. |
| **U-3** | **No "old key fully eradicated from product code AND served artifacts" gate exists *before* Task 9 removes the alias map.** Task 10 Step 3 greps for residue, but Task 10 runs *after* Task 9. If any product path still *emits* an old key when the alias map is deleted in Task 9, that path now produces an event that is rejected at ingest (no-level-reject) or silently mis-rendered — and the regression only shows under the specific level. | Task 9 Step 4 says "confirm Task 1 + G-LEVEL.1 pass" but those test the *tables and resolver*, not "does any live emitter still write `info`." The residue grep that would catch a lingering emitter is in Task 10, sequenced *after* the alias removal. | Move the residue grep (Task 10 Step 3) to be a **precondition of Task 9** — alias map removal is gated on "zero old-key emission in product code AND the served SPA bundle." Removing the bridge before the far bank is reached is the textbook half-applied failure. |
| **U-4** | **The `critical→fatal` display/sort/severity-rank may desync.** `critical` historically sorts/colors as the top severity. `fatal` is a *new* key. If the level's numeric rank, the console theme entry, the SPA severity-color map, and the OTLP severity-number map are not all moved together, `fatal` events render with a default/missing color or sort at the wrong position — visible only on a `fatal` event, which a smoke test rarely emits. | Task 3 fixes the *tables* and the `Fatal` drift. Task 6 fixes the *wire key*. **No task asserts the rank/color/severity-number for `fatal` is identical to what `critical` had** — the rename must preserve ordinal + presentation, not just the string. | Add an assertion to the G-LEVEL suite (Task 8): `fatal` has the same numeric rank and a defined theme/color entry that `critical` had; OTLP severity-number `21` still maps. Pin `BuiltInConsoleThemes.cs` and the SPA color map as sweep targets in Task 5/Task 7 explicitly (they are level-keyed presentation, easy to miss). |

**Bottom line:** U-1 and U-4 are *missing-scope* findings (a task exists but its triage/assertion is too narrow). U-2 and U-3 are *missing-step* findings (no task does this at all). All four are silent. Resolve before Glenn sweeps.

---

## Ranked failure narratives (the pre-mortem proper)

Each narrative is written from the post-failure vantage: "it shipped, and here is the bug report two weeks later." Ordered by blast radius × silence.

### F-1 — The two-table drift swaps which table is wrong (CRITICAL · mitigated by Tasks 1+3)

**The failure:** Today three tables disagree — `KnownLogLevels.cs` (runtime objects), `KnownLogLevelKeys.cs` (analyzer, netstandard2.0), `LogLevelKeys.cs` (runtime constants, which *already* carries a stray `Fatal="fatal"` next to `Critical="critical"`). A mechanical sweep updates the table a developer is *looking at* and misses one of the other two. Post-sweep, the analyzer table says `fatal`, the runtime object table still says `critical` (or vice-versa). The `[HeraldLog]` source generator emits code keyed to one table; the runtime resolves against the other. Events compile, route, and then fail to color/sort/filter because the analyzer-blessed key has no runtime object.

**Why it's silent:** the source generator runs at build, the runtime resolves at log-time — they never share a test today (that's the *root cause* of the existing drift). Nothing fails the build.

**Mitigated by:** Task 1 (`LevelTableEquivalenceTests` — references *both* tables in one suite, the first time they can see each other) + Task 3 (renames all three and deletes the stray `Fatal`). **Mitigation is real and direct.**

### F-2 — `critical` vanishes because `fatal` never existed (CRITICAL · mitigated by Tasks 2+8)

**The failure:** `critical→fatal` is unique among the four renames: it targets a key that *did not exist before* (`fatal`). Old persisted pipeline JSON, the replay-ring buffer mid-flight, and any in-flight SSE event carrying `"critical"` have no home after the table flips. If the resolver does an exact-match lookup, `critical` resolves to nothing → the event is rejected at ingest (project policy rejects no-level events) or, worse, defaults to Information. A whole severity class quietly disappears from storage.

**Why it's silent:** `critical`/`fatal` events are rare; a smoke test logs Info and maybe an Error. The drop shows up only when something actually goes critical — the worst time to lose the log.

**Mitigated by:** Task 2 (transitional alias map: `critical→fatal` canonicalized at the resolve entry point) + Task 8 G-LEVEL.1 (`critical` resolves to `fatal`, `Assert.NotNull` — "survives ingest, not vanished"). **Mitigation is real.** *Caveat:* the alias only protects while it exists — see F-6.

### F-3 — The wire splits: server emits `information`, SPA parses `info` (HIGH · partially mitigated; see U-1, U-2)

**The failure:** The SSE→SPA wire is live during the deploy window. Task 6 flips the server emitter to `information`. If the SPA (Task 7) hasn't flipped its parser — or has flipped the *source* but not the *served bundle* (U-2) — every event arrives with a level the SPA doesn't recognize. Per project policy the viewer must not default a missing severity to Information; the honest outcome is a `(no level)` marker on every row, or a render crash on a field-shape the SPA never expected.

**Why it's silent at the source level:** the `.jsx` diff looks done. The served minified bundle is a different artifact (U-2). A reviewer reading the PR sees new keys everywhere and approves.

**Mitigated by:** Task 7 Step 1 (make SPA parsing alias-tolerant *first* — accept both old and new — the safe intermediate) + Task 6/Task 7 lockstep + Task 7 Step 4 (end-to-end against a running server). **Partially mitigated:** the alias-tolerant-parse-first ordering is correct and defuses the window. **GAP:** nothing forces the *served bundle* to be rebuilt/verified (U-2), and nothing separates level-keys from icon-names/toast-tones in the SPA sweep (U-1).

### F-4 — `trace` the level vs `trace` the trace-id (HIGH · mitigated for OSS by Task 5; FLAGGED for SPA by U-1)

**The failure:** `OtelLogRecord.cs` uses `"trace"` two ways: as OTel severity *text* (`["trace"] = 1`) that maps to a level, and as the W3C **trace-id** field (32 hex chars). A blind `trace→verbose` replace either rewrites the severity-text key (which must stay `"trace"` because that's the OTel wire spelling, NOT Herald's level key) or — far worse — mangles a comment/field name near the trace-id. The OTel severity *text* is an external wire contract; it is not Herald's level key and must not be swept. The same collision lives in the SPA as `Icon name="info"` and `tone: "info"`.

**Why it's silent:** OTLP ingest still parses; the severity just maps wrong, or an icon silently fails to render. No throw.

**Mitigated by:** Task 5 Step 1 explicitly calls out "some `trace` hits are OpenTelemetry trace-id, NOT level. Do NOT blind-replace" and mandates file-by-file triage for Herald.OSS `src`/`native`. **For Herald.OSS this is mitigated.** **FLAGGED (U-1):** the same triage discipline is not extended to the Dashboard SPA, where `Icon name`/`tone` collisions are live, and the OTLP severity-text KEEP is not written down as an explicit non-sweep.

### F-5 — Presentation desyncs: `fatal` renders colorless / sorts wrong (MEDIUM · FLAGGED by U-4)

**The failure:** `BuiltInConsoleThemes.cs` has level-keyed theme entries (`"trace"`, `"info"`, `"warn"`, `"critical"` → styles). `LiveLogCapture.cs` maps `"critical" => "crimson"` and treats `critical` as an alert level. The SPA has its own severity-color map. If the table renames to `fatal` but a theme/color map still keys on `critical`, `fatal` events fall through to a default style — uncolored, possibly mis-sorted if the numeric rank didn't move with the key. Only visible on a `fatal`/`verbose` event.

**Why it's silent:** color is cosmetic until an operator is scanning a console at 2am for the crimson row that isn't crimson.

**Mitigated by:** Task 5 sweeps OSS literals (themes are in scope as `src` files) — *but* the plan doesn't name `BuiltInConsoleThemes.cs`/`LiveLogCapture.cs` as level-keyed presentation specifically, and **no task asserts rank+color parity** between old `critical` and new `fatal`. **FLAGGED (U-4):** add the parity assertion to Task 8 and name these files as Task-5 sweep targets.

### F-6 — The alias map is removed while a back-channel still emits old keys (MEDIUM · FLAGGED by U-3)

**The failure:** Task 2 installs the alias bridge; Task 9 removes it. Between them, if *any* product path still emits `info`/`critical` (a missed file in the ~19, a code path the grep didn't catch, a serialized default), the alias map silently absorbs it for the whole sweep — so tests stay green and nobody notices the lingering emitter. Then Task 9 deletes the bridge and that path's events start getting rejected loud (correct behavior) or silently mis-handled — in production, post-merge, with no test pointing at it.

**Why it's silent until it isn't:** the alias map's job is to hide exactly this. Its removal is when the hidden defect surfaces.

**Mitigated by:** Task 10 Step 3 residue grep — **but that runs after Task 9** (U-3). The grep that proves "no old-key emission remains" must be a *precondition* of removing the bridge, not a postscript. **FLAGGED (U-3):** move the residue gate ahead of Task 9.

### F-7 — DemoApp seed and sample logs drift from the renamed core (LOW · mitigated by Task 7)

**The failure:** DemoApp seeds and the `SampleLogs/game-server_*.log` fixtures (81 hits each) carry old keys. If the core renames but the seed/sample still emits `info`, the first-impression DemoApp shows old keys flowing through a new-key pipeline — saved by the alias map, so it *works*, but it teaches the wrong vocabulary and leaves old keys in a shipped artifact.

**Why it's low:** the alias map covers it functionally; it's a vocabulary/cleanliness issue, not a data-loss one — until the alias map is removed (then it becomes F-6).

**Mitigated by:** Task 7 Step 3 (update DemoApp seed; confirm `critical→fatal`, `trace→verbose`). Sample-log fixtures should be added to the Task 5 triage list (they're test data, not product emission, but they model the vocabulary). **Adequately mitigated** if sample logs are explicitly included.

---

## Second-order effects (the failure after the failure)

- **The alias map becomes load-bearing.** F-6's deeper risk: if the sweep is incomplete and *nobody notices because the alias map hides it*, the "transitional" map quietly becomes permanent infrastructure. The plan's Task 9 (remove it) is the forcing function that prevents this — which is exactly why U-3 (gate the removal properly) matters. Removing the bridge is the test that the sweep was real.
- **A reviewer with no iteration history sees a clean diff.** Per the project's first-look-reviewer rule, the person reviewing the PR has no memory of the old keys. They cannot catch "this file was missed" by feel — the equivalence test (Task 1) and the residue grep (U-3) are the only mechanical catches. This raises the stakes on making both gates airtight.
- **Cross-repo merge-order is itself a hazard.** If Herald.OSS (Tasks 5–6) merges before the Dashboard SPA (Task 7), the deploy window has a new-key server against an old-key SPA. The plan's self-review note already says "Tasks 7 spans Dashboard + DemoApp — must land in the same wave." The alias-tolerant-parse-first ordering (Task 7 Step 1) is what makes the window survivable. Keep that ordering non-negotiable.

## Inversion check — what would make this sweep *succeed* silently?

Invert the pre-mortem: what does a *clean* outcome require?
1. All three tables move together and a test proves it (Task 1+3). ✔ covered.
2. Every old key resolves during the window and nothing vanishes (Task 2+8). ✔ covered.
3. The wire never splits — SPA parses both keys before the server flips, and the *served bundle* is rebuilt (Task 6+7 ordering). ◐ ordering covered, bundle-rebuild GAP (U-2).
4. No non-level literal is swept — OSS triage + SPA triage + OTLP KEEP (Task 5). ◐ OSS covered, SPA + OTLP-KEEP GAP (U-1).
5. The bridge is removed only after the far bank is confirmed reached (residue grep precedes Task 9). ✗ GAP (U-3).
6. `fatal`/`verbose` carry the rank+color their predecessors had (parity assertion). ✗ GAP (U-4).

Items 3–6 are where the silent failures live. 1–2 are solid.

---

## Risk → mitigating task map (the required deliverable)

| Risk | Severity | Mitigating P0 task | Status |
|---|---|---|---|
| F-1 Two-table drift swaps which table is wrong | CRITICAL | Task 1 (equivalence test) + Task 3 (rename all three + delete stray `Fatal`) | MITIGATED |
| F-2 `critical` vanishes (`fatal` never existed) | CRITICAL | Task 2 (alias map) + Task 8 G-LEVEL.1 | MITIGATED |
| F-3 Wire splits (server `information` / SPA `info`) | HIGH | Task 7 Step 1 (alias-tolerant parse first) + Task 6/7 lockstep + Task 7 Step 4 | PARTIAL — see U-1, U-2 |
| F-4 `trace` level vs trace-id / icon-name / toast-tone | HIGH | Task 5 Step 1 triage (Herald.OSS only) | PARTIAL — OSS mitigated; SPA + OTLP-KEEP **FLAGGED U-1** |
| F-5 `fatal` renders colorless / sorts wrong | MEDIUM | Task 5 sweep (themes in `src`) | **FLAGGED U-4** — no rank+color parity assertion; theme files not named |
| F-6 Alias map removed while old emitter survives | MEDIUM | Task 10 Step 3 residue grep (runs *after* Task 9) | **FLAGGED U-3** — gate is mis-sequenced |
| F-7 DemoApp seed / sample logs drift | LOW | Task 7 Step 3 | MITIGATED (include sample-log fixtures in Task 5 triage) |

### Unmitigated-risk roll-up (the gate)

- **U-1** (F-3, F-4) — SPA triage + OTLP severity-text KEEP not in any task. **Add Task-7 Step-0 triage + explicit OSS KEEP note.**
- **U-2** (F-3) — served SPA bundle rebuild/verify not in any task. **Add to Task 7 before end-to-end verify.**
- **U-3** (F-6) — residue grep must *precede* alias-map removal. **Re-sequence: make it a precondition of Task 9.**
- **U-4** (F-5) — no rank+color parity assertion for `fatal`/`verbose`. **Add to Task 8; name `BuiltInConsoleThemes.cs` + `LiveLogCapture.cs` + SPA color map as Task-5/7 targets.**

---

## Synthesis — strengthened position

The plan's spine is sound: the alias map (Task 2) is the right bridge, the cross-table equivalence test (Task 1) attacks the real root cause, and the alias-tolerant-parse-first ordering (Task 7 Step 1) is the correct way to survive the deploy window. The `critical→fatal` trap and the two-table drift — the two CRITICAL risks — are genuinely covered.

The gaps are all in the *edges of the sweep*: the literals that look like levels but aren't (U-1), the artifact that isn't the source (U-2), the bridge removed a beat too early (U-3), and the presentation parity that the string-rename doesn't carry (U-4). None of these crash. All of them ship green. That is exactly the class of failure a pre-mortem exists to surface.

**Recommendation:** treat U-1…U-4 as plan amendments to land *before* Glenn's sweep. Two are new steps (U-2, U-3), two are scope-tightenings on existing steps (U-1, U-4). None are large. With them in, the sweep is gated end-to-end.

# Cross-Plan Reconciliation — Serilog-Compat Plan Set

- **Date:** 2026-05-29 · **Branch:** `feat/serilog-compat`
- The nine sub-plans (P0–P8) were authored in parallel. This doc is the **single source of truth for cross-plan contracts**: canonical type names, resolved name-drift, and the open decisions. **Every execution agent reads this alongside its plan.** Where a plan names a shared type differently from the Canonical column below, the canonical name wins.

## Canonical shared types (P1 owns these; everyone else references)

| Concept | Canonical name (P1) | Plans that consume it | Drift to fix |
|---|---|---|---|
| Bidirectional level map | `SerilogLevelMap` | P2, P6, P7 | **P2 wrote `LogEventLevelMap.ToHeraldKey/ToHeraldLevel/ToSerilog` → rename to `SerilogLevelMap`** and align method names to P1's actual surface |
| Value-model tree projector (the mirror's public entry) | `LogEventValueProjector` | P3, P4, P7 | **P4's S2/S5 reference `LogEventValueProjector`** (was "the mirror's projection entry, TBD"). **P3's internal `ISerilogEventView` adapts over `LogEventValueProjector`** (does not duplicate it) |
| Serilog-hole-named arity generator | `SerilogArityGenerator` | P7 (mirrors output) | none |
| Per-hole capture-mode index | `SerilogTemplateHoleIndex` | P3, P4 (capture-mode routing) | none |
| Capturing test logger | `TestLoggers.CreateCapturing` | P2, P3, P4, P5, P6 | none — built in P1 |
| Mirrored level enum | `LogEventLevel` in `MMP.Herald.Serilog.Events` | all | none |

## Resolved decisions (derived from the committed design — execute as written)

1. **Layer-1 namespace = `MMP.Herald.Serilog.*`** (Richard §A: Layer 1 is the Herald-namespaced source-compat layer; one-`using`-swap. Layer 2 = bare `Serilog.*`). So P1 types live at `MMP.Herald.Serilog.Events.LogEventLevel` etc. *(Resolves P1 OD-2, informs P6 OD-1, P7.)* Consumer-facing **extension methods** that must be discoverable (`UseSerilog`, `ReadFrom.Configuration`) live in the conventional consumer namespace (`Serilog`, `Microsoft.Extensions.Hosting`) even while the assembly is `MMP.Herald.Serilog.*` — standard extension-method placement, not a type-identity change. *(Resolves P6 OD-1.)*
2. **`LogEventValueProjector` is the one public projector.** P3's `ISerilogEventView` is a thin internal adapter over it, not a second projection path. *(Resolves P3 OD-1, P4 OD-1 in favor of "P1 exposes the projector"; S5 reuses it rather than throwing — see Open D-3 for the raw-policy edge.)*
3. **CLEF parity = field/value parity, not byte parity** (two independent JSON writers won't guarantee byte order). *(Resolves P3 OD-4.)*
4. **Registry collision = throw** (`SinkResolutionException` / registration-collision pinned by test). *(Resolves P5 (4).)*
5. **Extract the shared `BufferSizeFor` helper** between Herald's native generator and `SerilogArityGenerator` (DRY; the buffer-size mapping is identical). *(Resolves P1 OD-4.)*
6. **P0 build/test commands:** Herald.OSS has **no local `build.sh` and no `.sln`** — use `dotnet build Herald.OSS.csproj` and `dotnet test tests/Herald.OSS.Tests.csproj`; full-tree builds run the umbrella `build.sh` at `E:/dev/herald`. (P0 Task 10 patched.)

## Open decisions — Richard to ratify (leans noted; do NOT block P0)

| # | Decision | Affects | Lean |
|---|---|---|---|
| R-1 | `Log.Logger` static slot: live in Layer-1 `MMP.Herald.Serilog` behind a volatile holder, or its own tiny `StaticFacade` assembly so the DI-pure path never references it | P1, P6 | Layer-1 volatile holder (simpler; isolate only if a consumer needs the DI-pure guarantee) |
| R-2 | **`Serilog.Core.Logger`** concrete type that `.CreateLogger()` returns and corpus code stores in fields — define the Layer-1 twin and assign owner | P1/P2, P7 | P2 owns it (it's the `.CreateLogger()` return); P1's `ILogger` is the interface it implements |
| R-3 | The build→logger adapter P2 flagged as `SerilogLoggerAdapter.FromBuild` — confirm name + owner | P1, P2 | P1 owns the adapter (it bridges `StructuredLogger`→Serilog `ILogger`); P2 calls it |
| R-4 | P2↔P5 boundary: does P2 expose a `ReadFrom` accessor + a per-source-context entry for `MinimumLevel.Override`? If not, P5 must fail loud, not silent-drop | P2, P5 | P2 exposes both (Override → kernel-fast `WithFastDynamicLevel`, not the legacy path) |
| R-5 | `ApplicationStopped` flush hook owner (double-flush risk) | P1, P6 | P6 registers it; P1's `CloseAndFlush` is idempotent |
| R-6 | `WriteTo.File` rolling-file args in P2 v1 or fast-follow | P2 | v1 (common Serilog config) |

## Open decisions — Steve's call (product/semantic; leans noted)

- **S-1 — the `.Level` extra-levels gap (real semantic gap, P1 OD-3).** When a Serilog-shaped consumer reads `.Level` (a 6-value `Serilog.LogEventLevel`) on an event Herald emitted at one of its *extra* levels (`notice`/`success`/`security`/`metric`), what do they see? Only happens in mixed environments (a pure Serilog drop-in never produces these). **Lean:** map each to its nearest Serilog level for the `.Level` read (`notice/success/metric→Information`, `security→Warning`) **and** preserve the true Herald level in a property so nothing is lost; document it. Richard ratifies the exact mapping.
- **S-2 — `{Timestamp}` UTC vs local (P3 OD-3).** Serilog renders local by default; Herald stores `TimeUtc`. **Lean:** match Serilog (render local) for output-template parity, since the whole point is drop-in equivalence; keep UTC available via the format specifier.
- **S-3 — on-disk config migration after the alias map is removed (P0 Task 9, P8).** Must old persisted configs (old level keys) still load, or are they must-migrate? **Lean:** a one-time read-side migration shim in the config loader (accept old keys, rewrite to new on save) so existing deployments don't break — distinct from the *wire* alias map (which is removed). Richard confirms.

## Critical sequencing note

**P0 (rename wave) depends on NONE of the open decisions above** — it is self-contained (Herald.OSS + Dashboard + DemoApp level rename). It can start now. All the open decisions are P1+ concerns and can be settled (by Richard, or by the leans) in parallel with P0 execution. Do not block P0.

---

## RATIFIED DECISIONS (Richard, 2026-05-29) — authoritative; supersedes the two open-decisions sections above

- **R-1 — `Log.Logger` home:** Layer-1 volatile holder at `MMP.Herald.Serilog.Log` (default `SilentLogger.Instance`, never NRE). **No** separate `StaticFacade` assembly (YAGNI; Guard-1 reference-direction test already proves the engine can't reach it). Owner **P1 Task 7**.
- **R-2 — concrete `.CreateLogger()` return type:** `SerilogLoggerAdapter` (P1, `IDisposable`, implements `MMP.Herald.Serilog.ILogger`). Do **not** add a Layer-1 `Serilog.Core.Logger`; that name is a **P7 Layer-2** type-forward/alias to `SerilogLoggerAdapter`, zero behaviour. **DRIFT-FIX: P2's `.CreateLogger()` returns `MMP.Herald.Serilog.ILogger`, NOT `global::Serilog.ILogger`** (the P2 Task 2 sketch is wrong — rebind).
- **R-3 — build adapter:** `SerilogLoggerAdapter.FromBuild(PipelineBuildResult)` (P1 owns; mirrors `QuickLogResult.FromBuild`). **PREREQ: P1 Task 0 adds `[assembly: InternalsVisibleTo("MMP.Herald.Serilog")]` to Herald.OSS** — `PipelineBuildResult.Logger` is `internal`; without the IVT, P2 Task 2 won't compile. Reject widening the engine's public surface.
- **R-4 — P2↔P5 boundary:** P2 **declares** the `LoggerConfiguration.ReadFrom` accessor (anchored on the single `QuickLogBuilder` root); P5 **implements** `ReadFrom.Configuration`. If absent, P5 **fails loud at construction**, never silent-drop. `MinimumLevel.Override` routes through **`WithFastDynamicLevel(switch, CategoryLevelSwitchMap)` (kernel-fast) ONLY** — never the legacy `DynamicLevelPolicy` path (it disqualifies the kernel). **PIN a regression test:** any `.Override` config produces `FastPathDynamicLevel.CategoryOverrides`, never a `DynamicLevelPolicy` block.
- **R-5 — flush hook:** **P6** registers `IHostApplicationLifetime.ApplicationStopped` → calls **P1's** `Log.CloseAndFlush()`. P1's `CloseAndFlush` is idempotent via `Interlocked.Exchange(ref _logger, SilentLogger.Instance)`. **PIN:** double-`CloseAndFlush` test (no throw; live sink flush/dispose runs exactly once). Owner P1 Task 7 (mechanism) + P6 Task 3 (trigger).
- **R-6 — `WriteTo.File` rolling:** **v1**, P2 Task 3. Map only natively-supported rolling params (`rollingInterval`, `fileSizeLimitBytes`, `retainedFileCountLimit` if present on `WithFileSink`); any unsupported rolling arg is an honest hard-wall named in the P8 parity audit, never silently dropped.
- **S-2 — `{Timestamp}`:** render **local** by default (`.ToLocalTime()` over the stored `TimeUtc` `DateTimeOffset`); UTC via format specifier (`{Timestamp:u}`). Owner **P3**. (Richard owns this — engineering parity, no Steve flag.)

### Two decisions Richard explicitly handed to Steve (pending)

- **S-1 — `security` level's `.Level` mapping.** Mechanism settled: nearest-Serilog-level on read + true Herald level preserved in a property (in `LogEventValueProjector`, P1 Task 8). `notice`/`success`/`metric` → `Information` (ratified). **`security` → `Warning` (Richard's lean, fail-toward-visibility) vs `Information` (avoid alert-noise) is STEVE's product-posture call.** Whichever he picks is pinned as a P8 G-GAP test; property-preservation is settled regardless.
- **S-3 — old on-disk configs after alias removal.** **Trap Richard caught:** the naive S-3 lean would silently revert P0 Task 9 (you can't both loud-reject AND accept old keys at the same boundary). Resolution if built: a **separate** one-time read-side normalizer at the config-file-load boundary (`LoggingJsonSerializer`/`DefaultLoggingConfigurationMapper`) — NOT a revived alias map; **P0 Task 9's wire-side loud-reject (G-LEVEL.5) stands unchanged.** **Whether to build the shim at all is STEVE's compatibility-promise call** (do real persisted old-key configs exist in the wild?). Richard leans build-it (cheap, deletable); if must-migrate, ship a documented note + a loud actionable startup error. Owner P8. Does NOT block P0 Tasks 1–8; only gates Task 9's optional normalizer.

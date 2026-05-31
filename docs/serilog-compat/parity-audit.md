# Parity Audit — Herald.OSS Serilog Compatibility

- **Date:** 2026-05-30 (P7 draft; extended by P8 Task 1 — full friction map with ranked tags)
- **Branch:** `feat/serilog-compat`
- **Updated by:** P8 Task 1 — complete tag assignment, population ranks, regression test refs; P7 gap additions retained
- **Status:** Draft — sections marked `<!-- FILL AFTER P-n -->` require shipped artifacts before finalization (Task 4 in P8)

---

## What this is

A friction map, not a defect list. Every Serilog public surface tagged against its Herald status, ordered so the gap that blocks the most real Serilog users appears first. The goal is to let a Serilog team decide quickly whether Herald drops in, then migrate each gap with a named path instead of a fork.

---

## How to read it

Three tags:

- **`carries-over`** — source-compatible on recompile. Change the package reference and rebuild; the surface works unchanged.
- **`maps-to-equivalent`** — different name or package shape, same behavior. One documented change at the config or call site; behavior is equivalent.
- **`hard-wall`** — structural boundary. No drop-in path. Named alternatives exist; there is no workaround that preserves the original community package binding.

**Population rank** reflects how many production Serilog apps use the surface: `very high`, `high`, `medium-high`, `medium`, `medium-low`, `low`. The friction map is ordered so the highest-rank items appear first. High-rank gaps block the most users on day one.

---

## The honest claim

> *"Swap the package, rebuild, and your standard Serilog code runs on Herald — popular sinks map over; the third-party-sink ecosystem and the expression DSL are a documented boundary."*

Source compatibility on recompile. Not binary identity. Herald does not have Serilog's strong-name key and will not spoof it.

Full wording and usage rules: `docs/serilog-compat/honest-claim.md`.

---

## Friction map

Ordered by population rank — the gap that blocks the most real Serilog users appears first.

| Serilog surface | Tag | Herald equivalent | Population rank | Regression test |
|---|---|---|---|---|
| Instance `ILogger` verbs + static `Log` facade | `carries-over` | `MMP.Herald.Serilog.ILogger` + `Log` | very high | G-CORPUS.1 |
| Message templates (`{Named}`, positional, `{{`/`}}` escaping) | `carries-over` | Same grammar | very high | G-HOT.3 |
| `LogEventLevel` map (Verbose→Trace, Information, Warning, Error, Fatal→Critical, Debug) | `carries-over` | `SerilogLevelMap` | very high | G-LEVEL.1 |
| `LoggerConfiguration` code config (`MinimumLevel.*`, `WriteTo.*`, `Enrich.*`, `CreateLogger`) | `carries-over` | P2 `LoggerConfiguration` shim → `QuickLogBuilder` | very high | G-CORPUS.1 |
| `appsettings.json` — `ReadFrom.Configuration(IConfiguration)` | `carries-over` | `Herald.OSS.Serilog.Settings.Configuration` (P5, Apache-2.0) | very high | G-CORPUS.2 |
| ASP.NET — `UseSerilog(...)` / `AddSerilog(...)` / `UseSerilogRequestLogging()` | `carries-over` | `MMP.Herald.Serilog.AspNetCore` (P6) | very high | G-CORPUS.3 |
| Popular sinks (Console / File / Elasticsearch / OTLP / HTTP / TCP / UDP / Null) | `maps-to-equivalent` | Herald built-in sinks | very high | G-SINK-WALL.1 (positive) |
| Sink/enricher by name in `appsettings.json` (`"Using"` / `"Name"` resolution) — S-NEW-1 | `maps-to-equivalent` | `LoggerSinkRegistry.RegisterSink("MyName", ...)` | high | G-CORPUS.2 |
| `ForContext(...)` / `PushProperty(...)` | `carries-over` | `ForContext` (renamed P0) / `BeginScope` | high | G-CORPUS.4 |
| Custom user-authored `ILogEventSink` (source-compiled only — S1) | `maps-to-equivalent` | `WriteTo.Sink(source-compiled)` via adapter | high | G-CORPUS.4 |
| Custom `ILogEventEnricher` (source-compiled — S2) | `maps-to-equivalent` | `Enrich.With(...)` via adapter | high | G-CORPUS.4 |
| Output-template grammar (`{Level:u3}`, `{Message:lj}`, `{Timestamp:HH:mm}`, `:lj`) | `maps-to-equivalent` | `SerilogOutputTemplateRenderer` (P3) | high | G-GAP.1 |
| Custom `IDestructuringPolicy` — `ByTransforming<T>(Func)` + raw policy form (S5) | `maps-to-equivalent` | `Destructure.With(...)` tree bridge | medium-high | G-SEC.1 |
| `{@Obj}` destructure / `{$Obj}` stringify inline syntax | `carries-over` | `LogPropertyCaptureMode` mapping, routed per-hole | high | G-HOT.3 |
| `AuditTo` vs `WriteTo` semantics (throw vs swallow — S9) | `maps-to-equivalent` | `AuditTo.Sink(...)` with `auditMode` bool | medium | G-SEC.2, G-SEC.3 |
| `ITextFormatter` / CLEF output format (S3) | `maps-to-equivalent` | `ITextFormatter` seam + `CompactJsonFormatter` bridge | medium | G-GAP.5 |
| `LoggingLevelSwitch` (S4) | `maps-to-equivalent` | `LoggingLevelSwitch` wrapper over `LogLevelSwitch` | medium-low | G-GAP.3 |
| Sub-loggers — `WriteTo.Logger(lc => ...)` (S6) | `maps-to-equivalent` | Nested pipeline (additive in vNext) | low | G-GAP.6 |
| `SelfLog` (S7) | `maps-to-equivalent` | `SelfLog` facade over `ISinkHealthReporter` | low | G-GAP.4 |
| Value model (`ScalarValue`, `StructureValue`, `SequenceValue`, `DictionaryValue`) | `carries-over` | Layer-1 value-model mirror | medium | G-VM.1, G-VM.2 |
| **Pre-compiled community sinks (Seq / MSSql / Datadog / long tail)** | **`hard-wall`** | No drop-in path — identity wall; see §Third-party sinks below | high | G-SINK-WALL.1 |
| **`Serilog.Expressions` string DSL** (`Filter.ByIncluding("level = 'Error'")`) | **`hard-wall`** | Predicate `Filter.ByExcluding(Func<>)` maps; string DSL does not — open RFC | medium | G-GAP.2 |

> **Migration companions:** per-gap step-by-step guides live under `docs/serilog-compat/migrations/`. See the [companion index](#per-gap-migration-companion-index) at the end of this document.

---

## Third-party sinks — the identity wall

In plain terms: .NET stamps each signed library with a cryptographic ID, and a community sink like Seq was built to accept only the library carrying Serilog's exact ID. Herald's shim does not carry that ID — we don't have Serilog's signing key and won't forge one — so the sink refuses to load against it. That refusal is the wall. The rest of this section is the precise engineering statement of the same fact.

The following is the binding technical statement on community sinks. It is reproduced verbatim from Jared's design round (the authoritative source):

> Third-party and community Serilog sinks (`Serilog.Sinks.Seq`, `.Sinks.MSSqlServer`, `.Sinks.Datadog`, and the long tail) cannot bind to the Herald `Serilog.*` shim. Each is compiled against `Serilog, PublicKeyToken=24c2f752a8e58a10` and depends on the real strong-named `Serilog.ILogEventSink`/`Serilog.Core` types. The shim is unsigned and exports types of a different assembly identity; the CLR will not satisfy the sink's `Serilog` reference with the shim. Referencing such a sink transitively loads the real `Serilog.dll`, producing duplicate `Serilog.*` types (CS0433 at compile, or InvalidCastException at runtime). This is a structural identity wall, not a deferral. Herald's own equivalents (Console/File/HTTP/TCP/UDP/Elasticsearch/OTLP/Null) cover the popular sinks; Seq and the long tail are named gaps with no drop-in path absent a strong-named signing key we do not have and will not spoof.

---

## `Serilog.Expressions` DSL — the second wall

`Serilog.Expressions` enables configuration like `Filter.ByIncluding("Level = 'Error' and @Properties.Environment = 'Production'")`. The string-DSL form is a separate parse engine that Herald does not implement.

The predicate form — `Filter.ByExcluding(e => e.Level < LogEventLevel.Warning)` — maps to Herald's processor pipeline and carries over.

The string-DSL form is named as an open problem for the OSS community. If you implement a compatible parser that runs on Herald's engine, the extension seam is available.

See [migrations/expressions-dsl.md](migrations/expressions-dsl.md) for realistic alternatives.

---

## P7-discovered gaps (added Task 8)

These gaps were identified or clarified during P7 (Layer-2 mirror implementation) and are added here so per-gap migration docs can reference them.

### Community sinks — assembly identity (hard-wall, confirmed by G-LAYER2.1)

The identity wall is structural. P7 implemented G-LAYER2.1: a test project that references both the Layer-2 shim and a real-Serilog package is expected to fail to build with `CS0433`. The test verifies the failure is a compile error, not a runtime exception. This is the enforcement mechanism for the migration runbook's cutover rule.

| Gap surface | Status | Herald equivalent | Migration path |
|---|---|---|---|
| Community sinks (Seq, MSSql, Datadog, etc.) | Hard wall — assembly identity (`PublicKeyToken=24c2f752a8e58a10` ≠ unsigned shim) | Herald Console/File/HTTP/OTLP/Elasticsearch sinks for popular targets | Map at config level; replace the package; see [migrations/third-party-sinks.md](migrations/third-party-sinks.md) |

### `Serilog.Context.LogContext` — stub, throws NotSupportedException

`LogContext.PushProperty(...)` and related methods are present in the Layer-1 and Layer-2 surfaces but are stubs that throw `NotSupportedException`. The ambient-scope pattern works; the static `LogContext` API that some codebases rely on is not yet wired.

| Gap surface | Status | Herald equivalent | Migration path |
|---|---|---|---|
| `Serilog.Context.LogContext` (`PushProperty`, `Push`, `Clone`, `Reset`, `Suspend`) | Stub — throws `NotSupportedException` | `ILogger<T>.BeginScope(dict)` on the MEL interface (wired through `HeraldLoggerProvider`) | Replace `LogContext.PushProperty("Key", value)` with a scoped `using var scope = logger.BeginScope(new Dictionary<string, object> { ["Key"] = value })`. Behavior is equivalent for structured logging; ambient propagation is per-`ILogger<T>` instance scope. Post-P7 release will implement `LogContext` natively. |

### `IWebHostBuilder.UseSerilog` — stub, throws NotSupportedException

The `IWebHostBuilder.UseSerilog(...)` extension on the older pre-3.1 host model is a stub. The generic host (`IHostBuilder.UseSerilog(...)`) is fully implemented.

| Gap surface | Status | Herald equivalent | Migration path |
|---|---|---|---|
| `IWebHostBuilder.UseSerilog(...)` | Stub — throws `NotSupportedException` | `IHostBuilder.UseSerilog(...)` | Change `WebHost.CreateDefaultBuilder().UseSerilog(...)` to `Host.CreateDefaultBuilder().UseSerilog(...)`. ASP.NET Core 3.1+ projects already use the generic host model; this is a one-line change at startup. |

### Third-party enrichers (pre-compiled NuGet packages)

Pre-compiled enricher packages built against real Serilog's `ILogEventEnricher` interface have the same assembly-identity problem as pre-compiled sinks. They cannot bind to the Layer-2 shim.

| Gap surface | Status | Herald equivalent | Migration path |
|---|---|---|---|
| Pre-compiled enricher packages (e.g. `Serilog.Enrichers.Environment`, `Serilog.Enrichers.Thread`) | Hard wall — assembly identity | Herald's seven built-in enrichers (machine name, process id, thread id, correlation id, and others) | Port the enricher from source using `Enrich.With(ILogEventEnricher)` with a source-compiled implementation. The `ILogEventEnricher` seam (S2) is fully bridged. For the standard environment/thread enrichers, Herald's built-in enrichers cover the common data; register them via `Enrich.WithMachineName()` equivalents. See [migrations/custom-enricher.md](migrations/custom-enricher.md). |

---

## Population-rank rationale

**Very high — universal blockers:**

- **Core call surface and static `Log` facade** — the log verbs and `LoggerConfiguration` builder are in every Serilog app. Any gap here blocks adoption universally. This is the first thing every evaluator hits.
- **Message templates** — `"User {Id} logged in"` is how Serilog apps communicate structure. A break here makes the compat layer a non-starter before the first compile.
- **Level map (Verbose → Fatal)** — apps use `LogEventLevel` by name throughout. Without a clean map onto Herald's level set, the call surface carries over in name only.
- **`appsettings.json` configuration** — a large share of production Serilog deployments configure sinks and enrichers via `appsettings.json`. Without `ReadFrom.Configuration`, they cannot drop in even if the call surface carries over. This is the day-one gate for any ops team that does not change code to change log configuration.
- **ASP.NET wiring** — `UseSerilog()` and `AddSerilog()` are the standard entry points for every ASP.NET Core app. Without them the host integration does not exist.
- **Popular sinks (Console/File/Elasticsearch/OTLP/HTTP)** — these cover the vast majority of sink usage. `maps-to-equivalent` because Herald ships its own implementations; the Serilog sink package itself cannot bind to the shim (assembly identity wall), but an equivalent Herald sink exists for each.

**High — blocks a large production segment:**

- **Sink/enricher by name in `appsettings.json`** (S-NEW-1) — a shop that wires their in-house sink as `"Name": "MyCompanySink"` hits a wall with no resolution except forking the parser. This is day-one friction for any shop with an in-house sink, which is most shops in the regulated segment. `maps-to-equivalent` because `LoggerSinkRegistry.RegisterSink(...)` is a one-call fix.
- **Custom user-authored sinks (S1)** — production shops nearly always have at least one in-house sink (a centralized log store, an audit trail, a metrics counter). The S1 seam absorbs source-compiled sinks. It does not absorb pre-compiled community packages — that is the identity wall, separately ranked.
- **Custom enrichers (S2)** — compliance and platform teams rely on enrichers to add correlation IDs, thread context, and environment metadata. The S2 bridge absorbs source-compiled enrichers. Pre-compiled enricher NuGet packages hit the same identity wall as sinks.
- **Output-template grammar** — `{Level:u3}`, `{Message:lj}`, and the timestamp format specifiers are how teams control log output shape. Silent degradation to wrong output was ruled out in the scope PRD; this surface is v1.
- **Pre-compiled community sinks (hard wall)** — Seq in particular is widely used for local development and production monitoring. The identity wall is a named gap with no workaround short of replacing the sink.

**Medium-high:**

- **Custom destructuring policies (S5)** — compliance teams rely on `IDestructuringPolicy` to strip PII before the event reaches any sink. A silent no-op on the redaction policy is a security regression, not a feature gap. Ranked high in security impact; medium-high in installed-base prevalence.

**Medium — meaningful friction, not universal:**

- `AuditTo` semantics matter acutely for compliance deployments, less for general-purpose logging.
- Custom formatters (`ITextFormatter` / CLEF) are used by teams that control output schema strictly.

**Medium-low / Low:**

- `LoggingLevelSwitch`, sub-loggers, and `SelfLog` are used by a subset of Serilog customers. Gaps here matter to the teams that use them but do not block the majority.

---

## Per-gap migration companion index

Full step-by-step migration guides live under `docs/serilog-compat/migrations/`. Each companion covers one gap: what you have in Serilog, what changes, how to verify, and — for hard walls — the honest alternatives.

| Gap | Companion |
|---|---|
| User-authored custom sink | [migrations/custom-sink.md](migrations/custom-sink.md) |
| User-authored custom enricher | [migrations/custom-enricher.md](migrations/custom-enricher.md) |
| Custom destructuring policy | [migrations/destructuring-policy.md](migrations/destructuring-policy.md) |
| AuditTo / WriteTo semantics | [migrations/audit-sinks.md](migrations/audit-sinks.md) |
| Sink/enricher by name in appsettings.json | [migrations/config-by-name.md](migrations/config-by-name.md) |
| Custom ITextFormatter / CLEF | [migration-runbook.md § Structural-match gaps](migration-runbook.md#structural-match-gaps-inline) (inline) |
| Sub-loggers | [migration-runbook.md § Structural-match gaps](migration-runbook.md#structural-match-gaps-inline) (inline) |
| LoggingLevelSwitch | [migration-runbook.md § Structural-match gaps](migration-runbook.md#structural-match-gaps-inline) (inline) |
| Output-template grammar | [migration-runbook.md § Structural-match gaps](migration-runbook.md#structural-match-gaps-inline) (inline) |
| Pre-compiled community sinks (hard wall) | [migrations/third-party-sinks.md](migrations/third-party-sinks.md) |
| Serilog.Expressions DSL (hard wall) | [migrations/expressions-dsl.md](migrations/expressions-dsl.md) |

---

## Deferred fields (fill after P1–P7 land)

The following cells require shipped artifacts and are marked for finalization in P8 Task 4:

- Exact loud-fail error text for G-SINK-WALL.1 (ships in P5).
- Request-log line field list for G-CORPUS.3 (ships in P6).
- CS0433 coexistence proof verbatim (ships in P7 G-LAYER2.1).
- ~~Net10 allocation figures for the honest-claim benchmark citation~~ — landed (P1). See [§Measured numbers](#measured-numbers-net10) below.

<!-- FILL AFTER P5: replace stub error text with the exact named throw message from the settings parser -->
<!-- FILL AFTER P6: add the request-log field list from UseSerilogRequestLogging -->
<!-- FILL AFTER P7: add the exact CS0433 diagnostic text from the G-LAYER2.1 test output -->

---

## Measured numbers (net10)

Measured on net10 with BenchmarkDotNet InProcess, RyuJIT AVX2. Source:
`benchmarking/comparisons/net10/serilog-compat/` on `feat/serilog-compat`.

The accept-path allocation claim: **0 B for the six hot primitives** (int, long, double,
bool, DateTime, string) **on the typed fast path, at every arity 1–16.** The typed fast path
means `SerilogLoggerAdapter` is the concrete receiver and the template is a cached interned
string.

The headline comparison at **arity 2**: the accept path runs **~69 ns / 0 B** on Herald's
typed fast path. The `Serilog.Log.*` surface a consumer writes today runs **~108 ns / 1,271 B**
— it routes through the Layer-2 `params object[]` shim, which boxes its arguments exactly as
real Serilog does. The typed fast path is the 0 B claim; the surface is not, and we do not
claim it is.

| Path | Arity 2 | Arity 12 | Notes |
|---|---|---|---|
| Herald native typed-args (accept) | ~57 ns / 0 B | ~55 ns / 0 B | Direct Herald API, no compat layer |
| Serilog-compat FastPath (typed, accept) | 68.61 ns / 0 B | 429.97 ns / 0 B | `SerilogLoggerAdapter` typed receiver |
| Serilog-compat Surface (`Serilog.Log.*`, accept) | 107.89 ns / 1,271 B | — | Layer-2 `params` boxing — equals Serilog's own behavior |
| Real Serilog 4.3.1 (accept) | — | ~551 ns / 1.49 KB | Reference baseline |
| Serilog-compat FastPath (reject) | 3.183 ns / 0 B | — | `IsInformationAcceptable` level guard |
| Serilog-compat Surface (reject) | 2.855 ns / 0 B | — | Level guard before any work |

The reject path is the level guard turning a call away before any work. At ~3 ns / 0 B it
costs almost nothing — a logger left at `Information` pays this for every `Debug` call and
never feels it.

At arity 12 the typed fast path runs ~430 ns / 0 B against real Serilog's ~551 ns / 1.49 KB:
faster, and zero bytes where Serilog allocates 1.49 KB.

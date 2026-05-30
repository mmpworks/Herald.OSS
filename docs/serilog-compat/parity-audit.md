# Parity Audit — Herald.OSS Serilog Compatibility

- **Date:** 2026-05-30 (initial draft; P7-gap additions included)
- **Branch:** `feat/serilog-compat`
- **Updated by:** Task 8 — migration runbook + P7-gap integration
- **Status:** Draft — sections marked `<!-- FILL AFTER P-n -->` require shipped artifacts before finalization (Task 4 in P8)

---

## What this is

A friction map, not a defect list. Every Serilog public surface tagged against its Herald status, ordered so the gap that blocks the most real Serilog users appears first. The goal is to let a Serilog team decide quickly whether Herald drops in, then migrate each gap with a named path instead of a fork.

---

## How to read it

Three tags:

- **carries-over** — source-compatible on recompile. Change the package reference and rebuild; the surface works.
- **maps-to-Herald-equivalent** — different name or package, same behavior. One documented change at the config or call site; behavior is equivalent.
- **hard-wall** — structural boundary. No drop-in path. Named alternatives exist; there is no workaround that preserves the original community package binding.

**Population rank** (H / M / L) reflects how many production Serilog apps use the surface, based on the seam inventory pre-mortem. High-rank gaps block the most users on day one.

---

## The honest claim

> *"Swap the package, rebuild, and your standard Serilog code runs on Herald — popular sinks map over; the third-party-sink ecosystem and the expression DSL are a documented boundary."*

Source compatibility on recompile. Not binary identity. Herald does not have Serilog's strong-name key and will not spoof it.

Full wording and usage rules: `docs/serilog-compat/honest-claim.md`.

---

## Friction map

| Serilog surface | Tag | Herald equivalent / boundary | Population rank | Migration companion | Regression test |
|---|---|---|---|---|---|
| Instance `ILogger` verbs (`Verbose`…`Fatal`, `Write`, `ForContext`, `IsEnabled`) | carries-over | `MMP.Herald.Serilog.ILogger` | H | — | G-CORPUS.1 |
| Static `Log` facade (`Log.Logger`, `Log.Information`, `Log.CloseAndFlush`) | carries-over | `MMP.Herald.Serilog.Log` | H | — | G-CORPUS.1 |
| Message templates — named holes, positional holes, `{{`/`}}` escaping | carries-over | Herald template parser | H | — | G-GAP.6 |
| `{@Obj}` destructure / `{$Obj}` stringify inline syntax | carries-over | `LogPropertyCaptureMode` mapping | H | — | G-HOT.3 |
| `LogEventLevel` enum (`Verbose`…`Fatal`) | carries-over | `LogLevel` level map | H | — | G-LEVEL.3 |
| `LogContext.PushProperty(...)` | **stub (throws)** | `BeginScope(dict)` on MEL `ILogger<T>` as interim | H | [Planned post-P7](#logcontext-stub) | — |
| `LoggerConfiguration` code config (`MinimumLevel.*`, `WriteTo.*`, `Enrich.*`, `CreateLogger`) | carries-over | `MMP.Herald.Serilog.LoggerConfiguration` → `QuickLogBuilder` | H | — | G-CORPUS.1 |
| `appsettings.json` — `ReadFrom.Configuration(IConfiguration)` | carries-over | `Herald.OSS.Serilog.Settings.Configuration` (Apache-2.0) | H | — | G-CORPUS.2 |
| ASP.NET — `IHostBuilder.UseSerilog(...)` / `AddSerilog(...)` | carries-over | `MMP.Herald.Serilog.AspNetCore` over `HeraldLoggerProvider` | H | — | G-CORPUS.3 |
| ASP.NET — `UseSerilogRequestLogging(...)` | carries-over | `MMP.Herald.Serilog.AspNetCore` middleware | M | — | G-CORPUS.3 |
| ASP.NET — `IWebHostBuilder.UseSerilog(...)` | **stub (throws)** | Use `IHostBuilder.UseSerilog(...)` instead | M | [migration-runbook.md](migration-runbook.md) | — |
| Console sink (`WriteTo.Console(...)`) | carries-over | Herald built-in Console sink | H | — | — |
| File sink (`WriteTo.File(...)`) | carries-over | Herald built-in File sink | H | — | — |
| HTTP / TCP / UDP sinks | carries-over | Herald built-in HTTP/TCP/UDP sinks | M | — | — |
| Elasticsearch sink | carries-over | Herald built-in Elasticsearch sink | M | — | — |
| OTLP sink | carries-over | Herald built-in OTLP sink | M | — | — |
| Null sink | carries-over | Herald built-in Null sink | L | — | — |
| Custom user-authored `ILogEventSink` (source-compiled) | carries-over | `WriteTo.Sink(new MySink())` via adapter | H | [migrations/custom-sink.md](migrations/custom-sink.md) | G-CORPUS.4 |
| Custom `ILogEventEnricher` (source-compiled) | carries-over | `Enrich.With(...)` via adapter | H | [migrations/custom-enricher.md](migrations/custom-enricher.md) | G-CORPUS.4 |
| Custom `IDestructuringPolicy` — `ByTransforming<T>(Func)` form | carries-over | `Destructure.ByTransforming<T>(...)` mapped to `QuickLogBuilder` projection | H | [migrations/destructuring-policy.md](migrations/destructuring-policy.md) | G-SEC.1 |
| Custom `IDestructuringPolicy` — raw `Destructure.With(IDestructuringPolicy)` form | carries-over | Bridge adapter onto value-model tree (not string path) | H | [migrations/destructuring-policy.md](migrations/destructuring-policy.md) | G-SEC.1 |
| `AuditTo` vs `WriteTo` semantics (throw vs swallow) | carries-over | `auditMode` bool on sink adapter | M | [migrations/audit-sinks.md](migrations/audit-sinks.md) | G-SEC.2, G-SEC.3 |
| Sink/enricher by name in `appsettings.json` (`"Using"` / `"Name"` resolution) | carries-over | `LoggerSinkRegistry.RegisterSink("MyName", ...)` | H | [migrations/config-by-name.md](migrations/config-by-name.md) | G-SINK-WALL.1 |
| `ITextFormatter` / CLEF output | carries-over | `ILogFormatter` bridge via `StringWriter` | M | [migrations/custom-formatter.md](migrations/custom-formatter.md) | G-GAP.5 |
| Sub-loggers (`WriteTo.Logger(lc => ...)`) | carries-over | `QuickLogBuilder` nested-pipeline composition | M | [migrations/sub-loggers.md](migrations/sub-loggers.md) | — |
| `LoggingLevelSwitch` | carries-over | `LogLevelSwitch` constructor/property alias | M | [migrations/level-switch.md](migrations/level-switch.md) | G-GAP.3 |
| `SelfLog` | carries-over | `ISinkHealthReporter` facade | M | — | G-GAP.4 |
| Output-template grammar (`{Level:u3}`, `{Message:lj}`, `:HH:mm`) | carries-over | Herald output-template grammar v1 | H | [migrations/output-template.md](migrations/output-template.md) | G-GAP.1 |
| Value model (`ScalarValue`, `StructureValue`, `SequenceValue`, `DictionaryValue`) | carries-over | Layer-1 value-model mirror | M | — | G-VM.1, G-VM.2 |
| **Pre-compiled community sinks (Seq, MSSql, Datadog, long tail)** | **hard-wall** | No Herald equivalent for pre-compiled binding; see below | H | [migrations/third-party-sinks.md](migrations/third-party-sinks.md) | G-SINK-WALL.1 |
| **`Serilog.Expressions` string DSL** (`Filter.ByIncluding("level = 'Error'")`) | **hard-wall** | Predicate `Filter.ByExcluding(Func<>)` maps; string DSL does not; open RFC | M | [migrations/expressions-dsl.md](migrations/expressions-dsl.md) | G-GAP.2 |

---

## Third-party sinks — the identity wall

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

**High (H) — day-one blockers for most Serilog users:**

- **Core call surface and static `Log` facade** — the log verbs and `LoggerConfiguration` builder are in every Serilog app. Any gap here blocks adoption universally.
- **`appsettings.json` configuration** — a large share of production Serilog deployments configure sinks and enrichers via `appsettings.json`. Without `ReadFrom.Configuration`, they cannot drop in even if the call surface carries over.
- **Custom user-authored sinks** — production shops nearly always have at least one in-house sink (a centralized log store, an audit trail, a metrics counter). The S1 seam and its hard-wall caveat (source-compiled only, not pre-compiled community packages) lands directly on this population.
- **Custom enrichers and destructuring policies** — compliance teams rely on enrichers to add correlation IDs and on destructuring policies to strip PII before the event reaches any sink. A silent no-op on the redaction policy is a security regression. These land on the regulated-industry segment that is the highest-value Herald customer.
- **Sink/enricher by name in `appsettings.json`** (S-NEW-1) — a shop that registers their in-house sink as `"Name": "MyCompanySink"` in appsettings hits a wall with no resolution except forking the parser. This is day-one friction for any shop with an in-house sink, which is most shops in the regulated segment.
- **Pre-compiled community sinks** — Seq in particular is widely used for local development and production monitoring. The identity wall is a named gap with no workaround short of replacing the sink.

**Medium (M) — meaningful friction, not universal:**

- `UseSerilogRequestLogging` and the ASP.NET wiring are standard in web apps but less universal than the call surface itself.
- `AuditTo` semantics matter acutely for compliance deployments, less for general-purpose logging.
- Custom formatters, sub-loggers, `LoggingLevelSwitch`, and `SelfLog` are used by a subset of Serilog customers.

**Low (L):**

- The Null sink is used for testing suppression; gaps here have minimal production impact.

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
| Custom ITextFormatter / CLEF | [migrations/custom-formatter.md](migrations/custom-formatter.md) |
| Sub-loggers | [migrations/sub-loggers.md](migrations/sub-loggers.md) |
| LoggingLevelSwitch | [migrations/level-switch.md](migrations/level-switch.md) |
| Output-template grammar | [migrations/output-template.md](migrations/output-template.md) |
| Pre-compiled community sinks (hard wall) | [migrations/third-party-sinks.md](migrations/third-party-sinks.md) |
| Serilog.Expressions DSL (hard wall) | [migrations/expressions-dsl.md](migrations/expressions-dsl.md) |

---

## Deferred fields (fill after P1–P7 land)

The following cells require shipped artifacts and are marked for finalization in P8 Task 4:

- Exact loud-fail error text for G-SINK-WALL.1 (ships in P5).
- Request-log line field list for G-CORPUS.3 (ships in P6).
- CS0433 coexistence proof verbatim (ships in P7 G-LAYER2.1).
- Net10 allocation figures for the honest-claim benchmark citation (ships in P1).

<!-- FILL AFTER P5: replace stub error text with the exact named throw message from the settings parser -->
<!-- FILL AFTER P6: add the request-log field list from UseSerilogRequestLogging -->
<!-- FILL AFTER P7: add the exact CS0433 diagnostic text from the G-LAYER2.1 test output -->
<!-- FILL AFTER P1: add net10 allocation figure + provenance for honest-claim §3 benchmark citation -->

# Seam Inventory — Serilog-Compat Extension Hooks (Rosanne)

- **Date:** 2026-05-29 · **Branch:** `feat/serilog-compat`
- Ratifies/corrects Richard's §D.2 stand-in. Governing rule: *baseline + customization path, not fork* — never make "fork the shim" the only way to reach a known-necessary path.
- **Shared constraint:** all five user-extension families (sink, enricher, formatter, destructuring policy, sub-logger) receive the **mirrored** Serilog `LogEvent` (Richard's value model, Jared signed off). The mirror's public projection entry must produce the **tree**, not a string. Get that right first — every seam below depends on it.

## Land in v1 (high-confidence, expensive retrofit)

### S5 (corrected) — Custom destructuring policy: the return-type fork  *(security-critical)*
- User holds: `Destructure.ByTransforming<T>(Func<T,object>)` **and** raw `Destructure.With(IDestructuringPolicy)` (returns a `LogEventPropertyValue` **tree**).
- Native: `QuickLogBuilder.Destructure<T>(Func<T,object?>)` maps the **projection** form cleanly (`QuickLogBuilder.Pipeline.cs:793`). Herald's raw `IDestructuringPolicy.TryDestructure(object, out string?)` returns a **string** — the raw-policy form does **not** map.
- Hook now: a shim `IDestructuringPolicy` adapter bridged onto the value-model **tree** projection (not the native string policy). Ship `ByTransforming` as the worked example; ship the raw-policy adapter with a test proving a password-stripping policy actually strips. **If the tree bridge can't land in v1, throw loudly at registration — never silent no-op** (a no-op'd redaction = PII regression / possible CVE).
- Abuts value model: directly (its output *is* the mirror's tree).

### S-NEW-1 — Custom sink/enricher resolution **by name** in `appsettings.json`  *(the miss Richard's stand-in didn't see)*
- User holds: `"Using": ["MyCompany.Logging"], "WriteTo": [{ "Name": "MyCompanySink", "Args": {...} }]`, `"Enrich": ["MyCompanyEnricher"]`. Serilog resolves names→types by assembly scan.
- Native: the reimplemented `Herald.OSS.Serilog.Settings.Configuration` parser only knows the hard-coded Herald sink set as designed — a custom name has nowhere to resolve.
- Hook now: public `LoggerSinkRegistry`/`LoggerEnricherRegistry` on the settings project — `RegisterSink(string name, Func<args, sinkConfig>)` — consulted before the parser fails. Ship empty (Herald built-ins pre-registered); a user registers `"MyCompanySink"` in one call. Unresolved name → named, audited throw (same loud-fail as Seq).
- Why dangerous if deferred: a large share of production Serilog apps configure sinks by name in `appsettings.json`, including their own. Day-one of adoption a shop with one in-house sink hits a wall whose only escape is fork-the-parser. Retrofit later = re-version the Apache-2.0 package + every pinned consumer.

### S1 — Custom user-authored sink (`ILogEventSink.Emit`)
- User holds: `class MySink : ILogEventSink { void Emit(LogEvent e) }`, wired `WriteTo.Sink(new MySink())`.
- Native: `WriteTo.Sink(...)` compat verb → adapter on Herald's heap-sink path (`CustomSinkProvider` registration exists, `QuickLogBuilderSerializers.cs:102`); adapter hands the sink the **mirrored** `LogEvent`. (Kernel `IKernelSink` is an optimization the user never sees; Serilog sinks are heap-shaped.)
- **Hard-wall caveat — do NOT paper over:** S1 absorbs **source-compiled, user-authored** sinks only. It does **NOT** absorb pre-compiled community sinks (Seq/MSSql/Datadog) — strong-name identity wall. The worked example must open with this boundary or a user infers "custom sink works" ⇒ "Seq works."

### S2 — Custom enricher (`ILogEventEnricher.Enrich`)
- User holds: `class MyEnricher : ILogEventEnricher { void Enrich(LogEvent e, ILogEventPropertyFactory f) }`, wired `Enrich.With(...)`.
- Native: `ILogEnricher.Enrich(LogEventEnrichmentContext)` (`ILogEnricher.cs:13`) — same one-way pre-sink shape. Adapter feeds the mirrored event + an `ILogEventPropertyFactory` shim that **must route `CreateProperty(..., destructureObjects:true)` through the same value-model tree path** or enricher-created `{@}` props silently flatten.
- Note: Herald's `ToJsonConfig()` round-trip contract — a stateful Serilog enricher won't survive `Reload(json)` unless the adapter emits its config (test, not blocker).

### S9 — Sink-failure semantics: `WriteTo` swallow vs `AuditTo` throw  *(compliance)*
- User holds: `WriteTo.X()` (swallows) vs `AuditTo.X()` (throws on sink failure — compliance contract).
- Hook now: an `auditMode` bool threaded from the `AuditTo` verb into the sink adapter — default false (swallow, matches `WriteTo`), true re-throws. 8 lines now; retrofitting throw-semantics into a shipped swallow path means auditing every sink-adapter call site. Silently swallowing an audit failure is the worst break given Herald's compliance positioning.

## Optional (medium-confidence or cheaper retrofit)

- **S3 — Custom `ITextFormatter`/CLEF.** `ILogFormatter.Format(LogEvent)→string` bridges to Serilog's `Format(LogEvent, TextWriter)` via a `StringWriter`. Mechanical, cheap retrofit. **Do not conflate with the output-template grammar** (now v1 per Steve) — the formatter *seam* is optional; the grammar is a separate v1 item.
- **S6 — Sub-loggers (`WriteTo.Logger(lc => ...)`).** Maps onto the existing `QuickLogBuilder`→JSON nested-pipeline composition. Additive verb; land when the corpus shows real usage. `AuditTo` inside a sub-logger inherits S9's bool.

## Demoted (not seams — kept honest)

- **S4 — `LoggingLevelSwitch`:** native `LogLevelSwitch` (`LogLevelSwitch.cs:12`) is a confirmed structural match. Work is a constructor/property alias, not an extension hook. No seam.
- **S7 — `SelfLog`:** forwards onto existing `ISinkHealthReporter`. Honor it (high trust-during-incident value) as compat-facade wiring, not a seam.

## Hard walls (open-source-dilemma treatment, not a fake soft seam)

- **Pre-compiled community sinks (Seq/MSSql/Datadog/long tail):** strong-name identity wall (`PublicKeyToken=24c2f752a8e58a10`; Herald.OSS unsigned). S1 does NOT absorb these.
- **`Serilog.Expressions` DSL** (`Filter.ByIncluding("...")`, expression templates): hard wall. The **predicate** `Filter.ByExcluding(...)` maps to processors; the **string-DSL** form does not. Send the DSL to the community as an open RFC.

## Pre-mortem (the six-month failure this prevents)

A mid-size regulated .NET shop (Herald's wedge) swaps the package, rebuilds, log calls work — claim holds. Then their platform lead opens `appsettings.json` where an in-house `AuditSink` + `PiiRedactingEnricher` are wired **by name**. The parser doesn't know them; it throws (good — loud-fail held) but there's **no registration path** — only "fork the parser." That's S-NEW-1, landing on the highest-value customer. The quieter, worse failure: their `IDestructuringPolicy` strips a `password`; under a silent no-op (string-vs-tree fork) the redaction stops and PII flows to the sink with no exception, no failed build. That's S5 — a compatibility gap turned compliance incident. Both cost a handful of lines now; both cost a package re-version + escalation + possible disclosure later. **Land S-NEW-1, S5, S1, S2, S9 in v1.**

# Design Round — Jared (systems co-lead)

- **Date:** 2026-05-29
- **Branch:** `feat/serilog-compat`
- **Charter:** zero-alloc lowering (open Q3), assembly-identity/binding verdict (open Q2), independent structural cross-review. Adversarial (red-team) pass on both load-bearing claims.
- **Status:** complete; pending reconciliation with Richard's round.

## Verdicts (decision-relevant)

### Open Q3 — zero-alloc lowering: RESOLVED → separate Serilog-hole-named arity generator

- Herald's existing typed-args generator names properties from `[CallerArgumentExpression]` (arg source text). **Serilog names from template holes.** So the shim **cannot** reuse Herald's typed-args overloads — it must emit its **own** arity family (per level × arity 1..16) that binds the i-th positional arg to the i-th template-hole name.
- It **does** reuse the value-transport primitive `LogPropertyCompact.From<T>(name, value)` (JIT-specialized, no box for int/long/double/bool/DateTime/string — verified `LogPropertyCompact.cs:170`) and the existing `BufferSizeFor` buffer-size mapping (stack `InlineArray` buffers → `StructuredLogger.LogCompact(..., ReadOnlySpan<LogPropertyCompact>)`). DRY on transport; correctly **not** shared on naming.
- Net-new state: a **positional hole-name index** captured once at parse time (alongside the existing parse cache) so `NameAt(i)` is an array index — no per-call token walk, no LINQ. Must be **bounded/evicted** like `NameResolverCache` (cap mechanism to confirm in impl).
- `[OverloadResolutionPriority(arity)]` on the generic overloads keeps normal calls off the `params object[]` boxing path. A missing/incorrect priority silently routes mid-arity calls to boxing — **caught only by an allocation benchmark, not correctness tests.**

**Honest perf claim (do NOT overclaim):** *"No additional allocation or boxing versus real Serilog on any call shape; zero boxing for the six hot primitives (int/long/double/bool/DateTime/string) on arity 1..16 with cached templates — a path real Serilog does not have."* Boxing is **equal to Serilog** for decimal/Guid/TimeSpan/enum/struct (all fall to `From<T>`'s object arm, verified line 190). Zero-alloc holds for **cached templates**; novel/dynamically-built templates pay parse cost **equal to** Serilog.

### Open Q2 — binding: RESOLVED → reimplement (it's "build a parser," not "reference a package")

CLR binds assembly refs by full identity **including `PublicKeyToken`**. Serilog is strong-named (`PublicKeyToken=24c2f752a8e58a10`); **Herald.OSS is unsigned** (verified: no `SignAssembly` in `Herald.OSS.csproj`). An unsigned `Serilog` is a different identity; the strong-named refs baked into every Serilog add-on/sink cannot be satisfied by the shim. `[TypeForwardedTo]` does **not** launder identity.

- **`Serilog.Settings.Configuration`** → cannot drive the shim. Build `Herald.OSS.Serilog.Settings.Configuration` (Apache-2.0): `ReadFrom.Configuration(IConfiguration)` as an extension on the **shim's** `LoggerConfiguration`, parsing the same `appsettings.json` schema (`MinimumLevel`/`WriteTo`/`Enrich`/`Override`) → `QuickLogBuilder`. A `Using`/`WriteTo` entry naming a non-Herald sink (e.g. `Serilog.Sinks.Seq`) must **fail loudly with a named, audited error**, never silently drop.
- **`Serilog.AspNetCore` / `Serilog.Extensions.Logging`** → provide our own `UseSerilog`/`AddSerilog` over the already-shipping `HeraldLoggerProvider` (verified full `ILoggerProvider`/`Log<TState>`/`IsEnabled`/`BeginScope`; reads `{OriginalFormat}` at `HeraldLoggerProvider.cs:107`). `UseSerilogRequestLogging()` is the one net-new component (request-logging middleware over the Herald logger).
- **Third-party sinks** → hard identity wall. Precise statement drafted for the parity audit (see below).

## Correction to the scope PRD — coexistence is Layer-1-only (load-bearing, honesty)

The PRD says the compat layer "can coexist with the real Serilog package." **True only for Layer 1** (`MMP.Herald.Serilog.*`, distinct namespace). For **Layer 2** (mirrored `Serilog.*`), coexistence with any transitively-referenced real `Serilog.dll` produces **duplicate `Serilog.*` types** → `CS0433` at compile or `InvalidCastException` at runtime. **Layer 2 is the final-cutover package — it must be the only `Serilog` in the graph.** Migration runbook: stage on Layer 1 alongside real Serilog → verify → cut over to Layer 2 and remove all real-Serilog refs in one step. Heather lifts this into the parity audit + migration runbook.

## Correctness landmine — `{@}`/`{$}` holes must NOT use the compact fast path

`LogPropertyCompact` is **default-axes-only** (cannot carry capture mode — verified `LogPropertyCompact.cs:64-93`, enforced by analyzer `HERALD014`). A Serilog `{@Order}` (destructure) or `{$Value}` (stringify) hole **must route to the full `LogProperty[]` path**, not the compact buffer — silently compacting it would drop the mode. The hole-name index must therefore flag, **per hole**, whether it carries a non-default capture mode, and the overload picks compact-vs-full **at the hole**, per-property, not per-call.

## Parity-audit text — third-party sinks (drop in verbatim)

> Third-party and community Serilog sinks (`Serilog.Sinks.Seq`, `.Sinks.MSSqlServer`, `.Sinks.Datadog`, and the long tail) cannot bind to the Herald `Serilog.*` shim. Each is compiled against `Serilog, PublicKeyToken=24c2f752a8e58a10` and depends on the real strong-named `Serilog.ILogEventSink`/`Serilog.Core` types. The shim is unsigned and exports types of a different assembly identity; the CLR will not satisfy the sink's `Serilog` reference with the shim. Referencing such a sink transitively loads the real `Serilog.dll`, producing duplicate `Serilog.*` types (CS0433 at compile, or InvalidCastException at runtime). This is a structural identity wall, not a deferral. Herald's own equivalents (Console/File/HTTP/TCP/UDP/Elasticsearch/OTLP/Null) cover the popular sinks; Seq and the long tail are named gaps with no drop-in path absent a strong-named signing key we do not have and will not spoof.

## net9 vs net10

`params ReadOnlySpan<LogProperty>`, `OverloadResolutionPriority`, stack `InlineArray` buffers — all available on both net9 and net10; **no TFM fork in the hot path.** Design does **not** depend on any net10-only lowering; net10 span improvements are upside, not load-bearing. **Benchmark on net10** (per the .NET-10-only rule); do not publish net9 figures.

## Benchmark plan (no-regression gates, all net10)

1. **Herald native vs shim** (int+string, cached template): shim **0 B/op** on accept path, matching baseline. (Overload-priority-bug detector.)
2. **Shim vs real Serilog (net10)**: shim alloc **≤ Serilog** on every row (primitives strictly less; decimal/Guid/struct equal).
3. **Reject path** (below min level): **0 B/op** native and shim (`IsInfoAcceptable` field-read short-circuits before parse/buffer).

Workload matrix: arity {0,1,2,4,8,16,17} × arg-type {int, string, double, decimal(box), Guid(box), mixed} × template {cached, novel}.

## Independent structural take (for reconciliation with Richard)

```
MMP.Herald.Serilog.Core   (Layer 1: Serilog-shaped types in MMP namespace;
                            static Log facade HERE; arity generator HERE)
Serilog                    (Layer 2: thin identity mirror; NO TypeForwardedTo;
                            Serilog.Log → one-line forward to Layer-1 Log)
Herald.OSS.Serilog.Settings.Configuration   (Apache-2.0; reimplemented parser;
                            extension methods on Layer-1 LoggerConfiguration)
Herald.OSS.Serilog.AspNetCore               (UseSerilog/AddSerilog over
                            HeraldLoggerProvider + request-logging middleware)
```

**Facade placement (Jared's position):** all behavior — hole-name binding, zero-alloc fold, level mapping, `Log.Logger` singleton + `CloseAndFlush` — lives **once in Layer 1**. Layer 2 carries only namespace/type-shape identity, no behavior. Keeps a two-name surface from becoming a two-implementation surface.

## Open items for reconciliation with Richard

1. **Facade + generator placement** — Jared: both in Layer 1, Layer 2 thin mirror. Confirm.
2. **Static `Log.Logger` singleton** — its own tiny `StaticFacade` assembly (so the DI-pure path never references it) vs. living in Layer 1 behind a volatile holder. Both defensible; pick one.
3. **`{@}`/`{$}` routing** — compact-vs-full decided **at the hole**; the parser must surface per-hole capture-mode to the generator. Crosses both lanes.
4. **Rename ordering** — Jared recommends **rename lands FIRST** (mechanical, no users), then the shim generator is written against final `KnownLogLevels` names, so it isn't authored against names that change under it.

**Dissent from scope:** none. One sharpening (the Layer-1-only coexistence correction above) — a precision fix for honesty, not a scope disagreement.

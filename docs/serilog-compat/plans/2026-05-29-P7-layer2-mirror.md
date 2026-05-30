# P7 — Layer-2 Mirror Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship the two final-cutover assemblies — `Serilog` and `Serilog.AspNetCore` (both with `AssemblyName=Serilog…`) — that mirror Serilog's own namespaces and type shapes so a consumer swaps the package reference and changes nothing else. Every mirrored type is a **thin, zero-logic forwarder** onto its Layer-1 twin (`MMP.Herald.Serilog.*` from P1/P2/P6). The mirror carries **identity only**; all behaviour stays in Layer 1.

**Architecture:** Layer 2 is the literal `Serilog.*` surface (Richard §A.2/§A.3, Jared "independent structural take"). It is **mirrored types, NOT `[TypeForwardedTo]`** — forwarding cannot launder assembly identity (Jared Open-Q2), so each public Serilog type is re-declared in its real namespace and every member body forwards in one line to the Layer-1 implementation. **Any `if`/loop/format/parse/allocation-with-logic in a Layer-2 type is the DRY tripwire — reject it.** Because Layer 2 re-declares `Serilog.*`, it **cannot coexist** with a real `Serilog.dll` anywhere in the graph (duplicate types → `CS0433` at compile / `InvalidCastException` at runtime, Jared coexistence correction). Layer 2 is therefore the **final-cutover** package: stage on Layer 1 alongside real Serilog, verify, then cut over to Layer 2 and remove every real-Serilog reference in one step.

**Tech Stack:** C# / .NET — **net9 + net10 only** for both compat assemblies (override `HeraldTargetFrameworks=net9.0;net10.0`; no net8). `[OverloadResolutionPriority]` polyfill already lives in `src/Compatibility/OverloadResolutionPriorityAttribute.cs`. xUnit (`tests/Herald.OSS.Tests.csproj`); a **separate coexistence test project** is required because G-LAYER2.1 fails at *compile* (it cannot share a compilation with the rest of the suite). Real-Serilog parity corpus from P1's Layer-1 oracle harness, re-run unchanged against the mirror.

**Depends on:** P1 (Layer-1 `ILogger`, static `Log`, value-model mirror, level map), P2 (`LoggerConfiguration` builder, `WriteTo`/`Enrich`/`MinimumLevel`, `LoggingLevelSwitch`→`LogLevelSwitch`), P3 (output-template grammar + `ITextFormatter`/CLEF), P4 (seams: `ILogEventSink`/`ILogEventEnricher`/`IDestructuringPolicy`/`AuditTo`), P6 (`MMP.Herald.Serilog.AspNetCore` — `UseSerilog`/`AddSerilog`/`UseSerilogRequestLogging`). **P7 mirrors their public surface; it introduces no new behaviour and no new Layer-1 types.**

**Migration-runbook precondition (load-bearing):** Layer 2 must be the **only** `Serilog` in the dependency graph. The runbook is: stage on Layer 1 (`MMP.Herald.Serilog.*`) beside real Serilog → verify parity → swap to Layer 2 (`Serilog`) and remove all real-Serilog package references in one cutover. P7 documents and *tests* this precondition (G-LAYER2.1); it does not merely assert it in prose.

---

## Surface to mirror (enumerated from the design docs — the authoritative Layer-2 type list)

Every row is a Layer-2 type whose members forward to the Layer-1 twin. **Namespace = the real Serilog namespace** (left column). The Layer-1 twin lives in the `MMP.Herald.Serilog.*` mirror namespace (right column). If a Layer-1 twin named below does not exist when P7 starts, that is a **cross-plan gap — FLAG it, do not invent the type here** (P7 is mirror-only).

### Assembly `Serilog` (AssemblyName=Serilog)

| Layer-2 type (real Serilog namespace) | Kind | Forwards to (Layer-1 twin) | Source plan |
|---|---|---|---|
| `Serilog.ILogger` | interface | `MMP.Herald.Serilog.ILogger` | P1 |
| `Serilog.Log` (static facade) | static class | `MMP.Herald.Serilog.Log` | P1 |
| `Serilog.LoggerConfiguration` | class | `MMP.Herald.Serilog.LoggerConfiguration` | P2 |
| `Serilog.Core.Logger` (`ILogger`+`IDisposable`, `CreateLogger()` return) | sealed class | Layer-1 logger impl | P1 |
| `Serilog.Core.ILogEventSink` | interface | `MMP.Herald.Serilog.Core.ILogEventSink` | P4/S1 |
| `Serilog.Core.ILogEventEnricher` | interface | `MMP.Herald.Serilog.Core.ILogEventEnricher` | P4/S2 |
| `Serilog.Core.ILogEventPropertyFactory` | interface | Layer-1 property-factory shim | P4/S2 |
| `Serilog.Core.IDestructuringPolicy` | interface | `MMP.Herald.Serilog.Core.IDestructuringPolicy` | P4/S5 |
| `Serilog.Core.LoggingLevelSwitch` | class | Layer-1 wrapper over native `LogLevelSwitch` | P2 (S4) |
| `Serilog.Core.LoggerConfiguration` config-object set (`WriteTo`/`Enrich`/`MinimumLevel`/`Destructure`/`Filter`/`AuditTo`) | classes | Layer-1 configuration objects | P2/P4 |
| `Serilog.Events.LogEventLevel` | enum | maps to `MMP.Herald.Serilog` level map | P1 |
| `Serilog.Events.LogEvent` | class | Layer-1 value-model mirror (flat-fast/tree-on-demand) | P1 |
| `Serilog.Events.LogEventProperty` | class | Layer-1 twin | P1 |
| `Serilog.Events.LogEventPropertyValue` (abstract) | abstract class | Layer-1 twin | P1 |
| `Serilog.Events.ScalarValue` | class | Layer-1 twin | P1 |
| `Serilog.Events.SequenceValue` | class | Layer-1 twin | P1 |
| `Serilog.Events.StructureValue` | class | Layer-1 twin | P1 |
| `Serilog.Events.DictionaryValue` | class | Layer-1 twin | P1 |
| `Serilog.Events.MessageTemplate` | class | Layer-1 template twin | P1/P3 |
| `Serilog.Context.LogContext` (static) | static class | `MMP.Herald.Serilog.Context.LogContext` | P1 |
| `Serilog.Formatting.ITextFormatter` | interface | `MMP.Herald.Serilog.Formatting.ITextFormatter` | P3/S3 |
| `Serilog.Configuration.*` (the `LoggerSinkConfiguration`/`LoggerEnrichmentConfiguration`/`LoggerMinimumLevelConfiguration`/`LoggerDestructuringConfiguration`/`LoggerFilterConfiguration`/`LoggerAuditSinkConfiguration` objects returned by `WriteTo`/`Enrich`/`MinimumLevel`/`Destructure`/`Filter`/`AuditTo`) | classes | Layer-1 configuration objects | P2/P4 |
| `Serilog.Debugging.SelfLog` (static) | static class | Layer-1 `SelfLog` facade over `ISinkHealthReporter` | P4 (S7) |
| `Serilog.LoggerConfigurationExtensions` / `LoggerSinkConfiguration` extension entry points (`Console`/`File`/`Http`/`Tcp`/`Udp`/`Elasticsearch`/`Seq`-loud-fail/`Null`) | static class | Layer-1 sink-config extensions | P2 |

### Assembly `Serilog.AspNetCore` (AssemblyName=Serilog.AspNetCore)

| Layer-2 type (real Serilog namespace) | Kind | Forwards to (Layer-1 twin) | Source plan |
|---|---|---|---|
| `Serilog.SerilogApplicationBuilderExtensions.UseSerilogRequestLogging(...)` | static ext class | `MMP.Herald.Serilog.AspNetCore` request-logging middleware | P6 |
| `Serilog.SerilogHostBuilderExtensions.UseSerilog(...)` (all overloads) | static ext class | `MMP.Herald.Serilog.AspNetCore` host hook | P6 |
| `Serilog.SerilogWebHostBuilderExtensions.UseSerilog(...)` | static ext class | same host hook | P6 |
| `Microsoft.Extensions.DependencyInjection.SerilogServiceCollectionExtensions.AddSerilog(...)` | static ext class | `MMP.Herald.Serilog.AspNetCore` `AddSerilog` over `HeraldLoggerProvider` | P6 |
| `Serilog.AspNetCore.RequestLoggingOptions` | class | Layer-1 options twin | P6 |

> **Enumeration note:** This list is the design-anticipated surface. Two surfaces the design docs name only obliquely and which P7 must confirm against the *actual* Layer-1 output before mirroring (FLAG if the Layer-1 twin is absent rather than mirror a guess):
> 1. **`Serilog.Core.Logger` (the concrete `CreateLogger()` return type).** The docs talk about `ILogger`/`Log`/`LoggerConfiguration` but not the concrete `Logger` class that `LoggerConfiguration.CreateLogger()` returns and that real Serilog code stores in fields (`Logger log = new LoggerConfiguration()...CreateLogger();`). The corpus will need it. **FLAGGED as a likely Layer-1 surface the design under-specified — confirm in P1's output before mirroring.**
> 2. **`Serilog.Configuration.*` config-object exact shapes** (return types of `WriteTo`/`Enrich`/`MinimumLevel`/etc.). The design says "the `WriteTo`/`Enrich`/`MinimumLevel` configuration objects" without naming each class. P7 mirrors whatever P2/P4 publicly exposed; **if P2 exposed these as Layer-1 types with different names, that rename must be reconciled here — FLAG, do not paper over.**

---

### Task 0: the-fool pre-mortem gate (no code)

**Files:**
- Create: `docs/serilog-compat/plans/P7-layer2-premortem.md`

- [ ] **Step 1: Run the pre-mortem.** Invoke `Skill(the-fool)` framed as: *"Layer 2 is a hand-mirrored copy of Serilog's entire public surface, where the only correct amount of logic is zero. Where does a mirror silently grow behaviour, drift from the real Serilog shape, or fail to be the only Serilog in the graph?"* Capture failure modes, at minimum: (a) a mirrored member that does a null-check / format / cast instead of a bare forward (logic leaks in); (b) a Serilog public type omitted from the mirror so the corpus fails to compile (under-mirror); (c) a mirrored type whose signature drifts from real Serilog (a `default` param, a missing overload, a wrong return type) so source that compiled against real Serilog won't compile against the mirror; (d) Layer 2 accidentally referenced transitively *alongside* real Serilog and the CS0433 fires for a consumer who didn't read the runbook; (e) a Layer-1 twin that doesn't exist yet (cross-plan gap) being faked inside Layer 2 to "make it compile."

- [ ] **Step 2: Write the risk list** to `P7-layer2-premortem.md` — each risk + which Task below mitigates it. Any risk without a mitigating task means a task is missing from this plan.

- [ ] **Step 3: Commit**

```bash
git add docs/serilog-compat/plans/P7-layer2-premortem.md
git commit -m "docs(serilog-compat): the-fool pre-mortem on the Layer-2 mirror"
```

---

### Task 1: Stand up the two Layer-2 assemblies (net9/net10, AssemblyName=Serilog)

**Files:**
- Create: `src/Compatibility/Layer2/Serilog/MMP.Herald.Compat.Serilog.csproj` (`AssemblyName`=`Serilog`, `RootNamespace`=`Serilog`)
- Create: `src/Compatibility/Layer2/Serilog.AspNetCore/MMP.Herald.Compat.Serilog.AspNetCore.csproj` (`AssemblyName`=`Serilog.AspNetCore`)
- Read first: `Directory.Build.props` (TFM/lang-version mechanics), `Herald.OSS.csproj` (item-glob filtering pattern), `src/Compatibility/OverloadResolutionPriorityAttribute.cs` (polyfill already present).

- [ ] **Step 1: Author the `Serilog` csproj.** Set `<AssemblyName>Serilog</AssemblyName>`, `<RootNamespace>Serilog</RootNamespace>`, `<PackageId>` per Heather/Max convention (P8/Max own the published id; the *assembly* name is `Serilog`). Pin **net9/net10 only**: `<HeraldTargetFrameworks>net9.0;net10.0</HeraldTargetFrameworks>` (override the props default; **no net8**). Reference the Layer-1 assembly `MMP.Herald.Serilog` (P1/P2/P4) via `<ProjectReference>`. Keep `IsAotCompatible`/trim analyzers on (G-GAP.7). The mirror is unsigned by design (Jared Open-Q2: we do not spoof Serilog's strong-name key) — do **not** add `SignAssembly`.

- [ ] **Step 2: Author the `Serilog.AspNetCore` csproj** the same way; reference `MMP.Herald.Serilog.AspNetCore` (P6) and the `Serilog` Layer-2 assembly above. net9/net10 only.

- [ ] **Step 3: Wire both into the build/packaging surface** that the umbrella `build.sh` and `Directory.Packages.props` drive (coordinate with Max — packaging id, pack-on-publish). Do not invent a local `build.sh` (Herald.OSS has none; the umbrella owns it).

- [ ] **Step 4: Empty-build check** (no types yet — proves TFM + reference wiring).

```bash
cd E:/dev/Herald.OSS && dotnet build src/Compatibility/Layer2/Serilog/MMP.Herald.Compat.Serilog.csproj -c Debug 2>&1 | tail -5
```
Expected: builds clean (empty assembly named `Serilog`, net9/net10).

- [ ] **Step 5: Commit.**

```bash
git add src/Compatibility/Layer2 Directory.Packages.props
git commit -m "feat(serilog-compat): scaffold Layer-2 Serilog + Serilog.AspNetCore assemblies (net9/net10, unsigned mirror)"
```

---

### Task 2: Mirror the value model + level enum + message template (`Serilog.Events.*`)

The seam families (sink/enricher/formatter/policy) all receive these types — mirror them **first** (Rosanne's shared-constraint ordering: get the tree-shaped value model right before anything that consumes it).

**Files:**
- Create: `src/Compatibility/Layer2/Serilog/Events/LogEventLevel.cs`, `LogEvent.cs`, `LogEventProperty.cs`, `LogEventPropertyValue.cs`, `ScalarValue.cs`, `SequenceValue.cs`, `StructureValue.cs`, `DictionaryValue.cs`, `MessageTemplate.cs`
- Read first: the Layer-1 value-model twins in `MMP.Herald.Serilog` (P1). **If a twin is missing, STOP and FLAG a cross-plan gap — do not author the type's behaviour here.**

- [ ] **Step 1: Mirror `LogEventLevel`** as a plain enum with Serilog's exact member names/order (`Verbose=0, Debug, Information, Warning, Error, Fatal`). It carries no logic; conversion to Herald levels lives in Layer 1 (P1's level map). The enum *values* must match real Serilog's underlying numbers (corpus code may cast to int).

- [ ] **Step 2: Mirror the value types** (`LogEvent`, `LogEventProperty`, `LogEventPropertyValue` + the four concrete subtypes, `MessageTemplate`). Each is a thin wrapper holding a reference to its Layer-1 twin; every property/method is a one-line forward. **DRY tripwire:** a mirrored `ScalarValue.Render(...)` that formats, a `LogEvent.Properties` that *builds* a dictionary, a `MessageTemplate` that *parses* — all forbidden. Rendering/parsing/projection live in Layer 1; the mirror exposes the result.

- [ ] **Step 3: DRY-tripwire self-check on this batch** (full check is Task 7, but spot it early so the pattern is right from file one). Each member body is a single forwarding expression or a constructor that stores the twin. No `if`, no `for`/`foreach`, no `string.Format`, no `new Dictionary`, no `??`-with-side-effect.

- [ ] **Step 4: Build.**

```bash
dotnet build src/Compatibility/Layer2/Serilog/MMP.Herald.Compat.Serilog.csproj -c Debug 2>&1 | tail -5
```

- [ ] **Step 5: Commit.**

```bash
git add src/Compatibility/Layer2/Serilog/Events
git commit -m "feat(serilog-compat): Layer-2 mirror of Serilog.Events value model + LogEventLevel (zero-logic forwarders)"
```

---

### Task 3: Mirror the call surface (`Serilog.ILogger`, `Serilog.Log`, `Serilog.Context.LogContext`)

**Files:**
- Create: `src/Compatibility/Layer2/Serilog/ILogger.cs`, `Log.cs`, `Context/LogContext.cs`
- Read first: `MMP.Herald.Serilog.ILogger` / `.Log` / `.Context.LogContext` (P1).

- [ ] **Step 1: Mirror `Serilog.ILogger`** — the full instance surface the scope PRD names: `Verbose/Debug/Information/Warning/Error/Fatal` (each with the message-template + generic-typed overloads), `Write(LogEventLevel, …)`, the `ForContext(...)` overload family (`ForContext<T>()`, `ForContext(Type)`, `ForContext(string, value, bool)`, `ForContext(IEnumerable<ILogEventEnricher>)`), and `IsEnabled(LogEventLevel)`. Every member is a one-line forward to the Layer-1 `ILogger`. **The arity overloads forward as-is** — Layer 2 does NOT re-emit the Serilog-hole-named arity generator (that is Layer-1's job, P1/Jared Open-Q3); the mirror just exposes the generated Layer-1 signatures by forwarding. **If the typed-args overload set isn't present on the Layer-1 interface for the mirror to forward to, FLAG it (cross-plan dependency on P1's generator output).**

- [ ] **Step 2: Mirror the static `Serilog.Log` facade** — `Log.Logger` (get/set, one slot — forwards to the single Layer-1 mutable slot, NOT a second slot), `Log.Verbose/.../Fatal`, `Log.Write`, `Log.ForContext(...)`, `Log.CloseAndFlush()`, `Log.IsEnabled(...)`. **DRY tripwire:** `Log.Logger` must not hold its own backing field with logic — it forwards to Layer-1's `Log.Logger` (Richard: "`Log.Logger` is one mutable slot in Layer 1, not duplicated").

- [ ] **Step 3: Mirror `Serilog.Context.LogContext`** — `PushProperty(...)` (all overloads), `Push(...)`, `Clone()`, `Reset()`, `Suspend()` as Serilog exposes them — each forwarding to the Layer-1 `LogContext`.

- [ ] **Step 4: Build + commit.**

```bash
dotnet build src/Compatibility/Layer2/Serilog/MMP.Herald.Compat.Serilog.csproj -c Debug 2>&1 | tail -5
git add src/Compatibility/Layer2/Serilog/ILogger.cs src/Compatibility/Layer2/Serilog/Log.cs src/Compatibility/Layer2/Serilog/Context
git commit -m "feat(serilog-compat): Layer-2 mirror of Serilog.ILogger + static Log + LogContext (forward-only)"
```

---

### Task 4: Mirror configuration + core seam interfaces (`LoggerConfiguration`, `Serilog.Core.*`)

**Files:**
- Create: `src/Compatibility/Layer2/Serilog/LoggerConfiguration.cs`, `Core/Logger.cs`, `Core/ILogEventSink.cs`, `Core/ILogEventEnricher.cs`, `Core/ILogEventPropertyFactory.cs`, `Core/IDestructuringPolicy.cs`, `Core/LoggingLevelSwitch.cs`, `Configuration/` (the config-object set), `Formatting/ITextFormatter.cs`, `Debugging/SelfLog.cs`, and the `LoggerSinkConfiguration` extension entry points.
- Read first: the Layer-1 twins from P2 (`LoggerConfiguration`, `LoggingLevelSwitch` wrapper, the `WriteTo`/`Enrich`/`MinimumLevel`/`Destructure`/`Filter`/`AuditTo` config objects), P3 (`ITextFormatter`), P4 (`ILogEventSink`/`ILogEventEnricher`/`ILogEventPropertyFactory`/`IDestructuringPolicy`, `SelfLog`).

- [ ] **Step 1: Mirror `Serilog.LoggerConfiguration`** — `WriteTo`/`Enrich`/`MinimumLevel`/`Destructure`/`Filter`/`AuditTo` properties returning the mirrored config objects, and `CreateLogger()` returning the mirrored `Serilog.Core.Logger`. Each property/method forwards to the Layer-1 builder (which translates onto `QuickLogBuilder` → JSON — that translation is Layer-1's, never duplicated here).

- [ ] **Step 2: Mirror the `Serilog.Configuration.*` config objects** — the sink/enrichment/minimum-level/destructuring/filter/audit configuration classes, each method forwarding to its Layer-1 twin. **This is where the `WriteTo.Console()/.File()/.Http()/...` and `Enrich.With()/.FromLogContext()` and `MinimumLevel.Information()/.Override()` and `AuditTo.X()` extension/instance methods are exposed.** Map the built-in sink set (Console/File/HTTP/TCP/UDP/Elasticsearch/OTLP/Null). A `Seq()`/unknown-sink entry forwards to Layer-1's **loud-named-fail** path — Layer 2 adds no fail logic of its own (G-SINK-WALL.1 lives in Layer 1).

- [ ] **Step 3: Mirror the `Serilog.Core.*` seam interfaces** — `ILogEventSink`, `ILogEventEnricher`, `ILogEventPropertyFactory`, `IDestructuringPolicy`, `LoggingLevelSwitch`, and the concrete `Logger`. **Interfaces:** a Layer-2 `Serilog.Core.ILogEventSink.Emit(Serilog.Events.LogEvent)` whose Layer-1 adapter receives the call (Richard's value model means a user's `MySink : Serilog.Core.ILogEventSink` is the *consumer's* type implementing the *mirrored* interface; the Layer-1 adapter bridges it — confirm the bridge lives in P4, not here). **`LoggingLevelSwitch`** forwards to the Layer-1 wrapper over native `LogLevelSwitch` (S4 structural match — a property/ctor alias, zero logic).

- [ ] **Step 4: Mirror `Serilog.Formatting.ITextFormatter`** (forward to P3's `ITextFormatter` bridge) and **`Serilog.Debugging.SelfLog`** (forward to P4's `SelfLog` facade over `ISinkHealthReporter`).

- [ ] **Step 5: Build + commit.**

```bash
dotnet build src/Compatibility/Layer2/Serilog/MMP.Herald.Compat.Serilog.csproj -c Debug 2>&1 | tail -5
git add src/Compatibility/Layer2/Serilog/LoggerConfiguration.cs src/Compatibility/Layer2/Serilog/Core src/Compatibility/Layer2/Serilog/Configuration src/Compatibility/Layer2/Serilog/Formatting src/Compatibility/Layer2/Serilog/Debugging
git commit -m "feat(serilog-compat): Layer-2 mirror of LoggerConfiguration + Serilog.Core seams + ITextFormatter + SelfLog (forward-only)"
```

---

### Task 5: Mirror the ASP.NET Core surface (`Serilog.AspNetCore` assembly)

**Files:**
- Create: `src/Compatibility/Layer2/Serilog.AspNetCore/SerilogHostBuilderExtensions.cs` (`UseSerilog`), `SerilogWebHostBuilderExtensions.cs`, `SerilogApplicationBuilderExtensions.cs` (`UseSerilogRequestLogging`), `SerilogServiceCollectionExtensions.cs` (`AddSerilog`, in `Microsoft.Extensions.DependencyInjection` namespace), `RequestLoggingOptions.cs`
- Read first: `MMP.Herald.Serilog.AspNetCore` (P6) — `UseSerilog`/`AddSerilog`/`UseSerilogRequestLogging` and `RequestLoggingOptions`.

- [ ] **Step 1: Mirror the host hooks** `IHostBuilder.UseSerilog(...)` and `IWebHostBuilder.UseSerilog(...)` (all overloads Serilog ships: parameterless, `(logger)`, `(configureLogger)`, `(configureLogger, preserveStaticLogger, writeToProviders)`). Each forwards to the P6 host hook.

- [ ] **Step 2: Mirror `IApplicationBuilder.UseSerilogRequestLogging(...)`** (parameterless + `Action<RequestLoggingOptions>` overloads), forwarding to P6's middleware registration. **The middleware itself is the one net-new component — it is in P6 (Layer 1); Layer 2 only forwards the extension call.**

- [ ] **Step 3: Mirror `IServiceCollection.AddSerilog(...)`** in the `Microsoft.Extensions.DependencyInjection` namespace (Serilog ships it there so it surfaces on the `IServiceCollection` consumers already `using`). Forwards to P6's `AddSerilog` over `HeraldLoggerProvider`.

- [ ] **Step 4: Mirror `RequestLoggingOptions`** as a thin holder forwarding to P6's options twin.

- [ ] **Step 5: Build + commit.**

```bash
dotnet build src/Compatibility/Layer2/Serilog.AspNetCore/MMP.Herald.Compat.Serilog.AspNetCore.csproj -c Debug 2>&1 | tail -5
git add src/Compatibility/Layer2/Serilog.AspNetCore
git commit -m "feat(serilog-compat): Layer-2 mirror of Serilog.AspNetCore (UseSerilog/AddSerilog/UseSerilogRequestLogging forwarders)"
```

---

### Task 6: G-CORPUS.1 — real-Serilog snippet corpus compiles AND runs **unchanged against the mirror**

This is the win-condition proof: the P1 Layer-1 corpus, re-pointed at the **Layer-2** `Serilog` assembly (real Serilog removed), compiles with zero source edits and produces canonical-shape-equivalent `LogEvent`s.

**Files:**
- Create: `tests/SerilogCompat/Layer2/Layer2CorpusTests.csproj` (a **dedicated** test project that references **only** the Layer-2 `Serilog` + `Serilog.AspNetCore` assemblies — NOT real Serilog, NOT Layer-1 directly; net9/net10).
- Create: `tests/SerilogCompat/Layer2/Layer2CorpusTests.cs`
- Read first: P1's corpus harness + the canonical-shape comparer (ingress↔output canonical-equivalence rule).

- [ ] **Step 1: Re-host the corpus.** Take the representative real-Serilog snippets P1 validated against Layer 1 (instance API + static `Log` + `LoggerConfiguration` code-config + ASP.NET wiring) and compile them in this project against the **mirror** with their original `using Serilog;` / `using Serilog.Events;` / `using Serilog.Context;` lines **unchanged**. The whole point: the namespaces resolve to the mirror, source is byte-identical to a real-Serilog program.

- [ ] **Step 2: Assert it RUNS.** Each snippet emits to an in-memory capturing sink; assert the produced `LogEvent` matches the canonical shape (level, message template, property names/values, value-model tree for `{@}`/`{$}`). Reuse P1's canonical comparer; do not re-implement it.

```bash
cd E:/dev/Herald.OSS && dotnet test tests/SerilogCompat/Layer2/Layer2CorpusTests.csproj -v minimal 2>&1 | tail -15
```
Expected: PASS (snippets compile against the mirror and produce equivalent events).

- [ ] **Step 3: Commit.**

```bash
git add tests/SerilogCompat/Layer2
git commit -m "test(serilog-compat): G-CORPUS.1 — real-Serilog corpus compiles+runs unchanged on the Layer-2 mirror"
```

---

### Task 7: DRY-tripwire — Layer-2 types contain **zero logic**

The hardest DRY rule in the initiative (Richard §A.3, Jared facade-placement): every Layer-2 member body is a bare forward. This task encodes that as an automated check so a future edit that grows logic in the mirror fails CI.

**Files:**
- Create: `tests/SerilogCompat/Layer2/Layer2ZeroLogicTests.cs` (in the main suite — it reflects over compiled IL, so it CAN share a compilation)
- Read first: pick the analysis mechanism — Roslyn syntax analysis over the Layer-2 `.cs` files (source-level, most precise for "no `if`/loop") **or** IL inspection (cyclomatic-complexity == 1 per method). Source-level is recommended (catches intent, not just branch count).

- [ ] **Step 1: Write the check** — enumerate every method/property accessor declared in the `Serilog` and `Serilog.AspNetCore` Layer-2 assemblies; assert each body is a **single forwarding statement/expression** (an invocation, member-access, or object-creation that names a Layer-1 twin), with **no** `IfStatement`, `ForStatement`, `ForEachStatement`, `WhileStatement`, `SwitchStatement`, `TryStatement`, `string.Format`/interpolation-with-logic, or local-with-computation. Allow: `throw new NotSupportedException(...)` for a *deliberately* unmirrored member (must be on an allowlist with a reason), and trivial null-forward (`x?.Twin`) only where the forward itself is the null-propagation.

```csharp
// tests/SerilogCompat/Layer2/Layer2ZeroLogicTests.cs (shape)
[Fact]
public void Layer2_member_bodies_contain_no_logic()
{
    foreach (var file in EnumerateLayer2SourceFiles()) // src/Compatibility/Layer2/**
    foreach (var member in ParseMemberBodies(file))
        Assert.True(
            IsSingleForwardingExpression(member),
            $"DRY tripwire: {member.Location} contains logic — Layer-2 types must forward only.");
}
```

- [ ] **Step 2: Run — expect PASS** (Tasks 2–5 were authored forward-only). If it fails, the failing member is the bug — fix the *mirror* (push the logic down to Layer 1), do **not** weaken the check.

- [ ] **Step 3: Commit.**

```bash
git add tests/SerilogCompat/Layer2/Layer2ZeroLogicTests.cs
git commit -m "test(serilog-compat): DRY tripwire — Layer-2 member bodies are forward-only (zero logic)"
```

---

### Task 8: G-LAYER2.1 — coexistence fails at **COMPILE** (`CS0433`), not runtime

Enforces the migration-runbook precondition mechanically (Jared coexistence correction; Echo G-LAYER2.1): Layer 2 must be the only `Serilog` in the graph. The proof is a project that references **both** the mirror and real Serilog and **fails to build** with `CS0433` (duplicate `Serilog.*` types).

**Files:**
- Create: `tests/SerilogCompat/Layer2/Coexistence/CoexistenceConflict.csproj` — references **both** the Layer-2 `Serilog` assembly **and** the real `Serilog` NuGet package; `<IsPackable>false</IsPackable>`; **excluded from the normal build** (it is *expected to fail compilation*).
- Create: `tests/SerilogCompat/Layer2/Coexistence/UsesSerilogType.cs` — a one-liner referencing `Serilog.Log` (forces the duplicate-type collision).
- Create: `tests/SerilogCompat/Layer2/Coexistence/G_Layer2_CoexistenceTests.cs` — a test in the **main** suite that shells out a `dotnet build` of the conflict project and asserts the **build fails with CS0433**.

- [ ] **Step 1: Build the conflict project** that references both Serilogs. Exclude it from solution-wide build (own folder, not globbed; or `<ExcludeFromBuild>`-style guard) so it never breaks the main build.

- [ ] **Step 2: Write the meta-test** that invokes the build and asserts the failure is exactly the coexistence collision:

```csharp
[Fact]
public void Layer2_and_realSerilog_coexistence_fails_at_compile_with_CS0433()
{
    var result = DotnetBuild("tests/SerilogCompat/Layer2/Coexistence/CoexistenceConflict.csproj");
    Assert.NotEqual(0, result.ExitCode);                 // build MUST fail
    Assert.Contains("CS0433", result.Output);            // duplicate-type, the structural wall
    Assert.Contains("Serilog", result.Output);
    // Negative: it must NOT be a *runtime* InvalidCastException that slipped through a successful compile.
}
```

- [ ] **Step 3: Run — expect PASS** (i.e., the conflict build correctly fails with CS0433, and the meta-test observes it).

```bash
cd E:/dev/Herald.OSS && dotnet test tests/Herald.OSS.Tests.csproj --filter "FullyQualifiedName~G_Layer2_Coexistence" -v minimal 2>&1 | tail -15
```

- [ ] **Step 4: Commit.**

```bash
git add tests/SerilogCompat/Layer2/Coexistence
git commit -m "test(serilog-compat): G-LAYER2.1 — Layer-2/real-Serilog coexistence fails at COMPILE (CS0433)"
```

---

### Task 9: AOT/trim clean (G-GAP.7) + full build + wave close

**Files:**
- Modify (if needed): the two Layer-2 csprojs (annotations only — never logic).

- [ ] **Step 1: AOT/trim check** for both Layer-2 assemblies — they publish with **no new** trim/AOT warnings vs the Herald.OSS baseline (a pure forwarder should be AOT-trivial; if a warning appears it is a sign the mirror does reflection/format it shouldn't — fix the mirror).

```bash
cd E:/dev/Herald.OSS && dotnet test tests/AOT/Herald.OSS.Aot.Tests.csproj -v minimal 2>&1 | tail -10
```

- [ ] **Step 2: Full compat build + test** (coordinate with Max's umbrella `build.sh`).

```bash
dotnet build src/Compatibility/Layer2/Serilog/MMP.Herald.Compat.Serilog.csproj -c Release 2>&1 | tail -5
dotnet build src/Compatibility/Layer2/Serilog.AspNetCore/MMP.Herald.Compat.Serilog.AspNetCore.csproj -c Release 2>&1 | tail -5
dotnet test tests/SerilogCompat/Layer2 -v minimal 2>&1 | tail -15
```
Expected: green; corpus passes; DRY tripwire green; CS0433 coexistence test green.

- [ ] **Step 3: Confirm assembly identity** — both assemblies are named exactly `Serilog` / `Serilog.AspNetCore`, net9 + net10 only, **unsigned** (no PublicKeyToken — by design; the migration runbook explains why a pre-compiled third-party sink still won't bind, which is the *whole reason* Layer 2 is final-cutover).

```bash
# sanity: AssemblyName + TFMs
grep -n "AssemblyName\|HeraldTargetFrameworks\|SignAssembly" src/Compatibility/Layer2/Serilog/MMP.Herald.Compat.Serilog.csproj
```
Expected: `AssemblyName=Serilog`, `net9.0;net10.0`, no `SignAssembly`.

- [ ] **Step 4: Document the migration-runbook precondition.** Add the precondition text to the migration runbook surface (consult Heather — P8 owns the README/runbook; P7 contributes the precondition paragraph): *Layer 2 is the only `Serilog` in the graph; stage on Layer 1 beside real Serilog, verify, then cut over to Layer 2 and remove all real-Serilog references in one step. G-LAYER2.1 enforces this at compile.* Reference it; do not duplicate the full runbook here.

- [ ] **Step 5: Final commit + note P7 done.**

```bash
git add -A docs/serilog-compat src/Compatibility/Layer2 tests/SerilogCompat
git commit -m "chore(serilog-compat): P7 Layer-2 mirror complete — zero-logic forwarders, corpus green, CS0433 coexistence pinned"
```

---

## Self-review notes

- **Spec coverage:** P7 delivers the two Layer-2 assemblies (`Serilog`, `Serilog.AspNetCore`), the full mirrored surface enumerated above (value model + level enum + template, call surface, `LoggerConfiguration` + `Serilog.Core` seams + `ITextFormatter` + `SelfLog`, the ASP.NET host/middleware/DI hooks), and three test families: **G-CORPUS.1** (corpus compiles+runs unchanged on the mirror), the **DRY tripwire** (zero logic in Layer 2), and **G-LAYER2.1** (CS0433 coexistence at compile). It documents the migration-runbook precondition (Layer 2 = only Serilog in the graph) and tests it rather than asserting it in prose.
- **Zero-logic discipline:** Task 7 is the hardest DRY rule in the initiative, encoded as an automated source-level check so a future edit that grows behaviour in the mirror fails CI — not a reviewer's memory.
- **Cross-plan dependencies (mirror-only, no new types authored here):** P1 (`ILogger`/`Log`/value model/level map/`LogContext`/arity-overload set), P2 (`LoggerConfiguration` + config objects + `LoggingLevelSwitch` wrapper), P3 (`ITextFormatter`/CLEF), P4 (`ILogEventSink`/`ILogEventEnricher`/`ILogEventPropertyFactory`/`IDestructuringPolicy`/`SelfLog` + the seam adapters that bridge mirrored interfaces), P6 (`UseSerilog`/`AddSerilog`/`UseSerilogRequestLogging`/`RequestLoggingOptions`). Each table row names its source plan; a missing twin is a FLAGGED cross-plan gap, never a faked type.
- **Under-specified Serilog surfaces FLAGGED:** (1) `Serilog.Core.Logger` — the concrete `CreateLogger()` return type that corpus code stores in fields — is named by the design only via `ILogger`/`LoggerConfiguration`, not as its own class; confirm the Layer-1 twin exists before mirroring. (2) The exact `Serilog.Configuration.*` config-object class shapes (return types of `WriteTo`/`Enrich`/`MinimumLevel`/`Destructure`/`Filter`/`AuditTo`) are named collectively, not individually; P7 mirrors whatever P2/P4 published and reconciles any rename. Both are flagged inline in the surface table.
- **net9/net10 only**, unsigned by design (no strong-name spoof), AOT/trim-clean — all standing rules honoured.

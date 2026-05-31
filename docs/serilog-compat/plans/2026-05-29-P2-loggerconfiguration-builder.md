# P2 — LoggerConfiguration Builder Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the Serilog-shaped `LoggerConfiguration` fluent type in `MMP.Herald.Serilog` — `MinimumLevel.*` (including `.Override(...)` and `.ControlledBy(...)`), `WriteTo.*` (Console/File/HTTP/TCP/UDP/Elasticsearch/OTLP/Null), `Enrich.*`, and `.CreateLogger()`. It is a **translator**: every fluent call mutates an underlying `QuickLogBuilder`, and `.CreateLogger()` calls `QuickLogBuilder.Build()`. It writes **zero** new pipeline-construction logic.

**Architecture (from `design-round-richard.md` §C):** "The `LoggerConfiguration` builder is a **translator onto `QuickLogBuilder`** (still produces JSON → JSON drives construction, honouring JSON-as-source-of-truth)." A Serilog user writes `new LoggerConfiguration().MinimumLevel.Debug().WriteTo.Console().Enrich.WithMachineName().CreateLogger()`. Each sub-configuration object (`MinimumLevelConfiguration`, `LoggerSinkConfiguration`, `LoggerEnrichmentConfiguration`) holds a reference back to the one `QuickLogBuilder` instance and calls the matching `With*` method. There is no parallel state, no second JSON emitter, no duplicated default-trio/level/sink logic — `QuickLogBuilder.BuildJsonConfig()` stays the single source of pipeline shape.

**Tech Stack:** C# / .NET (net9 + net10 for the compat assembly; `MMP.Herald.Serilog.csproj` created in P1), xUnit (`tests/Herald.OSS.Tests.csproj`), `bash build.sh`. Real Serilog referenced **only in the test project** as the parity oracle (Layer-1 coexists with real Serilog — that is the whole point of Layer 1).

**Dependencies:** P0 landed (levels are `information`/`warning`/`fatal`/`verbose`; the `QuickLogBuilder` literals at `QuickLogBuilder.cs:50,682-684,1003-1008` are already renamed by P0). P1 exists: `MMP.Herald.Serilog` assembly, the `Serilog.ILogger`-shaped logger, the value-model mirror, the static `Log` facade, **and the `LogEventLevel` enum + the level map** (`LogEventLevel ⇄ Herald level key`). P2 consumes P1's level map; it does not author one.

---

## Cross-plan types this plan introduces (owned by P2, consumed by P5/P7)

| Type | Namespace | Role |
|---|---|---|
| `LoggerConfiguration` | `MMP.Herald.Serilog` | Root translator; holds the one `QuickLogBuilder`; exposes `MinimumLevel`/`WriteTo`/`Enrich`; `.CreateLogger()` → `Build()` |
| `MinimumLevelConfiguration` | `MMP.Herald.Serilog.Configuration` | `.Verbose()`…`.Fatal()`, `.Is(LogEventLevel)`, `.Override(source, level)`, `.ControlledBy(LoggingLevelSwitch)` |
| `LoggerSinkConfiguration` | `MMP.Herald.Serilog.Configuration` | `WriteTo.Console/File/Http/...`; one method per Herald-mapped sink |
| `LoggerEnrichmentConfiguration` | `MMP.Herald.Serilog.Configuration` | `Enrich.With(...)`/`.WithProperty(...)`/`.FromLogContext()` |
| `LoggingLevelSwitch` | `MMP.Herald.Serilog.Core` | Serilog-shaped mirror of native `LogLevelSwitch` (structural match) |

**Flag for P1 owner:** P2 calls a P1-owned helper to turn `LogEventLevel` (or a Serilog level name) into the Herald level **key string** that `QuickLogBuilder.WithMinimumLevel(string)` expects. design-round-richard.md names the *mapping* (Tier 2) but does not name the *helper type/method*. **P2 assumes P1 exposes `LogEventLevelMap.ToHeraldKey(LogEventLevel)` returning `"verbose"|"debug"|"information"|"warning"|"error"|"fatal"`.** If P1 named it differently, Task 1 Step 1 rebinds to the real symbol; if P1 did **not** build it, that is a P1 gap — STOP and raise it (P2 must not author the level map, or DRY breaks across P1/P2).

---

## Translation map (authoritative — every Serilog surface → existing `QuickLogBuilder` call)

| Serilog code | Translates to (existing `QuickLogBuilder`) | Source ref |
|---|---|---|
| `.MinimumLevel.Debug()` / `.Information()` / … | `WithMinimumLevel(LogEventLevelMap.ToHeraldKey(level))` | `Pipeline.cs:66` |
| `.MinimumLevel.Is(LogEventLevel)` | same, with the passed level | — |
| `.MinimumLevel.ControlledBy(LoggingLevelSwitch)` | `WithFastDynamicLevel(LogLevelSwitch)` | `FastPath.cs:131` |
| `.MinimumLevel.Override("Microsoft", level)` | `CategoryLevelSwitchMap` + `WithFastDynamicLevel(switch, map)` | `FastPath.cs:205` |
| `.WriteTo.Console()` | `WithConsoleSink()` | `Sinks.cs:23` |
| `.WriteTo.File(path)` | `WithFileSink(path)` | `Sinks.cs:98` |
| `.WriteTo.Http(uri)` | `WithHttpJsonSink(endpoint)` | `NetworkSinks.cs:23` |
| `.WriteTo.TCPSink(host,port)` | `WithTcpJsonLineSink(host, port)` | `NetworkSinks.cs:32` |
| `.WriteTo.UDPSink(host,port)` | `WithUdpJsonLineSink(host, port)` | `NetworkSinks.cs:42` |
| `.WriteTo.Elasticsearch(url)` | `WithElasticsearchSink(url)` | `NetworkSinks.cs:52` |
| `.WriteTo.OpenTelemetry(endpoint)` | `WithOtlpJsonSink(endpoint)` | `NetworkSinks.cs:100` |
| `.WriteTo.Sink(nullSink)` / null | `WithNullSink()` | `Sinks.cs:35` |
| `.Enrich.With(ILogEventEnricher)` | `WithEnrichers(ILogEnricher)` (via P1 mirror bridge) | `Pipeline.cs:262` |
| `.Enrich.WithProperty(name, value)` | `WithFastEnrichment(LogProperty)` | `FastPath.cs:104` |
| `.Enrich.FromLogContext()` | scope/`PushProperty` already wired in P1 — no-op here (records nothing new) | — |
| `.CreateLogger()` | `Build()` → wrap result as P1 `Serilog.ILogger` | `QuickLogBuilder.cs:387` |

**The per-sink `restrictedToMinimumLevel:` argument** (Serilog's most common `WriteTo` option) maps to the `minLevel` parameter every `WithXSink` already accepts — pass `LogEventLevelMap.ToHeraldKey(level)`.

**Out-of-scope sinks** (`WriteTo.Seq`, `WriteTo.MSSqlServer`, community sinks) are **not** methods on `LoggerSinkConfiguration`. A consumer calling them gets a compile error (the method does not exist), which is the honest hard-wall behaviour — the parity audit (P8) names them. The `appsettings.json` *name-based* path that fails loud is P5's concern, not P2's.

---

### Task 0: the-fool pre-mortem gate (no code)

**Files:**
- Create: `docs/serilog-compat/plans/P2-translator-premortem.md`

- [ ] **Step 1: Run the pre-mortem.** Invoke `Skill(the-fool)` framed as: *"This translator mutates one shared `QuickLogBuilder`. Where does the translation silently diverge from the equivalent hand-written `QuickLogBuilder` config — a sink mapped to the wrong kind, a level rounded to the wrong key, `.Override` losing a category, a fluent call applied to a stale builder, or two `.WriteTo` calls colliding on shared state?"* Capture each divergence mode.
- [ ] **Step 2: Write the risk list** to `P2-translator-premortem.md` — each risk + the Task/test below that catches it. A risk with no catching test means a test is missing.
- [ ] **Step 3: Commit.**

```bash
git add docs/serilog-compat/plans/P2-translator-premortem.md
git commit -m "docs(serilog-compat): the-fool pre-mortem on the LoggerConfiguration translator"
```

---

### Task 1: Parity-oracle fixture + `LoggingLevelSwitch` mirror (write the harness first)

The whole suite leans on one oracle: a Serilog-code snippet and the equivalent `QuickLogBuilder` snippet must produce the **same `JsonLoggingConfig` shape**. `QuickLogBuilder.ExportConfigJson()` (`QuickLogBuilder.cs:647`) gives the canonical JSON for free — diff the translator's builder against a hand-built builder. No pipeline bootstrap needed for shape parity, which keeps the oracle cheap and deterministic.

**Files:**
- Test harness: `tests/Serilog/Configuration/TranslatorParityOracle.cs` (create)
- Product: `src/Serilog/Core/LoggingLevelSwitch.cs` (create — Serilog-shaped mirror of native `LogLevelSwitch`)

- [ ] **Step 1: Confirm the P1 level-map symbol.** Grep `MMP.Herald.Serilog` for the `LogEventLevel`→key helper (assumed `LogEventLevelMap.ToHeraldKey`). Rebind every reference below to the real name. If absent → STOP, raise the P1 gap (see cross-plan note).

- [ ] **Step 2: Write the oracle helper.** A static `AssertSameShape(Action<LoggerConfiguration> serilogSide, Action<QuickLogBuilder> heraldSide)` that builds each, calls `ExportConfigJson()` on both underlying builders, and asserts the JSON is equal. (The translator exposes its inner builder to the test via an `internal` accessor + `InternalsVisibleTo` — confirm P1 already set `InternalsVisibleTo` for the test assembly; add it if not.)

- [ ] **Step 3: Write `LoggingLevelSwitch` (the-fool item #4 — structural match, NOT a gap).** Mirror Serilog's shape over native `LogLevelSwitch`:

```csharp
// src/Serilog/Core/LoggingLevelSwitch.cs
// Serilog-shaped mirror of MMP.Herald.Levels.LogLevelSwitch (structural match —
// the-fool disposition #4). Holds the native switch; .MinimumLevel get/set
// forwards through the LogEventLevel<->Herald key map. ZERO behaviour of its own.
#nullable enable
using MMP.Herald.Levels;

namespace MMP.Herald.Serilog.Core;

public sealed class LoggingLevelSwitch
{
    // The native switch is the single source of truth; this type is a shape adapter.
    internal LogLevelSwitch Native { get; }

    public LoggingLevelSwitch(LogEventLevel minimumLevel = LogEventLevel.Information)
        => Native = new LogLevelSwitch(LogEventLevelMap.ToHeraldLevel(minimumLevel)); // P1 helper

    public LogEventLevel MinimumLevel
    {
        get => LogEventLevelMap.ToSerilog(Native.MinimumLevel);   // P1 helper
        set => Native.MinimumLevel = LogEventLevelMap.ToHeraldLevel(value);
    }
}
```

`ToHeraldLevel` (returns the `LogLevel` object, not just the key) is a second P1 helper assumed here — flag if P1 only exposes the key-string form; `LogLevelSwitch`'s ctor needs the `LogLevel`.

- [ ] **Step 4: Pin `LoggingLevelSwitch` parity (G-GAP.3).** Test: construct one at `Warning`, assert `.MinimumLevel == LogEventLevel.Warning`; flip to `Error`, assert the *native* switch's `MinimumLevel.Key == "error"`. This pins the structural match so a future divergence is caught.

- [ ] **Step 5: Run — expect the `LoggingLevelSwitch` test to PASS, oracle helper to compile.** Commit.

```bash
git add tests/Serilog/Configuration/TranslatorParityOracle.cs src/Serilog/Core/LoggingLevelSwitch.cs tests/Serilog/Configuration/LoggingLevelSwitchTests.cs
git commit -m "test(serilog): translator parity oracle + LoggingLevelSwitch mirror (G-GAP.3)"
```

---

### Task 2: `LoggerConfiguration` root + `MinimumLevel.*` (TDD)

**Files:**
- Test: `tests/Serilog/Configuration/MinimumLevelConfigurationTests.cs` (create)
- Product: `src/Serilog/LoggerConfiguration.cs`, `src/Serilog/Configuration/MinimumLevelConfiguration.cs` (create)

- [ ] **Step 1: Write the failing tests** against the oracle:
  - `MinimumLevel.Debug()` ⇔ `WithMinimumLevel("debug")`.
  - `MinimumLevel.Is(LogEventLevel.Fatal)` ⇔ `WithMinimumLevel("fatal")` (the `critical→fatal` rename trap — assert `"fatal"`, never `"critical"`).
  - `MinimumLevel.Verbose()` ⇔ `WithMinimumLevel("verbose")`.
  - `MinimumLevel.ControlledBy(new LoggingLevelSwitch(Warning))` ⇔ `WithFastDynamicLevel(LogLevelSwitch.For(<warning>))` — assert the JSON `FastPathDynamicLevel` block matches (`QuickLogBuilder.cs:831`).
  - `MinimumLevel.Override("Microsoft", LogEventLevel.Warning)` ⇔ a `CategoryLevelSwitchMap` with `Microsoft→warning` fed to `WithFastDynamicLevel(switch, map)` — assert the JSON `FastPathDynamicLevel.CategoryOverrides` snapshot (`QuickLogBuilder.cs:877`) contains `Microsoft→warning`.

- [ ] **Step 2: Run — expect FAIL** (types not defined).

- [ ] **Step 3: Implement `LoggerConfiguration`** as the root translator. It owns one `QuickLogBuilder`; sub-configs reference it:

```csharp
// src/Serilog/LoggerConfiguration.cs
#nullable enable
using MMP.Herald.Quick;
using MMP.Herald.Serilog.Configuration;

namespace MMP.Herald.Serilog;

public sealed class LoggerConfiguration
{
    // The one real builder. Every sub-config mutates THIS — no parallel state.
    internal QuickLogBuilder Builder { get; } = QuickLogBuilder.Create();

    public MinimumLevelConfiguration MinimumLevel { get; }
    public LoggerSinkConfiguration WriteTo { get; }
    public LoggerEnrichmentConfiguration Enrich { get; }

    public LoggerConfiguration()
    {
        MinimumLevel = new MinimumLevelConfiguration(this);
        WriteTo = new LoggerSinkConfiguration(this);
        Enrich = new LoggerEnrichmentConfiguration(this);
    }

    // Serilog's CreateLogger returns Serilog.Core.Logger : Serilog.ILogger.
    // P1 owns the ILogger mirror; here we just Build() and hand the result over.
    public global::Serilog.ILogger CreateLogger()
        => SerilogLoggerAdapter.FromBuild(Builder.Build()); // P1-owned adapter
}
```

`SerilogLoggerAdapter.FromBuild(PipelineBuildResult)` is the P1 bridge from a built Herald pipeline to the `Serilog.ILogger` mirror. **Flag:** if P1 named the static `Log` facade's commit path differently (e.g. `QuickLogResult` → logger), rebind. P2 must not re-implement logger wrapping.

`MinimumLevelConfiguration` (return `LoggerConfiguration` so the fluent chain continues, matching Serilog):

```csharp
// src/Serilog/Configuration/MinimumLevelConfiguration.cs — each verb sets the floor, returns root.
public LoggerConfiguration Information() => Is(LogEventLevel.Information);
public LoggerConfiguration Is(LogEventLevel level)
{
    _root.Builder.WithMinimumLevel(LogEventLevelMap.ToHeraldKey(level)); // P1 helper
    return _root;
}
public LoggerConfiguration ControlledBy(LoggingLevelSwitch levelSwitch)
{
    _root.Builder.WithFastDynamicLevel(levelSwitch.Native);
    return _root;
}
public LoggerConfiguration Override(string source, LogEventLevel level)
{
    // Accumulate into ONE CategoryLevelSwitchMap across repeated .Override calls.
    _overrides ??= new CategoryLevelSwitchMap(/* default switch from current floor */);
    _overrides.SetCategoryLevel(source, LogEventLevelMap.ToHeraldLevel(level));
    _root.Builder.WithFastDynamicLevel(_globalSwitch, _overrides);
    return _root;
}
```

  **the-fool catch (Task 0):** repeated `.Override(...)` must accumulate into the *same* map, not replace it — pin this in Step 1 with a two-`Override` test asserting both categories survive. `.ControlledBy` then `.Override` (and vice-versa) must share the global switch — pin the ordering both ways.

- [ ] **Step 4: Run — expect PASS.** Commit.

```bash
git add src/Serilog/LoggerConfiguration.cs src/Serilog/Configuration/MinimumLevelConfiguration.cs tests/Serilog/Configuration/MinimumLevelConfigurationTests.cs
git commit -m "feat(serilog): LoggerConfiguration root + MinimumLevel translator (incl Override/ControlledBy)"
```

---

### Task 3: `WriteTo.*` sink mapping (TDD, table-driven)

**Files:**
- Test: `tests/Serilog/Configuration/LoggerSinkConfigurationTests.cs` (create)
- Product: `src/Serilog/Configuration/LoggerSinkConfiguration.cs` (create)

- [ ] **Step 1: Write the failing oracle tests**, one per mapped sink in the translation map (Console/File/Http/TCP/UDP/Elasticsearch/OpenTelemetry/Null). Each asserts the translator's `ExportConfigJson()` equals the hand-built `WithXSink(...)` builder's JSON — so a wrong-kind mapping (e.g. OTLP routed to HTTP) fails loudly. Include one `restrictedToMinimumLevel: LogEventLevel.Error` case asserting the sink's `minLevel` lands as `"error"`.

- [ ] **Step 2: Run — expect FAIL.**

- [ ] **Step 3: Implement `LoggerSinkConfiguration`** — one thin method per sink, each forwarding to the existing `With*Sink`. Zero logic beyond level-key translation:

```csharp
// src/Serilog/Configuration/LoggerSinkConfiguration.cs — pure forwarders.
public LoggerConfiguration Console(LogEventLevel restrictedToMinimumLevel = LogEventLevel.Verbose)
{
    _root.Builder.WithConsoleSink(minLevel: Floor(restrictedToMinimumLevel));
    return _root;
}
public LoggerConfiguration File(string path, LogEventLevel restrictedToMinimumLevel = LogEventLevel.Verbose)
{
    _root.Builder.WithFileSink(path, minLevel: Floor(restrictedToMinimumLevel));
    return _root;
}
public LoggerConfiguration Http(string requestUri, LogEventLevel restrictedToMinimumLevel = LogEventLevel.Verbose)
{
    _root.Builder.WithHttpJsonSink(requestUri, minLevel: Floor(restrictedToMinimumLevel));
    return _root;
}
// TCPSink / UDPSink / Elasticsearch / OpenTelemetry / Sink(null) follow the same one-line shape.

// Floor: Verbose (the default) means "no per-sink floor" -> pass null so the
// pipeline floor governs; any other level passes its Herald key.
private static string? Floor(LogEventLevel level)
    => level == LogEventLevel.Verbose ? null : LogEventLevelMap.ToHeraldKey(level);
```

  **DRY tripwire:** any `if`/loop building sink JSON inside this file is a reject — the sink JSON is owned by the serializer registry behind `WithXSink` (`QuickLogBuilder.cs:1071-1089`). This file only picks the method and translates the level.

- [ ] **Step 4: Run — expect PASS.** Commit.

```bash
git add src/Serilog/Configuration/LoggerSinkConfiguration.cs tests/Serilog/Configuration/LoggerSinkConfigurationTests.cs
git commit -m "feat(serilog): WriteTo.* sink translator (Console/File/HTTP/TCP/UDP/ES/OTLP/Null)"
```

---

### Task 4: `Enrich.*` mapping (TDD)

**Files:**
- Test: `tests/Serilog/Configuration/LoggerEnrichmentConfigurationTests.cs` (create)
- Product: `src/Serilog/Configuration/LoggerEnrichmentConfiguration.cs` (create)

- [ ] **Step 1: Write the failing tests:**
  - `.Enrich.WithProperty("App", "Checkout")` ⇔ `WithFastEnrichment(LogProperty("App","Checkout"))` — assert the JSON `FastPathEnrichment` block (`QuickLogBuilder.cs:890`) carries `App→Checkout`.
  - `.Enrich.With(customEnricher)` ⇔ `WithEnrichers(<bridged ILogEnricher>)` — uses the P1 value-model enricher bridge (Serilog `ILogEventEnricher` → Herald `ILogEnricher`). **Flag:** the bridge is **P1/P4 territory** (S2 seam). P2's job is only to *route* a bridged enricher into `WithEnrichers`. If the bridge type does not exist yet, this sub-case is `[Fact(Skip="awaits P4 S2 enricher bridge")]` with a tracking note — do not stub a fake bridge here (that would duplicate P4).
  - `.Enrich.FromLogContext()` ⇔ no builder mutation (P1 already wires scope/`PushProperty` ambient capture) — assert the JSON is unchanged vs a no-enrich builder.

- [ ] **Step 2: Run — expect FAIL** (type not defined).
- [ ] **Step 3: Implement `LoggerEnrichmentConfiguration`** as forwarders; `WithProperty` builds a `LogProperty` and calls `WithFastEnrichment`; `With` routes the P1-bridged enricher into `WithEnrichers`; `FromLogContext` returns `_root` unchanged.
- [ ] **Step 4: Run — expect PASS.** Commit.

```bash
git add src/Serilog/Configuration/LoggerEnrichmentConfiguration.cs tests/Serilog/Configuration/LoggerEnrichmentConfigurationTests.cs
git commit -m "feat(serilog): Enrich.* translator (WithProperty/With/FromLogContext)"
```

---

### Task 5: End-to-end `.CreateLogger()` parity + real-Serilog corpus (G-CORPUS.1 slice)

This is the win-condition test for P2: a full Serilog-code snippet produces the **same pipeline** as the equivalent `QuickLogBuilder` config, and the built logger actually emits the right events.

**Files:**
- Test: `tests/Serilog/Configuration/CreateLoggerParityTests.cs` (create)

- [ ] **Step 1: Write the shape-parity test.** The canonical snippet:

```csharp
// Serilog side
new LoggerConfiguration()
    .MinimumLevel.Debug()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .WriteTo.Console()
    .WriteTo.File("app.ndjson", restrictedToMinimumLevel: LogEventLevel.Error)
    .Enrich.WithProperty("App", "Checkout")
    .CreateLogger();
// Herald side (the oracle's reference)
QuickLogBuilder.Create()
    .WithMinimumLevel("debug")
    .WithFastDynamicLevel(<switch>, <map Microsoft->warning>)
    .WithConsoleSink()
    .WithFileSink("app.ndjson", minLevel: "error")
    .WithFastEnrichment(new LogProperty("App", "Checkout"));
```

  Assert both `ExportConfigJson()` outputs are equal.

- [ ] **Step 2: Write the behavioural test** (the run side, not just shape). `.CreateLogger()` against a real `QuickLogBuilder` build with an in-memory capturing sink (P1/P0 `TestLoggers.CreateCapturing` fixture — reuse, don't rebuild); emit `Information("hi {X}", 1)`; assert the captured event has level `information` and property `X=1`. This proves the translated builder actually *builds* and dispatches, not just that the JSON matches.

- [ ] **Step 3: Real-Serilog oracle cross-check (Layer-1 coexistence).** Run the *same snippet* through **real Serilog** (referenced in the test project) into a Serilog in-memory sink, and through the translator into Herald's capturing sink; diff the resulting events under the canonical-shape rule (ingress↔output canonical-equivalence). This is the G-CORPUS.1 `LoggerConfiguration`-code slice — P1 covers the instance-API + static-`Log` slices.

- [ ] **Step 4: Run — expect PASS.** Commit.

```bash
git add tests/Serilog/Configuration/CreateLoggerParityTests.cs
git commit -m "test(serilog): CreateLogger end-to-end parity + real-Serilog corpus slice (G-CORPUS.1)"
```

---

### Task 6: Named-gap regression pins (every gap → a test)

**Files:**
- Test: `tests/Serilog/Configuration/SinkWallTests.cs`, `tests/Serilog/Configuration/TranslatorGapTests.cs` (create)

- [ ] **Step 1: `WriteTo` hard-wall is compile-shaped, pinned by reflection (G-SINK-WALL.1 slice).** P2 cannot make `WriteTo.Seq(...)` a compile error *and* test it in the same assembly, so pin the contract by reflection: assert `LoggerSinkConfiguration` exposes **exactly** the mapped sink methods and **no** method named `Seq`/`MSSqlServer`/`Datadog`. This catches an accidental future stub that would silently "support" an out-of-scope sink. (The loud-named *runtime* failure for name-based config is P5's `appsettings.json` path — cross-reference, don't duplicate.)

- [ ] **Step 2: `LoggingLevelSwitch` parity already pinned (Task 1 Step 4)** — confirm it is in the suite (G-GAP.3).

- [ ] **Step 3: The four Herald-extra levels are reachable through the translator only via `Is`/key, never via a Serilog verb** — assert `MinimumLevelConfiguration` has no `Notice`/`Success`/`Security`/`Metric` method (they have no Serilog counterpart; surfacing them as verbs would be a false-compat signal). Pins the design boundary.

- [ ] **Step 4: Run — expect PASS.** Commit.

```bash
git add tests/Serilog/Configuration/SinkWallTests.cs tests/Serilog/Configuration/TranslatorGapTests.cs
git commit -m "test(serilog): translator gap pins (sink-wall surface, level-verb boundary, G-GAP.3)"
```

---

### Task 7: Build, AOT, DRY self-audit, wave close

- [ ] **Step 1: Full build + test.**

```bash
cd E:/dev/Herald.OSS && bash build.sh --all --test 2>&1 | tail -20
```

- [ ] **Step 2: AOT-clean check (G-GAP.7).** The translator is plain method forwarding (no reflection); confirm no new trim/AOT warnings vs baseline.

```bash
dotnet test tests/AOT/Herald.OSS.Aot.Tests.csproj -v minimal 2>&1 | tail -10
```

- [ ] **Step 3: DRY self-audit.** Grep the four config files for any pipeline-construction logic — `JsonLoggingConfig`, `JsonLogSinkConfig`, sink-kind strings, level-default lists. Expected: **none**. The translator only calls `With*` and the P1 level map.

```bash
grep -rn "JsonLoggingConfig\|JsonLogSinkConfig\|new JsonLog\|KnownSinkKinds" src/Serilog/Configuration src/Serilog/LoggerConfiguration.cs || echo "clean — pure translator"
```

- [ ] **Step 4: Final commit + note P2 done.**

```bash
git add -A docs/serilog-compat src/Serilog tests/Serilog
git commit -m "chore(serilog-compat): P2 LoggerConfiguration translator complete — ready for P5/P6"
```

---

## Self-review notes

- **Spec coverage:** P2 implements scope-PRD In-scope §1's `LoggerConfiguration` code-config line — `MinimumLevel.*` (incl `.Override`/`.ControlledBy`), `WriteTo.*` (the eight Herald-mapped sinks), `Enrich.*`, `.CreateLogger()` — plus test-inventory G-GAP.3 (`LoggingLevelSwitch`), the G-CORPUS.1 `LoggerConfiguration`-code slice, and the G-SINK-WALL.1 surface pin.
- **CUPID/DRY:** the translator writes zero pipeline-construction logic. Every fluent call is a one-line forward to an existing `QuickLogBuilder.With*`. `ExportConfigJson()` is the parity oracle — a hand-built builder and a translated builder must serialize identically, which is the strongest cheap guard against silent divergence.
- **Cross-plan dependency surface (flag to P1 owner):** P2 assumes P1 exposes `LogEventLevelMap.ToHeraldKey(LogEventLevel)`, `LogEventLevelMap.ToHeraldLevel(LogEventLevel)`, `LogEventLevelMap.ToSerilog(LogLevel)`, the `SerilogLoggerAdapter.FromBuild(PipelineBuildResult)` logger bridge, the value-model `ILogEventEnricher`→`ILogEnricher` bridge (also P4 S2), and `InternalsVisibleTo` for the test assembly. If any is missing or named differently, Task 1 Step 1 rebinds or STOPS.
- **Boundaries held:** the `.Enrich.With(custom)` bridge stays P1/P4 (skipped here, not stubbed); the `appsettings.json` name-based loud-fail stays P5; the output-template `:lj`/`u3` grammar stays P3. P2 touches none of them.
- **Open decisions for Richard:** (1) Does `.MinimumLevel.ControlledBy` followed by a plain `.MinimumLevel.Debug()` keep the switch or replace it with a static floor? Serilog lets the switch win; confirm the translator mirrors that (current plan: last-writer-wins per the underlying builder field — verify against Serilog semantics and pin a test). (2) `.WriteTo.File` rolling-policy arguments (`rollingInterval`, `fileSizeLimitBytes`) — v1 maps the path + level only; the rolling overload (`WithFileSink(path, interval, ...)`, `Sinks.cs:114`) is a fast-follow unless Richard wants it in P2. (3) Confirm `MinimumLevel.Override` should use the kernel-fast `WithFastDynamicLevel` path (kept kernel-eligible) rather than the legacy `WithDynamicLevels()`/`WithCategoryLevelOverride` path (`Pipeline.cs:686-692`) — the fast path is the plan's default for zero-regression, but it changes runtime-mutability semantics slightly.

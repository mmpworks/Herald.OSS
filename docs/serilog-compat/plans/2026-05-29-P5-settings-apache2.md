# P5 — Settings (Apache-2.0) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a NEW standalone Apache-2.0 project `Herald.OSS.Serilog.Settings` that gives a Serilog user `ReadFrom.Configuration(IConfiguration)` over Herald's engine. It parses the Serilog `appsettings.json` schema (`MinimumLevel` incl. `Override`, `WriteTo` with `Name`/`Args`, `Enrich`, `Using`) and drives the P2 `LoggerConfiguration` shim. A public **`LoggerSinkRegistry`/`LoggerEnricherRegistry`** (S-NEW-1) is consulted before the parser fails, pre-seeded with Herald's built-ins, so a config-named in-house sink resolves instead of forcing a parser fork. An unresolved or third-party name (e.g. `Serilog.Sinks.Seq`) fails **loud and named** with the identity-wall reason, never a silent drop.

**Architecture (the binding verdict — Jared, REIMPLEMENT):** The CLR binds assembly refs by full identity including `PublicKeyToken`. Serilog is strong-named (`PublicKeyToken=24c2f752a8e58a10`); Herald.OSS is unsigned (verified — no `SignAssembly` in `Herald.OSS.csproj`). The real `Serilog.Settings.Configuration` add-on is compiled against the strong-named `Serilog` and **cannot bind to the unsigned shim**. So this is "build a parser," not "reference a package." The parser reads the same `appsettings.json` schema and writes into the P2 builder. It does not load the real `Serilog.Settings.Configuration` at all.

**Where it sits (5-assembly topology, from design-round-richard §"Assembly topology"):**

```
MMP.Herald.Serilog            (Layer 1 — P1/P2: LoggerConfiguration shim + static Log facade)   ← extension TARGET
Serilog                       (Layer 2 — literal mirror)
Herald.OSS.Serilog.Settings   (Apache-2.0, standalone — THIS PLAN; reimplemented parser)
```

The settings project references **Layer 1** (`MMP.Herald.Serilog`), not Layer 2, so it can be linked independently by anyone, alongside the real Serilog if they want, without dragging the `Serilog.*` mirror into their graph.

**Tech Stack:** C# / .NET, **net9.0;net10.0 only** (no net8 — the compat layer is net9/net10 per the hard constraint; this differs from Herald.OSS core's `net8;net9;net10`). xUnit (a new `Herald.OSS.Serilog.Settings.Tests.csproj`). `Microsoft.Extensions.Configuration.Abstractions` for `IConfiguration`. Apache-2.0 license header on every source file.

**Extension target (from P1/P2 — reference these types; FLAG if the real shape differs at implementation time):**
- `MMP.Herald.Serilog.LoggerConfiguration` — the P2 builder shim with `MinimumLevel.*`, `WriteTo.*`, `Enrich.*`, `.CreateLogger()`, translating onto native `QuickLogBuilder` (which is **not** renamed — Dissent D-1).
- `MMP.Herald.Serilog.ILogger` / `Log.CloseAndFlush` — P1 call surface (referenced only indirectly via `CreateLogger()` in the corpus round-trip test).
- Native sink verbs the parser maps `WriteTo.Name` onto (verified present on `QuickLogBuilder.Sinks.cs` / `.NetworkSinks.cs`): `WithConsoleSink`, `WithFileSink`, `WithHttpJsonSink`, `WithTcpJsonLineSink`, `WithUdpJsonLineSink`, `WithElasticsearchSink`, `WithOtlpJsonSink` / `WithOtlpProtobufSink`, `WithNullSink`. Enrichers via `WithEnrichers` / the named built-in enricher verbs.

**Cross-plan types this plan DEPENDS ON (must exist from P1/P2 — do not author here):**
- `MMP.Herald.Serilog.LoggerConfiguration` (and its nested `MinimumLevel`/`WriteTo`/`Enrich` configuration objects).
- The level-key vocabulary post-P0 rename (`verbose/debug/information/warning/error/fatal` + Herald extras). The parser maps Serilog level names case-insensitively onto these.

**Cross-plan types this plan INTRODUCES (new public surface owned by P5 — FLAGGED for the audit):**
- `Herald.OSS.Serilog.Settings.LoggerSinkRegistry` (public; S-NEW-1).
- `Herald.OSS.Serilog.Settings.LoggerEnricherRegistry` (public; S-NEW-1).
- `Herald.OSS.Serilog.Settings.ConfigurationLoggerConfigurationExtensions` — the `ReadFrom.Configuration(IConfiguration)` extension on the Layer-1 `LoggerConfiguration`.
- `Herald.OSS.Serilog.Settings.SinkResolutionException` (or reuse a P1 named exception if one exists — FLAG and resolve at impl time) — the loud-named failure carrying the sink name + identity-wall reason.

**Build / packaging notes for Max (cross-plan — do NOT silently absorb):**
- `Microsoft.Extensions.Configuration.Abstractions` is **not** yet pinned in `Directory.Packages.props` (central management is on). Max adds the `PackageVersion` entry before this builds.
- The new test project and the new product project both need adding to whatever solution/`build.sh` enumerates the compat projects (Max's lane). This plan creates the csprojs; wiring them into `build.sh --all` is Max's mechanical follow-up.
- net9/net10 TFM on these two projects overrides the repo-default `net8;net9;net10` — set `<TargetFrameworks>net9.0;net10.0</TargetFrameworks>` explicitly in each csproj (do not inherit the net8 row).

---

### Task 0: the-fool pre-mortem gate (no product code)

**Files:**
- Create: `docs/serilog-compat/plans/P5-settings-premortem.md`

- [ ] **Step 1: Run the pre-mortem.** Invoke `Skill(the-fool)` framed as: *"A reimplemented appsettings.json parser drives the Herald shim. Where does it silently produce a different logger than the same config on real Serilog, or silently drop a configured sink/enricher instead of failing loud?"* Capture the failure modes — e.g. a `WriteTo` entry with a typo'd `Name` parsed to "no sink" instead of an error; `MinimumLevel.Override` for a sub-namespace dropped because only the global minimum is read; a registered custom name shadowed by a built-in of the same name; `Args` of the wrong type coerced silently; a Seq entry that parses to a no-op because the loud-fail check ran only on the *empty* registry, not the *unresolved-name* case.

- [ ] **Step 2: Write the risk list** to `P5-settings-premortem.md` — each risk + which Task below mitigates it. Any risk without a mitigating task means a task is missing from this plan.

- [ ] **Step 3: Commit**

```bash
git add docs/serilog-compat/plans/P5-settings-premortem.md
git commit -m "docs(serilog-compat): the-fool pre-mortem on the appsettings parser"
```

---

### Task 1: Scaffold the Apache-2.0 project + LICENSE + license headers

**Files:**
- Create: `compat/Herald.OSS.Serilog.Settings/Herald.OSS.Serilog.Settings.csproj`
- Create: `compat/Herald.OSS.Serilog.Settings/LICENSE` (Apache-2.0 full text — copy the repo-root `LICENSE`, which is already Apache-2.0)
- Create: `compat/Herald.OSS.Serilog.Settings/AssemblyInfo.cs` (or a shared header convention)
- Read first: `Herald.OSS.csproj` (package metadata shape), `Directory.Build.props` (TFM/lang rules — note this project overrides to net9/net10).

- [ ] **Step 1: Create the csproj.** net9/net10 only, Apache-2.0 package metadata, `IsAotCompatible`/trim analyzers on (the compat layer must stay AOT-clean — G-GAP.7). ProjectReference to the Layer-1 `MMP.Herald.Serilog` project. PackageReference to `Microsoft.Extensions.Configuration.Abstractions`. The csproj does **not** inherit the net8 TFM row.

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <RootNamespace>Herald.OSS.Serilog.Settings</RootNamespace>
    <AssemblyName>Herald.OSS.Serilog.Settings</AssemblyName>
    <PackageId>Herald.OSS.Serilog.Settings</PackageId>
    <!-- Compat layer is net9/net10 only — override the repo-default net8 row. -->
    <TargetFrameworks>net9.0;net10.0</TargetFrameworks>
    <Description>Apache-2.0 appsettings.json binding for the Herald.OSS Serilog-compat shim. Reimplements Serilog.Settings.Configuration's schema against Herald's engine; the real strong-named add-on cannot bind to the unsigned shim.</Description>
    <PackageLicenseExpression>Apache-2.0</PackageLicenseExpression>
    <Authors>Steven Muchow</Authors>
    <Company>MMPWorks LLC</Company>
    <Copyright>Copyright (c) 2026 MMPWorks LLC</Copyright>
    <IsAotCompatible>true</IsAotCompatible>
    <EnableAotAnalyzer>true</EnableAotAnalyzer>
    <EnableTrimAnalyzer>true</EnableTrimAnalyzer>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\MMP.Herald.Serilog\MMP.Herald.Serilog.csproj" />
    <!-- FLAG: confirm the P1/P2 Layer-1 project path/name at impl time. -->
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Configuration.Abstractions" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Add the Apache-2.0 LICENSE** to the project folder so the standalone package carries its own license, and adopt the per-file header convention. Every `.cs` file in this project (product + tests) starts with the Apache-2.0 short header:

```csharp
// Copyright (c) 2026 MMPWorks LLC
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.
```

- [ ] **Step 3: Build the empty shell** to confirm the reference graph resolves.

```bash
cd E:/dev/Herald.OSS && dotnet build compat/Herald.OSS.Serilog.Settings/Herald.OSS.Serilog.Settings.csproj -c Debug 2>&1 | tail -5
```
Expected: clean (no types yet, just the reference to Layer 1). If the Layer-1 project name/path differs, FLAG and fix the ProjectReference.

- [ ] **Step 4: Create the test project** `compat/Herald.OSS.Serilog.Settings.Tests/Herald.OSS.Serilog.Settings.Tests.csproj` (xUnit, net9/net10, references the product project + Layer-1 shim; `IsPackable=false`). Apache-2.0 header on every test file too.

- [ ] **Step 5: Commit.**

```bash
git add compat/Herald.OSS.Serilog.Settings compat/Herald.OSS.Serilog.Settings.Tests
git commit -m "feat(serilog-settings): scaffold Apache-2.0 appsettings binding project + test project"
```

---

### Task 2: The sink/enricher registry (S-NEW-1) — write it failing first

The public registration surface, pre-seeded with Herald's built-ins, consulted before the parser fails. This is the dangerous miss; build it before the parser so the parser has somewhere to resolve names.

**Files:**
- Test: `compat/Herald.OSS.Serilog.Settings.Tests/Registry/LoggerSinkRegistryTests.cs` (create)
- Create: `compat/Herald.OSS.Serilog.Settings/LoggerSinkRegistry.cs`
- Create: `compat/Herald.OSS.Serilog.Settings/LoggerEnricherRegistry.cs`
- Create: `compat/Herald.OSS.Serilog.Settings/SinkResolutionException.cs` (FLAG: reuse a P1 named exception if one fits)

- [ ] **Step 1: Write the failing tests.** The registry: (a) resolves a pre-seeded built-in name (`"Console"`, `"File"`); (b) resolves a user-registered custom name; (c) returns "unresolved" for an unknown name (the parser turns that into the loud throw, tested in Task 4); (d) registration is case-insensitive on the Serilog name; (e) a custom registration does not silently overwrite a built-in of the same name without intent (decide policy — last-write-wins vs throw-on-collision; pick throw, it's safer, and pin it).

```csharp
using Xunit;
using Herald.OSS.Serilog.Settings;

namespace Herald.OSS.Serilog.Settings.Tests.Registry;

public sealed class LoggerSinkRegistryTests
{
    [Theory]
    [InlineData("Console")]
    [InlineData("File")]
    public void BuiltIn_sink_names_are_preseeded(string name)
        => Assert.True(LoggerSinkRegistry.Default.IsRegistered(name));

    [Fact]
    public void Custom_sink_name_resolves_after_registration()
    {
        var reg = LoggerSinkRegistry.CreateDefault();
        reg.RegisterSink("MyCompanySink", (builder, args) => builder); // factory: (LoggerConfiguration, args) -> LoggerConfiguration
        Assert.True(reg.IsRegistered("MyCompanySink"));
    }

    [Fact]
    public void Name_resolution_is_case_insensitive()
        => Assert.True(LoggerSinkRegistry.Default.IsRegistered("console"));

    [Fact]
    public void Unknown_name_is_not_registered()
        => Assert.False(LoggerSinkRegistry.Default.IsRegistered("Serilog.Sinks.Seq"));

    [Fact]
    public void Registering_over_a_builtin_throws_not_silently_shadows()
    {
        var reg = LoggerSinkRegistry.CreateDefault();
        Assert.Throws<System.InvalidOperationException>(
            () => reg.RegisterSink("Console", (b, a) => b));
    }
}
```

- [ ] **Step 2: Run — expect FAIL** (type not defined).

```bash
dotnet test compat/Herald.OSS.Serilog.Settings.Tests/Herald.OSS.Serilog.Settings.Tests.csproj --filter "FullyQualifiedName~LoggerSinkRegistry" -v minimal
```

- [ ] **Step 3: Implement `LoggerSinkRegistry` + `LoggerEnricherRegistry`.** Public. `RegisterSink(string name, Func<LoggerConfiguration, IConfiguration, LoggerConfiguration> factory)` (the factory maps a `WriteTo` entry's `Args` onto the Layer-1 builder — confirm the exact factory signature against the P2 `LoggerConfiguration.WriteTo` shape; FLAG if it differs). A `CreateDefault()` factory pre-seeds the Herald built-ins (Console/File/HttpJson/TcpJsonLine/UdpJsonLine/Elasticsearch/OtlpJson/OtlpProtobuf/Null) by wiring each to its native `WithXSink` verb. A static `Default` for the no-customization path. Case-insensitive dictionary (`StringComparer.OrdinalIgnoreCase`). `RegisterSink` throws on collision. Mirror the same shape for enrichers. **DRY:** the two registries share a private generic `NamedFactoryRegistry<TFactory>` so the dictionary/collision/case-insensitive logic exists once.

- [ ] **Step 4: Run — expect PASS.**

- [ ] **Step 5: Commit.**

```bash
git add compat/Herald.OSS.Serilog.Settings/LoggerSinkRegistry.cs compat/Herald.OSS.Serilog.Settings/LoggerEnricherRegistry.cs compat/Herald.OSS.Serilog.Settings/SinkResolutionException.cs compat/Herald.OSS.Serilog.Settings.Tests/Registry/LoggerSinkRegistryTests.cs
git commit -m "feat(serilog-settings): public sink/enricher registry pre-seeded with Herald built-ins (S-NEW-1)"
```

---

### Task 3: The parser — `MinimumLevel` (incl. `Override`), `WriteTo`, `Enrich`, `Using`

The schema-reading core. Reads the `Serilog` section of an `IConfiguration` and applies it to the Layer-1 `LoggerConfiguration`. Resolves `WriteTo`/`Enrich` names through the Task-2 registries.

**Files:**
- Create: `compat/Herald.OSS.Serilog.Settings/Parsing/SerilogConfigurationSection.cs` (the typed read-model of the `Serilog:` config subtree)
- Create: `compat/Herald.OSS.Serilog.Settings/Parsing/SerilogConfigurationReader.cs` (applies the section to a `LoggerConfiguration`)
- Test: `compat/Herald.OSS.Serilog.Settings.Tests/Parsing/MinimumLevelParsingTests.cs`
- Test: `compat/Herald.OSS.Serilog.Settings.Tests/Parsing/WriteToEnrichParsingTests.cs`

- [ ] **Step 1: Write failing parse tests** (use `ConfigurationBuilder().AddInMemoryCollection(...)` to build `IConfiguration` fixtures — no file I/O in unit tests):
  - `MinimumLevel` as a bare string (`"Information"`) → builder minimum set; case-insensitive Serilog name mapped onto the post-P0 key.
  - `MinimumLevel` as an object with `Default` + `Override` (`{"Default":"Information","Override":{"Microsoft":"Warning","System":"Error"}}`) → default minimum + per-source-context overrides applied. FLAG: confirm the P2 `LoggerConfiguration` exposes a per-namespace override entry point; if it does not, that is a **cross-plan gap** — record it and pin a test asserting the override is *not silently dropped* (loud or recorded), per the level-gating / loud-fail discipline.
  - `WriteTo` array of `{ "Name": "Console" }` and `{ "Name": "File", "Args": { "path": "log.txt" } }` → the matching native sink verbs invoked with the args.
  - `Enrich` array of names → matching enricher verbs.
  - `Using` array (assembly hints) → recorded; for the reimplemented parser, `Using` does **not** trigger a real assembly scan (we don't load arbitrary assemblies); it is advisory. Names still resolve through the registry. A `Using` of a real Serilog sink assembly does not make that sink work — it resolves to the loud-fail in Task 4.

- [ ] **Step 2: Run — expect FAIL** (reader not defined).

- [ ] **Step 3: Implement the read-model + reader.** `SerilogConfigurationSection` binds the `Serilog:` subtree (use `IConfiguration` indexers / `GetSection`, not reflection-based `Bind` where it would pull a trim/AOT warning — keep it AOT-clean, G-GAP.7). `SerilogConfigurationReader.Apply(LoggerConfiguration, SerilogConfigurationSection, LoggerSinkRegistry, LoggerEnricherRegistry)`:
  - map `MinimumLevel` (default + overrides) onto the builder;
  - for each `WriteTo`, look the `Name` up in the sink registry → invoke its factory with the entry's `Args`; **unresolved name → throw the loud-named error (Task 4)**;
  - for each `Enrich`, look up in the enricher registry → same loud-fail on miss;
  - keep cognitive complexity low: one small method per schema section, a `switch`/dictionary dispatch on section kind, guard clauses over nesting.

- [ ] **Step 4: Run — expect PASS.**

- [ ] **Step 5: Commit.**

```bash
git add compat/Herald.OSS.Serilog.Settings/Parsing compat/Herald.OSS.Serilog.Settings.Tests/Parsing
git commit -m "feat(serilog-settings): parse MinimumLevel/Override/WriteTo/Enrich/Using onto the shim builder"
```

---

### Task 4: Loud-named failure for unresolved / third-party sink names (G-SINK-WALL.1)

The identity wall, surfaced as a named, audited throw — never a silent drop. The error text matches the parity-audit verbatim reason.

**Files:**
- Modify: `compat/Herald.OSS.Serilog.Settings/SinkResolutionException.cs`
- Modify: `compat/Herald.OSS.Serilog.Settings/Parsing/SerilogConfigurationReader.cs` (throw site)
- Test: `compat/Herald.OSS.Serilog.Settings.Tests/SinkWall/ThirdPartySinkFailsLoudTests.cs` (the G-SINK-WALL.1 SUITE)

- [ ] **Step 1: Write the failing suite.** A `WriteTo` naming `Serilog.Sinks.Seq` (and `Serilog.Sinks.MSSqlServer`, `Serilog.Sinks.Datadog` — gap-class → suite) throws `SinkResolutionException`; the message **contains the sink name** AND the identity-wall reason (assert on a stable substring drawn from the parity-audit text — e.g. `"strong-name"` / `"PublicKeyToken"` / `"cannot bind"`). Assert it is **not** a silent no-op: the same config without the loud-fail would have produced a logger with zero sinks — pin that the throw happens (the negative the happy path never exercises).

```csharp
using System;
using Microsoft.Extensions.Configuration;
using Xunit;
using Herald.OSS.Serilog.Settings;
using MMP.Herald.Serilog; // LoggerConfiguration

namespace Herald.OSS.Serilog.Settings.Tests.SinkWall;

public sealed class ThirdPartySinkFailsLoudTests
{
    [Theory]
    [InlineData("Serilog.Sinks.Seq")]
    [InlineData("Serilog.Sinks.MSSqlServer")]
    [InlineData("Serilog.Sinks.Datadog")]
    public void ThirdParty_sink_name_fails_loud_and_named(string sinkName)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new System.Collections.Generic.Dictionary<string, string?>
            {
                ["Serilog:WriteTo:0:Name"] = sinkName,
            })
            .Build();

        var ex = Assert.Throws<SinkResolutionException>(
            () => new LoggerConfiguration().ReadFrom.Configuration(config));

        Assert.Contains(sinkName, ex.Message);
        Assert.Contains("strong-name", ex.Message, StringComparison.OrdinalIgnoreCase); // identity-wall reason
    }
}
```

- [ ] **Step 2: Run — expect FAIL** (no `ReadFrom.Configuration` yet — Task 5; or the throw not wired). Note this test also depends on Task 5's extension entry point; if Task 5 is not yet done, target the reader's `Apply` directly here and re-point at `ReadFrom.Configuration` after Task 5.

- [ ] **Step 3: Implement `SinkResolutionException`** carrying the sink name; the throw site builds the message from a single shared constant holding the identity-wall reason (DRY — the same constant the parity audit references; do not retype the paragraph at each call site). Distinguish **third-party Serilog sink** (the `Serilog.Sinks.*` prefix → the full identity-wall reason) from a **plain unknown name** (a typo'd in-house name → "not registered; register it via LoggerSinkRegistry.RegisterSink"). Both throw; the message differs so the user gets the right next step.

- [ ] **Step 4: Run — expect PASS.**

- [ ] **Step 5: Commit.**

```bash
git add compat/Herald.OSS.Serilog.Settings/SinkResolutionException.cs compat/Herald.OSS.Serilog.Settings/Parsing/SerilogConfigurationReader.cs compat/Herald.OSS.Serilog.Settings.Tests/SinkWall/ThirdPartySinkFailsLoudTests.cs
git commit -m "feat(serilog-settings): unresolved/third-party sink names fail loud + named (G-SINK-WALL.1)"
```

---

### Task 5: `ReadFrom.Configuration(IConfiguration)` extension entry point

The public verb a Serilog user actually calls. An extension on the Layer-1 `LoggerConfiguration` that wires the reader + the default registries.

**Files:**
- Create: `compat/Herald.OSS.Serilog.Settings/ConfigurationLoggerConfigurationExtensions.cs`
- Test: `compat/Herald.OSS.Serilog.Settings.Tests/ReadFromConfigurationTests.cs`

- [ ] **Step 1: Write the failing test** asserting `new LoggerConfiguration().ReadFrom.Configuration(config).CreateLogger()` produces a working logger for a basic Console+MinimumLevel config. FLAG: confirm whether P2 exposes a `ReadFrom` *property* (Serilog's real shape is `loggerConfiguration.ReadFrom.Configuration(...)`) or whether the extension hangs directly off `LoggerConfiguration`. Serilog's `ReadFrom` is a sub-object; match that shape so the corpus snippet compiles unchanged. If P2 does not ship a `ReadFrom` sub-object, that is a **cross-plan dependency** — coordinate with the P1/P2 owner to add the empty `ReadFrom` accessor on the shim (it belongs to the call surface, not the settings project) OR ship a `ReadFrom` extension-target type here and FLAG the seam. Record the decision in the plan's self-review.

- [ ] **Step 2: Run — expect FAIL.**

- [ ] **Step 3: Implement the extension.** `ReadFrom.Configuration(this <ReadFrom-target> source, IConfiguration configuration, LoggerSinkRegistry? sinks = null, LoggerEnricherRegistry? enrichers = null)`. Defaults to `LoggerSinkRegistry.Default` / `LoggerEnricherRegistry.Default`. Reads the `Serilog:` section, builds the `SerilogConfigurationSection`, calls `SerilogConfigurationReader.Apply(...)`, returns the `LoggerConfiguration` for chaining. The custom-registry overload is how a shop wires its in-house sink (S-NEW-1's one-call escape from the fork).

- [ ] **Step 4: Run — expect PASS.**

- [ ] **Step 5: Commit.**

```bash
git add compat/Herald.OSS.Serilog.Settings/ConfigurationLoggerConfigurationExtensions.cs compat/Herald.OSS.Serilog.Settings.Tests/ReadFromConfigurationTests.cs
git commit -m "feat(serilog-settings): ReadFrom.Configuration(IConfiguration) extension on the shim builder"
```

---

### Task 6: G-CORPUS.2 — appsettings round-trip equals the code-config equivalent (SUITE)

The headline parity proof: the same logger configured two ways produces the same engine config.

**Files:**
- Test: `compat/Herald.OSS.Serilog.Settings.Tests/Corpus/AppSettingsRoundTripTests.cs` (the G-CORPUS.2 SUITE)
- Reference fixtures: small `appsettings.json` strings built in-memory (Console+File+MinimumLevel+Override+Enrich).

- [ ] **Step 1: Write the round-trip suite.** For each fixture: configure logger A via `ReadFrom.Configuration(config)` and logger B via the equivalent **code config** on the same Layer-1 `LoggerConfiguration` (`.MinimumLevel.Information().WriteTo.Console().WriteTo.File("log.txt").Enrich.With(...)`). Assert the two produce a **canonical-shape-equivalent** engine config (per the ingress↔output canonical-equivalence rule). The cleanest comparison surface: both `LoggerConfiguration`s translate onto `QuickLogBuilder` → JSON (the JSON-as-source-of-truth path, per design-round-richard §CUPID/DRY). **Compare the produced builder JSON, canonical-shape-normalized** (sort keys / ignore incidental ordering), not byte-identical. Cover: minimum level, sink set, sink args (file path), enricher set, `Override` map.

- [ ] **Step 2:** Add the **Seq-fails-loud row inside this suite** (G-CORPUS.2 ties G-SINK-WALL.1): a fixture with a Seq `WriteTo` throws inside the round-trip path too (proves the loud-fail is on the real `ReadFrom.Configuration` path, not only the reader-direct test).

- [ ] **Step 3:** Add the **custom-name-resolves row**: register `"MyCompanySink"` on a fresh registry, pass it to `ReadFrom.Configuration(config, customRegistry)`, assert the logger builds (the in-house sink resolves instead of throwing — the S-NEW-1 win, end-to-end).

- [ ] **Step 4: Run the suite — expect PASS.**

```bash
dotnet test compat/Herald.OSS.Serilog.Settings.Tests/Herald.OSS.Serilog.Settings.Tests.csproj --filter "FullyQualifiedName~Corpus.AppSettingsRoundTrip" -v minimal
```

- [ ] **Step 5: Commit.**

```bash
git add compat/Herald.OSS.Serilog.Settings.Tests/Corpus/AppSettingsRoundTripTests.cs
git commit -m "test(serilog-settings): appsettings round-trip == code-config; Seq loud; custom name resolves (G-CORPUS.2)"
```

---

### Task 7: AOT-clean + full build, wire into build.sh (coordinate with Max), close

**Files:**
- Modify (Max's lane, flagged): `Directory.Packages.props` (add `Microsoft.Extensions.Configuration.Abstractions` version), `build.sh` / solution enumeration.

- [ ] **Step 1: AOT/trim-clean check** — the project declares `IsAotCompatible`; confirm no new trim/AOT warnings (G-GAP.7). The reflection-free `IConfiguration` read path is what keeps this clean; if `Bind`-style reflection crept in, replace it with explicit indexer reads.

```bash
dotnet build compat/Herald.OSS.Serilog.Settings/Herald.OSS.Serilog.Settings.csproj -c Release 2>&1 | grep -iE "IL2|IL3|warning" | head -20 || echo "clean"
```
Expected: `clean` (or only the repo-baselined `WarningsNotAsErrors` set).

- [ ] **Step 2: Full test pass** for both TFMs.

```bash
dotnet test compat/Herald.OSS.Serilog.Settings.Tests/Herald.OSS.Serilog.Settings.Tests.csproj -v minimal 2>&1 | tail -10
```
Expected: green on net9 and net10.

- [ ] **Step 3: Hand Max the build wiring** — add the `PackageVersion` for `Microsoft.Extensions.Configuration.Abstractions` and enumerate the two new csprojs in `build.sh --all`. (Max's lane; this plan does not edit `build.sh`/central props directly — FLAG the dependency in the commit body.)

- [ ] **Step 4: Final commit + note P5 done.**

```bash
git add -A docs/serilog-compat compat/Herald.OSS.Serilog.Settings compat/Herald.OSS.Serilog.Settings.Tests
git commit -m "chore(serilog-compat): P5 settings (Apache-2.0) complete — appsettings binding + S-NEW-1 registry"
```

---

## Self-review notes

- **Spec coverage:** P5 implements scope-PRD §2 (`appsettings.json` configuration, separate Apache-2.0 project) + seam S-NEW-1 (config-name sink/enricher registry) + the binding VERDICT (REIMPLEMENT — build the parser, the real `Serilog.Settings.Configuration` cannot bind the unsigned shim). Tests: G-CORPUS.2 (round-trip == code-config SUITE) and G-SINK-WALL.1 (loud-named third-party fail SUITE), plus the S-NEW-1 custom-name-resolves proof. G-GAP.7 (AOT-clean) is honored on this project.
- **Loud-fail discipline:** every unresolved name throws a named exception (third-party Serilog sink → identity-wall reason; plain unknown → "register via LoggerSinkRegistry"); never a silent drop. Pinned negatively in Task 4 + Task 6 step 2.
- **CUPID/DRY:** the two registries share one private `NamedFactoryRegistry`; the identity-wall reason is a single shared constant referenced by the throw site and the parity audit; the parser is one small method per schema section.
- **Cross-plan types DEPENDED ON (from P1/P2):** `MMP.Herald.Serilog.LoggerConfiguration` (+ `MinimumLevel`/`WriteTo`/`Enrich` sub-objects, and the `ReadFrom` sub-object — see open decision 1), the native `WithXSink`/enricher verbs on `QuickLogBuilder`, the post-P0 level vocabulary. **Cross-plan types INTRODUCED by P5:** `LoggerSinkRegistry`, `LoggerEnricherRegistry`, `ConfigurationLoggerConfigurationExtensions`, `SinkResolutionException`. **Cross-plan build deps (Max):** `Microsoft.Extensions.Configuration.Abstractions` central pin + `build.sh` enumeration.
- **Open decisions (resolve at impl time):**
  1. **`ReadFrom` shape** — does P2 ship a `LoggerConfiguration.ReadFrom` sub-object accessor (so `loggerConfiguration.ReadFrom.Configuration(...)` compiles like real Serilog), or does P5 supply the `ReadFrom` extension target? It belongs to the call surface (P1/P2), not settings — confirm with the P1/P2 owner; if P5 must supply it, FLAG the seam. (Task 5.)
  2. **`MinimumLevel.Override` target** — does P2's `LoggerConfiguration` expose a per-source-context override entry point? If not, the override map is a **cross-plan gap**: it must be recorded + pinned loud (not silently dropped), per the level-gating/loud-fail discipline. (Task 3.)
  3. **`SinkResolutionException` ownership** — new P5 type vs. reuse of a P1 named exception. Prefer reuse if P1 ships a fitting named exception; otherwise own it here. (Task 2/4.)
  4. **Registration collision policy** — throw-on-collision (chosen, safer) vs last-write-wins. Pinned by a test in Task 2; confirm it matches whatever the seam-inventory worked example documents.
- **Task count:** 8 tasks (Task 0 pre-mortem gate → Task 7 close).

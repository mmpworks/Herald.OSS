# P0 — Serilog-Compat Rename Wave Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rename Herald.OSS's overlapping log levels and typed-logger verbs to Serilog's vocabulary (`Info→Information`, `Warn→Warning`, `Critical→Fatal`, `Trace→Verbose`), in a cross-repo lockstep that never breaks the SSE↔SPA wire mid-flight, and fix the pre-existing two-table drift while we're in there.

**Architecture:** A *transitional* bidirectional alias map lets old and new level keys coexist during the sweep; the three level tables (`KnownLogLevels`, `KnownLogLevelKeys`, `LogLevelKeys`) are renamed and reconciled; the ~19 OSS files + Dashboard SPA + DemoApp seeds + wire emitters are swept to new keys; a regression suite pins the hazards; the alias map is removed last so no permanent two-name surface remains. Herald keeps its four extra levels (`Notice`/`Success`/`Security`/`Metric`).

**Tech Stack:** C# / .NET (net9 + net10 target for compat; Herald.OSS core multi-targets), xUnit (`tests/Herald.OSS.Tests.csproj`), `bash build.sh`, source-generator analyzer tables (netstandard2.0).

**Rename mapping (authoritative — from Richard's ADR):**

| Old key | Old display | New key | New display |
|---|---|---|---|
| `info` | Info | `information` | Information |
| `warn` | Warn | `warning` | Warning |
| `critical` | Critical | `fatal` | Fatal |
| `trace` | Trace | `verbose` | Verbose |
| `debug` | Debug | `debug` | Debug (already aligned) |
| `error` | Error | `error` | Error (already aligned) |

Kept untouched (no Serilog equivalent): `notice`, `success`, `security`, `metric`.

---

### Task 0: the-fool pre-mortem gate (no code)

**Files:**
- Create: `docs/serilog-compat/plans/P0-rename-premortem.md`

- [ ] **Step 1: Run the pre-mortem.** Invoke `Skill(the-fool)` framed as: *"This rename is applied mechanically across ~40 files in three repos (Herald.OSS, Dashboard, DemoApp). It is half-applied at the moment of failure. Where does a partial application leave a silently-broken system?"* Capture the failure modes (e.g., a file swept to `information` while a sibling still emits `info`; an analyzer table updated but the runtime table not; a Dashboard key flipped before the server emits the new key).

- [ ] **Step 2: Write the risk list** to `P0-rename-premortem.md` — each risk + which Task below mitigates it. This gates the sweep: any risk without a mitigating task means a task is missing from this plan.

- [ ] **Step 3: Commit**

```bash
git add docs/serilog-compat/plans/P0-rename-premortem.md
git commit -m "docs(serilog-compat): the-fool pre-mortem on the rename sweep"
```

---

### Task 1: Cross-table equivalence test (G-LEVEL.6) — write it failing first

This test references **both** the analyzer table and the runtime table in one suite (today they can't see each other — the root cause of the existing `Fatal` drift). It fails now for two reasons (old keys + the stray `Fatal`); Tasks 2–3 green it.

**Files:**
- Read first: `src/Levels/KnownLogLevels.cs`, `src/Levels/KnownLogLevelKeys.cs`, `src/Services/LogLevelKeys.cs` (learn the current member names + the `Fatal` discrepancy).
- Test: `tests/Levels/LevelTableEquivalenceTests.cs` (create)

- [ ] **Step 1: Write the failing test.** The intended post-rename invariant: the *value set* of the two key tables is identical, namely the six Serilog keys + Herald's four extras.

```csharp
using System.Collections.Generic;
using System.Linq;
using Xunit;
using MMP.Herald.Levels; // KnownLogLevelKeys
// using the runtime table's namespace — confirm from LogLevelKeys.cs (likely MMP.Herald.Services)

namespace Herald.OSS.Tests.Levels;

public sealed class LevelTableEquivalenceTests
{
    // The single source of truth for the intended post-rename key set.
    private static readonly HashSet<string> ExpectedKeys = new()
    {
        "verbose", "debug", "information", "warning", "error", "fatal", // Serilog six
        "notice", "success", "security", "metric"                       // Herald extras
    };

    [Fact]
    public void AnalyzerTable_and_RuntimeTable_have_identical_value_sets()
    {
        var analyzerKeys  = KnownLogLevelKeys.AllKeys.ToHashSet();   // confirm the exposing member name
        var runtimeKeys   = LogLevelKeys.AllKeys.ToHashSet();        // confirm the exposing member name

        Assert.Equal(ExpectedKeys, analyzerKeys);
        Assert.Equal(ExpectedKeys, runtimeKeys);
        Assert.Equal(analyzerKeys, runtimeKeys); // the drift guard
    }
}
```

If the tables don't currently expose an `AllKeys` enumeration, add a minimal internal/`public` static `IReadOnlyList<string> AllKeys` to each in Tasks 2–3 (and note it here). Do NOT weaken `ExpectedKeys` to make the test pass — it encodes the target.

- [ ] **Step 2: Run it — expect FAIL** (old keys present, `Fatal` stray).

```bash
cd E:/dev/Herald.OSS && dotnet test tests/Herald.OSS.Tests.csproj --filter "FullyQualifiedName~LevelTableEquivalenceTests" -v minimal
```
Expected: FAIL (sets differ).

- [ ] **Step 3: Commit the failing test** (red is the contract).

```bash
git add tests/Levels/LevelTableEquivalenceTests.cs
git commit -m "test(levels): cross-table equivalence guard (red — pins post-rename key set)"
```

---

### Task 2: Transitional level-key alias map (new code)

Bidirectional alias so old persisted JSON and in-flight old-key events resolve during the sweep. **Scaffolding — removed in Task 9.**

**Files:**
- Create: `src/Levels/TransitionalLevelKeyAliases.cs`
- Read: `src/Levels/LogLevel.cs`, wherever level keys are resolved to `LogLevel` (the registry — find with `grep -rn "ResolveLevel\|FromKey\|ILogLevelRegistry" src`).
- Test: `tests/Levels/TransitionalLevelKeyAliasTests.cs`

- [ ] **Step 1: Write the failing test.**

```csharp
using Xunit;
using MMP.Herald.Levels;

namespace Herald.OSS.Tests.Levels;

public sealed class TransitionalLevelKeyAliasTests
{
    [Theory]
    [InlineData("info", "information")]
    [InlineData("warn", "warning")]
    [InlineData("critical", "fatal")]   // value rename to a previously-nonexistent key — the trap
    [InlineData("trace", "verbose")]
    [InlineData("information", "information")] // new keys pass through unchanged
    [InlineData("debug", "debug")]
    [InlineData("notice", "notice")]    // extras untouched
    public void Canonicalize_maps_old_keys_to_new(string input, string expected)
        => Assert.Equal(expected, TransitionalLevelKeyAliases.Canonicalize(input));

    [Fact]
    public void Canonicalize_is_case_insensitive()
        => Assert.Equal("information", TransitionalLevelKeyAliases.Canonicalize("INFO"));
}
```

- [ ] **Step 2: Run — expect FAIL** (type not defined).

```bash
dotnet test tests/Herald.OSS.Tests.csproj --filter "FullyQualifiedName~TransitionalLevelKeyAlias" -v minimal
```

- [ ] **Step 3: Implement the alias map.**

```csharp
// src/Levels/TransitionalLevelKeyAliases.cs
// TRANSITIONAL — scaffolding for the Serilog rename wave. REMOVED in Task 9.
// Do not build new behaviour on this; it exists only so old keys resolve mid-sweep.
using System;
using System.Collections.Generic;

namespace MMP.Herald.Levels;

internal static class TransitionalLevelKeyAliases
{
    private static readonly IReadOnlyDictionary<string, string> OldToNew =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["info"]     = "information",
            ["warn"]     = "warning",
            ["critical"] = "fatal",
            ["trace"]    = "verbose",
        };

    /// <summary>Maps a legacy level key to its post-rename canonical key; passes through anything already canonical.</summary>
    public static string Canonicalize(string key)
        => OldToNew.TryGetValue(key, out var canonical) ? canonical : key.ToLowerInvariant();
}
```

- [ ] **Step 4: Wire it into the registry's key-resolution entry point** so any inbound key (config load, wire ingest) is canonicalized before lookup. Read the registry, add a single `key = TransitionalLevelKeyAliases.Canonicalize(key);` at the top of the resolve method.

- [ ] **Step 5: Run — expect PASS.**

- [ ] **Step 6: Commit.**

```bash
git add src/Levels/TransitionalLevelKeyAliases.cs tests/Levels/TransitionalLevelKeyAliasTests.cs src/Levels/<registry-file>.cs
git commit -m "feat(levels): transitional old->new level-key alias map (scaffolding)"
```

---

### Task 3: Rename the three level tables + fix the Fatal drift

**Files:**
- Modify: `src/Levels/KnownLogLevels.cs`, `src/Levels/KnownLogLevelKeys.cs`, `src/Services/LogLevelKeys.cs`

- [ ] **Step 1: Rename in `KnownLogLevels.cs`** — `Info→Information` (key `information`, display `Information`), `Warn→Warning`, `Critical→Fatal` (key `fatal`), `Trace→Verbose` (key `verbose`). Keep `Debug`/`Error`/`Notice`/`Success`/`Security`/`Metric` as-is. Add the `AllKeys` enumeration if absent.

- [ ] **Step 2: Rename in `KnownLogLevelKeys.cs`** (analyzer table) to the same six + four extras. Add `AllKeys`.

- [ ] **Step 3: Rename in `LogLevelKeys.cs`** (runtime table) to the same set, **deleting the stray `Fatal="fatal"` duplication path** so the table has exactly the ten canonical keys. Add `AllKeys`.

- [ ] **Step 4: Run the two table tests — expect PASS now.**

```bash
dotnet test tests/Herald.OSS.Tests.csproj --filter "FullyQualifiedName~LevelTableEquivalenceTests|FullyQualifiedName~TransitionalLevelKeyAlias" -v minimal
```
Expected: PASS (sets equal; aliases resolve).

- [ ] **Step 5: Commit.**

```bash
git add src/Levels/KnownLogLevels.cs src/Levels/KnownLogLevelKeys.cs src/Services/LogLevelKeys.cs
git commit -m "refactor(levels)!: rename overlapping levels to Serilog names; fix two-table drift"
```

---

### Task 4: Rename typed-logger verbs + add Verbose/Fatal

**Files:**
- Modify: `src/ILogger{T}.cs`, `src/TypedLogger.cs`, `native/dotnet/Pipeline/StructuredLogger.cs` (verb methods)
- Test: `tests/Levels/TypedVerbRenameTests.cs`

- [ ] **Step 1: Write the failing test** asserting the new verb surface exists and routes to the right level.

```csharp
using Xunit;
using MMP.Herald;          // ILogger<T>
using MMP.Herald.Levels;

namespace Herald.OSS.Tests.Levels;

public sealed class TypedVerbRenameTests
{
    [Fact]
    public void Logger_exposes_Serilog_named_verbs()
    {
        // Arrange a logger with a capturing sink (use the test harness's in-memory sink).
        var (logger, captured) = TestLoggers.CreateCapturing<TypedVerbRenameTests>();

        logger.Information("hello {X}", 1);
        logger.Warning("warn {X}", 2);
        logger.Verbose("verbose {X}", 3);
        logger.Fatal("fatal {X}", 4);

        Assert.Equal("information", captured[0].Level.Key);
        Assert.Equal("warning",     captured[1].Level.Key);
        Assert.Equal("verbose",     captured[2].Level.Key);
        Assert.Equal("fatal",       captured[3].Level.Key);
    }
}
```
(If `TestLoggers.CreateCapturing` doesn't exist, add it to the test harness as the first cross-cutting fixture — an in-memory `IKernelSink`/sink that records `LogEvent`s.)

- [ ] **Step 2: Run — expect FAIL** (verbs `Information`/`Verbose`/`Fatal` not defined).

- [ ] **Step 3: Rename `Info→Information`, `Warn→Warning` on the typed logger + `StructuredLogger`; add `Verbose` and `Fatal` verbs** (mapping to the `verbose`/`fatal` levels), each with the existing overload shapes (with/without `Exception`, the typed-args arities). Keep `Trace`/`Critical` removed (they became `Verbose`/`Fatal`).

- [ ] **Step 4: Run — expect PASS.**

- [ ] **Step 5: Commit.**

```bash
git add src/ILogger{T}.cs src/TypedLogger.cs native/dotnet/Pipeline/StructuredLogger.cs tests/Levels/TypedVerbRenameTests.cs
git commit -m "refactor(api)!: typed verbs Info/Warn->Information/Warning; add Verbose/Fatal"
```

---

### Task 5: Mechanical sweep of OSS files referencing old key literals

The alias map (Task 2) keeps old fixtures green during this sweep; the goal is to remove old-key *emission/definition* from product code.

**Files (exact list — re-derive before starting):**

```bash
cd E:/dev/Herald.OSS
grep -rlw --include="*.cs" -E '"info"|"warn"|"critical"|"trace"' src native > /tmp/p0-sweep-list.txt
cat /tmp/p0-sweep-list.txt   # ~19 files; review each — some "trace" hits are OpenTelemetry trace-id, NOT level. Do NOT blind-replace.
```

- [ ] **Step 1: Triage the list.** For each file, confirm whether the literal is a *level key* (sweep it) or an unrelated token (`trace` as trace-id, `info` in a comment). Mark each. This is the half-applied-rename risk from Task 0 — do it deliberately, file by file.

- [ ] **Step 2: Apply** the `info→information`, `warn→warning`, `critical→fatal`, `trace→verbose` substitution **only to confirmed level-key literals**, one file per commit-group of related files. Read each file; change the literal; keep surrounding code intact.

- [ ] **Step 3: Verify after each group.**

```bash
dotnet build Herald.OSS.csproj -c Debug 2>&1 | tail -5
dotnet test tests/Herald.OSS.Tests.csproj -v minimal 2>&1 | tail -10
```
Expected: build clean, tests green (alias map covers any lagging fixture).

- [ ] **Step 4: Commit per group.**

```bash
git add <the files in this group>
git commit -m "refactor(levels)!: sweep old level keys -> Serilog keys (<area>)"
```

---

### Task 6: Wire / SSE emitters to new keys

**Files:**
- Modify: the level emitters in the management/SSE path (find: `grep -rn "levelKey\|\"level\"\|LevelRank" src/Addons/ManagementApi native`).
- Test: `tests/Wire/SseLevelKeyTests.cs`

- [ ] **Step 1: Write the failing test** asserting the SSE/wire `level` field emits the new key.

```csharp
[Fact]
public void Sse_emits_new_level_keys()
{
    var evt = TestEvents.At("information"); // build a LogEvent at Information
    var json = SseLevelSerializer.Serialize(evt); // confirm the actual emitter entry point
    Assert.Contains("\"level\":\"information\"", json);
    Assert.DoesNotContain("\"level\":\"info\"", json);
}
```

- [ ] **Step 2: Run — expect FAIL** (still emits `info`).
- [ ] **Step 3: Update the emitter** to write canonical new keys.
- [ ] **Step 4: Run — expect PASS.**
- [ ] **Step 5: Commit.**

```bash
git add <emitter files> tests/Wire/SseLevelKeyTests.cs
git commit -m "refactor(wire)!: SSE/wire emitters emit Serilog level keys"
```

---

### Task 7: Dashboard SPA + DemoApp seeds (cross-repo — coordinate with Nancy)

**Files (other repos):**
- `E:/dev/herald/Modules/Dashboard/**` — level-lane keys, level-filter UI, parser.
- DemoApp seed (`Serilog's 6 levels` per canonical-seed memory — verify alignment; it may already be `information`/`warning`).

- [ ] **Step 1:** In Dashboard, make level-key **parsing alias-tolerant** first (accept both old and new) — this is the safe intermediate that survives the deploy window.
- [ ] **Step 2:** Flip SPA emission/storage to **new keys primary**.
- [ ] **Step 3:** Update DemoApp seed to new keys; confirm `critical→fatal`, `trace→verbose`.
- [ ] **Step 4: Verify end-to-end** against a running server (`bash build.sh --release --run --port 5210`) — the SSE stream emits new keys, the SPA renders every event with a severity (no `(no level)` markers), the level filter mutates correctly.
- [ ] **Step 5: Commit in each repo** (Dashboard + DemoApp), referencing this plan.

---

### Task 8: G-LEVEL regression suite

**Files:**
- Test: `tests/Levels/LevelRenameRegressionTests.cs`

- [ ] **Step 1: Write the regression tests** (all should pass given Tasks 2–7; they pin the hazards).

```csharp
public sealed class LevelRenameRegressionTests
{
    // G-LEVEL.1 — old persisted JSON round-trips through the alias map, every renamed key.
    [Theory]
    [InlineData("info", "information")]
    [InlineData("warn", "warning")]
    [InlineData("critical", "fatal")]
    [InlineData("trace", "verbose")]
    public void OldPersistedKey_resolves_to_new_level(string oldKey, string newKey)
    {
        var level = LevelRegistry.Resolve(oldKey);   // confirm resolve entry point
        Assert.Equal(newKey, level.Key);
        Assert.NotNull(level);                        // survives ingest, not rejected -> not vanished
    }

    // G-LEVEL.4 — the four extra levels survive and are absent from the Serilog map.
    [Theory]
    [InlineData("notice")]
    [InlineData("success")]
    [InlineData("security")]
    [InlineData("metric")]
    public void ExtraLevels_survive_rename(string key)
        => Assert.Equal(key, LevelRegistry.Resolve(key).Key);

    [Theory]
    [InlineData("notice")]
    [InlineData("metric")]
    public void ExtraLevels_have_no_Serilog_counterpart(string key)
        => Assert.False(SerilogLevelMap.TryToSerilog(key, out _)); // one-way Herald->string only
}
```

- [ ] **Step 2: Run — expect PASS.**
- [ ] **Step 3:** Add the **replay-buffer-spans-rename** (G-LEVEL.2) and **level-mutation round-trip** (G-LEVEL.3) tests using the replay-ring+SSE inter-step harness (cross-cutting fixture — build it here). These need the harness; if it's not yet built, build the minimal version now (seed ring with old-key events, connect fresh client, assert all resolve).
- [ ] **Step 4: Commit.**

```bash
git add tests/Levels/LevelRenameRegressionTests.cs tests/Infrastructure/ReplayRingHarness.cs
git commit -m "test(levels): rename regression suite (G-LEVEL.1-4) + replay-ring harness"
```

---

### Task 9: Remove the transitional alias map (lockstep step 6)

Only after Tasks 5–8 are green and the Dashboard/DemoApp are on new keys.

**Files:**
- Delete: `src/Levels/TransitionalLevelKeyAliases.cs`
- Modify: the registry resolve method (remove the `Canonicalize` call)
- Test: `tests/Levels/LevelRenameRegressionTests.cs` (add G-LEVEL.5)

- [ ] **Step 1: Write the failing test (G-LEVEL.5)** — post-removal, old keys are **rejected loud**, not silently aliased.

```csharp
[Theory]
[InlineData("info")]
[InlineData("critical")]
public void OldKeys_are_rejected_after_alias_removal(string oldKey)
    => Assert.Throws<UnknownLogLevelException>(() => LevelRegistry.Resolve(oldKey)); // confirm the reject type
```

- [ ] **Step 2: Run — expect FAIL** (alias still resolves them).
- [ ] **Step 3: Delete the alias map + its registry call.** Delete `TransitionalLevelKeyAliasTests.cs` (it tested scaffolding).
- [ ] **Step 4: Run — expect PASS** (old keys now rejected). Confirm Task 1 + Task 8 G-LEVEL.1 tests are updated/removed if they depended on aliasing (G-LEVEL.1's premise was the *transitional* window — convert it to assert old persisted JSON must be migrated, or move it behind a one-time migration shim if old on-disk configs must still load; decide with Richard).
- [ ] **Step 5: Commit.**

```bash
git add -A src/Levels tests/Levels
git commit -m "refactor(levels)!: remove transitional alias map; old keys now rejected loud"
```

---

### Task 10: Full build, test, AOT, and wave close

- [ ] **Step 1: Full build + test.** (Herald.OSS has no local `build.sh`/`.sln` — build the csproj directly; the umbrella `build.sh` at `E:/dev/herald` covers the whole tree if a cross-module check is wanted.)

```bash
cd E:/dev/Herald.OSS && dotnet build Herald.OSS.csproj -c Release 2>&1 | tail -10
cd E:/dev/Herald.OSS && dotnet test tests/Herald.OSS.Tests.csproj -c Release 2>&1 | tail -20
```
Expected: green.

- [ ] **Step 2: AOT-clean check.**

```bash
dotnet test tests/AOT/Herald.OSS.Aot.Tests.csproj -v minimal 2>&1 | tail -10
```
Expected: no new trim/AOT warnings.

- [ ] **Step 3: Grep for residue** — no product code emits old keys.

```bash
grep -rnw --include="*.cs" -E '"info"|"warn"|"critical"' src native | grep -iv "trace-id\|//" || echo "clean"
```
Expected: `clean` (or only justified non-level hits).

- [ ] **Step 4: Final commit + note P0 done.**

```bash
git add -A docs/serilog-compat
git commit -m "chore(serilog-compat): P0 rename wave complete — ready for P1 Layer-1 core"
```

---

## Self-review notes

- **Spec coverage:** P0 implements the rename ADR (Richard) + G-LEVEL.1-6 (Echo) + the cross-table drift fix. P1–P8 cover the rest (see roadmap).
- **Cross-repo:** Tasks 7 spans Dashboard + DemoApp — must land in the same wave as Tasks 5–6 (the lockstep). Do not merge P0 with old keys still emitted anywhere.
- **The `critical→fatal` trap** is pinned in Tasks 2, 8 explicitly.
- **Open decision for Task 9:** whether old on-disk persisted configs must still load after alias removal (one-time migration shim) or are treated as must-migrate. Resolve with Richard before executing Task 9.

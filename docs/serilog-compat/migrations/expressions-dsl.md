---
gap-id: expressions-dsl
serilog-surface: Serilog.Expressions string DSL (Filter.ByIncluding("..."), expression templates)
herald-status: hard-wall (predicate form maps; string DSL does not)
population-rank: medium
regression-test-id: G-GAP.2
---

<!-- Heather T-H2: STANDALONE companion. HARD WALL. The predicate form maps; the
     string-DSL form does not. Named as an open RFC to the OSS community
     (open-source-dilemma rule). -->

# Migrating Off Serilog.Expressions

> ⚠️ **STALE — corrected by Wave 1 (2026-06-01).** The "string-DSL is a hard wall / Herald does not
> implement that engine" claim below is **out of date**. The `Serilog.Expressions` string-DSL engine
> now ships — `Filter.ByExcluding(string)` / `Filter.ByIncludingOnly(string)` parse and evaluate a
> string expression into an `ILogFilter`. The *real* remaining gap (found migrating Ref4) is that
> there is **no `LoggerConfiguration.Filter` fluent step** to apply that filter inline on the config
> chain, so a migrated `.Filter.ByExcluding("...")` call site does not compile yet. The
> `ExpressionTemplate` renderer + the `appsettings.json` `Filter` block remain out of scope.
> Heather to re-author the body; `herald-status` should become "string-DSL engine ships; fluent
> `LoggerConfiguration.Filter` integration is the open gap." See `migrations/results/migration-results.json`
> (ref4-filtering) and `REF4-notes.md`.

## What maps and what does not

`Serilog.Expressions` is two different things bundled under one package name. The predicate form and the string-DSL form behave differently with Herald's compat layer.

**Predicate filtering — maps over.**

`Filter.ByExcluding(e => ...)` and `Filter.ByIncluding(e => ...)` with a C# predicate lambda map onto Herald's processor pipeline. The predicate form compiles and runs unchanged.

**String-DSL filtering — does not map.**

`Filter.ByIncluding("Level = 'Error' and SomeProperty > 5")` is a string expression evaluated by the `Serilog.Expressions` parse engine. Herald does not implement that engine. Attempting to configure a string-DSL filter through the compat layer fails loud and named (G-GAP.2) — it does not silently no-op.

This is a hard wall, not a deferral.

## What you have in Serilog

Two patterns to distinguish:

```csharp
// Predicate form — this maps over
.Filter.ByExcluding(e => e.Level == LogEventLevel.Verbose)

// String-DSL form — this does not map
.Filter.ByIncluding("Level = 'Error' and RequestPath = '/health'")
```

## If your filter is a predicate

No changes needed. The predicate form compiles and runs on Herald. Recompile against the Layer-1 assembly and verify the filter behavior matches.

```csharp
// Before (Serilog)
.Filter.ByExcluding(e => e.Level == LogEventLevel.Verbose)

// After (Herald Layer 1) — identical call, recompile
.Filter.ByExcluding(e => e.Level == LogEventLevel.Verbose)
```

## If your filter is genuinely string-DSL only

There is no drop-in path. The string-DSL is a separate parse engine (`Serilog.Expressions`) that Herald does not implement.

**Interim path:** Convert the string-DSL expression to an equivalent C# predicate. For most filters this is straightforward — a string-DSL `Level = 'Error'` becomes `e.Level == LogEventLevel.Error` in a lambda. Complex expression-template usage (rendering with expressions, filtering on structured property values) requires more work to rewrite as predicates.

**Long-term:** This gap is named as an open problem to the Herald OSS community. If you implement a `Serilog.Expressions`-compatible parse engine on Herald's processor extension seam, the seam is available — it does not need to be in Herald's core. See [parity-audit.md § Serilog.Expressions DSL — the second wall](../parity-audit.md) for the community RFC discussion and the extension point.

If you have a concrete implementation or a proposal, open an issue on Herald.OSS with the label `serilog-compat` and tag it `expressions-rfc`.

## Verify

A configuration that uses the unsupported string-DSL form fails at startup with a named error (G-GAP.2). The error message identifies:

- The DSL string that could not be parsed
- That the string-DSL form is not supported
- The predicate-form alternative

If you see this error, the DSL conversion above is your path forward. If the string DSL fires a runtime exception rather than a startup-time error, file a bug — the correct behavior is loud-fail at configuration time.

# Migration Runbook — Herald.OSS Serilog Drop-In Compatibility

- **Date:** 2026-05-30
- **Branch:** `feat/serilog-compat`
- **Status:** Task 8 deliverable — documentation only; P7 implementation artifacts are the dependency

---

## The honest claim

> *"Swap the package, rebuild, and your standard Serilog code runs on Herald — popular sinks map over; the third-party-sink ecosystem and the expression DSL are a documented boundary."*

Source compatibility on recompile. Not binary identity — Herald does not have Serilog's strong-name key and will not spoof it. That single fact draws the hard edge of what carries over and what doesn't.

---

## Is Herald a drop-in for you?

Four questions. Answer them before picking a migration path.

1. Do you use only the standard sinks Herald ships — Console, File, HTTP, TCP, UDP, Elasticsearch, OTLP, Null?
2. Is your code configured in C# (`LoggerConfiguration().WriteTo...`) or `appsettings.json`? No Serilog.Expressions string DSL?
3. Are your custom sinks and enrichers **source-compiled** in your own repo (not pre-compiled community NuGet packages like Seq or MSSql)?
4. Are you targeting net9 or net10?

If the answer is yes to all four, the fast path (Layer 2 swap) works straight through.

If any answer is no, find your gap in the [parity audit](parity-audit.md) before you start.

---

## Two-layer strategy

The compat layer ships in two layers with different coexistence properties. Understand the difference before picking a path.

### Layer 1 — `MMP.Herald.Serilog.*`

Package: `MMP.Herald.Serilog` (and `MMP.Herald.Serilog.AspNetCore` for the ASP.NET wiring).

Layer 1 puts Serilog-shaped types in a Herald namespace. Every `Serilog.ILogger` maps to `MMP.Herald.Serilog.ILogger`; every `Serilog.Log` maps to `MMP.Herald.Serilog.Log`. A consumer changes one `global using` per project (or per-file `using` swaps) and everything below it is identical code.

**Layer 1 can coexist with real Serilog in the same project graph.** Both assemblies are present; both resolve without conflict because the namespaces are distinct. This is the staging layer — add it beside real Serilog, verify parity at your own pace, then cut over.

### Layer 2 — `Serilog.*` shim (final cutover)

Packages: `MMP.Herald.Compat.Serilog` + `MMP.Herald.Compat.Serilog.AspNetCore`.

Layer 2 re-declares Serilog's own namespaces and type shapes, each one a thin forwarding wrapper onto its Layer-1 twin. The assembly is named `Serilog.dll`. A consumer swaps the package reference and changes nothing in their code — `using Serilog;` still resolves; `Log.Information(...)` still compiles.

**Layer 2 cannot coexist with real Serilog in the same project graph.** Both declare `Serilog.ILogger`, `Serilog.Log`, and every other `Serilog.*` type. The CLR sees duplicate type definitions — `CS0433` at compile, `InvalidCastException` at runtime. This is structural and intentional. Test G-LAYER2.1 verifies the compile error fires (see [Hard constraints](#hard-constraints) below).

Cut over to Layer 2 only after you have verified correctness on Layer 1. The cutover is a single step — remove all real-Serilog package references, add the Layer-2 packages, build.

---

## Step-by-step migration

### Step 1 — Stage on Layer 1

Add the Layer-1 package alongside your existing Serilog reference. Real Serilog and Layer 1 coexist without conflict.

```xml
<!-- Your .csproj — add alongside the existing Serilog references -->
<PackageReference Include="MMP.Herald.Serilog" Version="x.y.z" />
<PackageReference Include="MMP.Herald.Serilog.AspNetCore" Version="x.y.z" />  <!-- if you use the ASP.NET wiring -->
```

Add one `global using` alias so `Serilog` resolves to the Layer-1 namespace. Put it in your `GlobalUsings.cs` (or equivalent):

```csharp
// GlobalUsings.cs — one line swaps the entire surface
global using Serilog = MMP.Herald.Serilog;
global using Serilog.Events = MMP.Herald.Serilog.Events;
global using Serilog.Context = MMP.Herald.Serilog.Context;
// Add the namespaces you actually use; the alias only applies to names you import
```

If you prefer per-file swaps over a global alias, change each file's `using Serilog;` to `using Serilog = MMP.Herald.Serilog;`. Both approaches work; the global alias is less error-prone at scale.

Build. At this point both Serilog and Herald are in the graph. Your application runs on Herald's engine for the aliased namespace and on real Serilog for any remaining real references.

### Step 2 — Verify parity

Run your test suite. Compare log output against a known-good real-Serilog baseline. Check:

- Structured properties are captured with the correct names and value-model types (scalar, structure, sequence, dictionary).
- `{@Obj}` destructuring produces a `StructureValue`, not a flat string.
- `{$Obj}` stringification produces a `ScalarValue` string.
- Minimum level filtering matches — check both `Information` and the `Verbose`/`Fatal` ends.
- `LogContext.PushProperty(...)` properties appear in the correct events.
- ASP.NET request-log lines contain the expected fields.
- Custom enrichers add the expected properties.
- Custom destructuring policies strip or transform as expected.

If you have a custom destructuring policy that strips sensitive fields (passwords, PII), verify the field is absent from the full serialized event — not just absent from the property dictionary. G-SEC.1 in the test suite uses this shape; replicate it in your own verification.

Remove real Serilog from the build temporarily to confirm nothing in your code directly references real-Serilog types:

```bash
# Temporarily comment out real Serilog refs in your csproj, then build.
# If it compiles clean, you have no remaining direct real-Serilog dependencies.
# Restore the refs before proceeding to step 3.
```

### Step 3 — Cut over to Layer 2

Once verification passes, cut over in a single step.

Remove **all** real-Serilog package references from every project in the solution:

```xml
<!-- Remove these (and any community sinks compiled against real Serilog) -->
<PackageReference Include="Serilog" Version="..." />
<PackageReference Include="Serilog.Extensions.Logging" Version="..." />
<PackageReference Include="Serilog.AspNetCore" Version="..." />
<!-- Also remove community sinks: Serilog.Sinks.Seq, Serilog.Sinks.MSSqlServer, etc. -->
```

Add the Layer-2 packages:

```xml
<PackageReference Include="MMP.Herald.Compat.Serilog" Version="x.y.z" />
<PackageReference Include="MMP.Herald.Compat.Serilog.AspNetCore" Version="x.y.z" />  <!-- if using ASP.NET wiring -->
```

Remove the `global using` alias (or per-file aliases) from Step 1. Layer 2 re-declares the real `Serilog.*` namespaces, so your original `using Serilog;` resolves correctly without aliasing.

Build. The assembly named `Serilog.dll` in your output directory is now Herald's Layer-2 shim. Your code is unchanged.

### Step 4 — Verify again after cutover

Run the test suite a second time on the Layer-2 build. Confirm:

- No `CS0433` duplicate-type error. If one fires, a real-Serilog reference is still present — check transitive dependencies of community sink packages (they pull in real Serilog).
- All the same parity checks from Step 2 pass.
- The application starts and processes log events correctly.

If a community sink package is still present as a transitive dependency, it will try to load the real `Serilog.dll` against the shim and fail. The options are:
- Remove the community sink and replace it with the Herald equivalent (Console, HTTP, OTLP, Elasticsearch — see the [parity audit](parity-audit.md) for the mapping).
- Wrap the sink behind a Herald `ILogEventSink` adapter compiled in your own codebase (this absorbs source-compiled adapters; it does not resolve the assembly-identity problem for pre-compiled packages).
- Keep that specific sink on a separate logging path that does not share the `Serilog` assembly with Herald.

---

## Hard constraints

These are structural facts, not configuration choices. Read them before you start.

### Layer 2 and real Serilog cannot coexist (CS0433)

Layer 2 re-declares every `Serilog.*` type. If both Layer 2 and real `Serilog.dll` are in the same project graph — directly or transitively — the compiler sees duplicate types and emits `CS0433`. This is guaranteed by test G-LAYER2.1: a test project referencing both is expected to fail to build, and the test verifies that the error is `CS0433`, not a runtime exception that slipped through a successful compile.

The mitigation is the runbook above: stage on Layer 1 (coexistence safe), verify, then cut over to Layer 2 and remove all real-Serilog references in one step.

### Pre-compiled community sinks will not bind to Layer 2 (identity wall)

`Serilog.Sinks.Seq`, `Serilog.Sinks.MSSqlServer`, `Serilog.Sinks.Datadog`, and the rest of the community sink long tail are each compiled against `Serilog, PublicKeyToken=24c2f752a8e58a10`. Herald.OSS is unsigned. An unsigned `Serilog.dll` is a different assembly identity. The CLR will not satisfy a reference to `Serilog, PublicKeyToken=24c2f752a8e58a10` with an unsigned assembly regardless of assembly name.

Referencing a pre-compiled community sink pulls in real `Serilog.dll` as a transitive dependency, which then collides with the Layer-2 shim and produces the CS0433 error above.

Herald does not have Serilog's signing key and will not spoof it. This is a structural boundary, not a deferred feature. The [parity audit](parity-audit.md) maps the popular sinks to their Herald equivalents. The third-party-sink gap is a named boundary with no drop-in path absent a different key.

### `IWebHostBuilder.UseSerilog` is not yet implemented (use `IHostBuilder.UseSerilog`)

The `IWebHostBuilder.UseSerilog(...)` overload is present as a stub and throws `NotSupportedException`. The generic host model (`IHostBuilder.UseSerilog(...)`) is fully implemented.

If your code uses `WebHost.CreateDefaultBuilder().UseSerilog(...)`, change it to `Host.CreateDefaultBuilder().UseSerilog(...)`. ASP.NET Core 3.1+ applications use the generic host by default; `WebHost.CreateDefaultBuilder` is the older pre-3.1 pattern.

**Workaround:** change one call site.

### `LogContext` (ambient scope) is not yet implemented

`Serilog.Context.LogContext` is present as a stub. `PushProperty(...)` and related methods throw `NotSupportedException`.

**Interim path:** use `BeginScope(...)` on the MEL `ILogger<T>` interface, which Herald's `HeraldLoggerProvider` implements. This covers the same use case — attach ambient properties to a scope — without the `LogContext` static API.

`LogContext` is planned for a post-P7 release.

---

## Community sink gaps — Herald equivalents

| Community sink | Herald equivalent | Configuration change |
|---|---|---|
| `Serilog.Sinks.Console` | Built-in Console sink | `WriteTo.Console()` — same verb |
| `Serilog.Sinks.File` | Built-in File sink | `WriteTo.File(path)` — same verb |
| `Serilog.Sinks.Http` | Built-in HTTP sink | `WriteTo.Http(url)` — same verb |
| `Serilog.Sinks.Elasticsearch` | Built-in Elasticsearch sink | `WriteTo.Elasticsearch(url)` |
| `Serilog.Sinks.OpenTelemetry` | Built-in OTLP sink | `WriteTo.OpenTelemetry(endpoint)` |
| `Serilog.Sinks.Seq` | No Herald equivalent — hard wall | Alternatives: OTLP-compatible backend, Elasticsearch; see [parity audit](parity-audit.md) |
| `Serilog.Sinks.MSSqlServer` | No Herald equivalent — hard wall | Alternatives: Herald HTTP sink → SQL ingestion layer; see parity audit |
| `Serilog.Sinks.Datadog` | No Herald equivalent — hard wall | Alternatives: OTLP export to Datadog; see parity audit |

For any sink that has no Herald equivalent: keep that output on a separate logging path, or route Herald events through an HTTP sink to a compatible backend.

---

## Structural-match gaps (inline)

These four surfaces are structural aliases — a renamed call, a wrapper, or the same grammar under a different renderer. Each is a one-to-two-line change, so it lives here instead of in its own companion file.

### Output-template grammar

Serilog's output-template specifiers (`{Level:u3}`, `{Message:lj}`, `{Timestamp:HH:mm:ss}`, `{NewLine}`, `{Exception}`) carry over. Herald renders them through `SerilogOutputTemplateRenderer`, which reads the same grammar.

```csharp
// Before and after — identical template string, recompile against the shim
.WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
```

No code change. Verify the rendered line matches your real-Serilog baseline (Step 2 covers this).

### Custom `ITextFormatter` / CLEF

A custom `ITextFormatter` (including the CLEF `CompactJsonFormatter`) carries over through Herald's `ITextFormatter` seam. Update the `using` directives in your formatter file to the Herald namespace; the `Format(LogEvent, TextWriter)` method body is unchanged.

```csharp
// Update the using, recompile — the formatter logic does not change
using MMP.Herald.Serilog.Formatting;   // was: using Serilog.Formatting;
```

### Sub-loggers (`WriteTo.Logger(lc => ...)`)

The nested-logger form maps to a Herald nested pipeline. The call shape is the same:

```csharp
.WriteTo.Logger(lc => lc
    .Filter.ByIncluding(/* predicate */)
    .WriteTo.File("errors.log"))
```

Use the predicate (`Func<>`) filter form, not the `Serilog.Expressions` string DSL — the string DSL is a hard wall (see [parity audit](parity-audit.md)).

### `LoggingLevelSwitch`

`LoggingLevelSwitch` carries over as a wrapper over Herald's `LogLevelSwitch`. The call shape is unchanged:

```csharp
var levelSwitch = new LoggingLevelSwitch(LogEventLevel.Information);
// ... later, at runtime:
levelSwitch.MinimumLevel = LogEventLevel.Debug;   // same property, same effect
```

No code change beyond the recompile.

---

## Reporting a gap

If you encounter a Serilog surface that Herald's compat layer does not handle and this runbook does not cover, open an issue on the Herald.OSS repository with the label `serilog-compat`. Describe the surface, the expected behavior, and whether you are hitting a compile error, a runtime error, or a behavioral difference.

If you find the gap is structural (assembly identity or strong-name), the [parity audit](parity-audit.md) explains why and links to the community RFC discussion.

# P6 — ASP.NET Core Compat (`MMP.Herald.Serilog.AspNetCore`) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship a new Layer-1 assembly, `MMP.Herald.Serilog.AspNetCore`, that lets standard Serilog ASP.NET Core wiring code recompile and run on Herald. Three entry points: `AddSerilog()` (MEL provider registration), `IHostBuilder/IHostApplicationBuilder.UseSerilog(...)` (host hook that builds the Herald logger and registers the provider), and the one **net-new** component `UseSerilogRequestLogging()` — a middleware that emits exactly one summary log line per HTTP request. All three are thin skins over the **already-shipping `HeraldLoggerProvider`** (`src/Addons/MelAdapter/HeraldLoggerProvider.cs`); we do not reimplement MEL.

**Architecture:** `AddSerilog`/`UseSerilog` are wiring façades — they construct or accept a Herald `StructuredLogger`, wrap it in `HeraldLoggerProvider` (the verified full `ILoggerProvider`/`Log<TState>`/`IsEnabled`/`BeginScope` bridge that reads `{OriginalFormat}`), and register that provider into MEL's `ILoggerFactory`. The lambda overload `UseSerilog((context, services, loggerConfiguration) => …)` hands the caller a **P2 `LoggerConfiguration` shim**; calling `.CreateLogger()` on it (P2's translator onto `QuickLogBuilder`) yields the Herald logger we wrap. `.ReadFrom.Configuration(...)` inside that lambda resolves to the **P5 settings extension**. `UseSerilogRequestLogging()` is genuine new behaviour: an `IMiddleware`-style component that times the request, reads the final status code, and writes one structured event through `IDiagnosticContext`-enriched properties at a configurable level + message template.

**Tech Stack:** C# / net9 + net10 (no hot-path TFM fork — confirmed Jared §"net9 vs net10"), the new assembly references `Microsoft.AspNetCore.Http.Abstractions` + `Microsoft.Extensions.Hosting.Abstractions` + `Microsoft.Extensions.Logging` and the Herald.OSS assembly (where `HeraldLoggerProvider` + `StructuredLogger` live). xUnit (`tests/Herald.OSS.Tests.csproj` or a sibling `tests/AspNetCore/` project — see Task 1), `Microsoft.AspNetCore.TestHost` for the middleware test, `bash build.sh`.

**Grounding facts (verified in the real codebase):**

- `HeraldLoggerProvider(StructuredLogger heraldLogger)` is the constructor; it exposes `CreateLogger(string)` and `CreateLogger<T>()` and is a `Microsoft.Extensions.Logging.ILoggerProvider`. (`HeraldLoggerProvider.cs:34-52`.)
- The MEL→Herald bridge already maps levels, reads `{OriginalFormat}`, fast-paths `IReadOnlyList<KeyValuePair<string,object?>>` state into `LogCompact`, and falls back to `Log(...)` with an `Exception` in context. **We add nothing to it.** (`HeraldLoggerProvider.cs:70-176`.)
- `QuickLogBuilder.Build()` returns `PipelineBuildResult` whose `.Logger` is the `StructuredLogger`. (`QuickLogBuilder.cs:387`.) The P2 `LoggerConfiguration.CreateLogger()` is the translator onto this.
- `HeraldLoggerProvider` is compiled **into the root `Herald.OSS` assembly** (all `src/Addons/*` fold into `Herald.OSS.csproj`; there is no per-addon csproj). The root assembly only references `Microsoft.Extensions.Logging.Abstractions` — so the new P6 assembly carries the ASP.NET + Hosting + full-Logging references, keeping Herald.OSS core's dependency surface unchanged.

**Cross-plan types this plan consumes (FLAG — owned elsewhere, must exist before P6 integrates):**

| Type / symbol | Owning plan | How P6 uses it |
|---|---|---|
| `Serilog.LoggerConfiguration` shim (with `.CreateLogger()` → Herald `StructuredLogger`, translator onto `QuickLogBuilder`) | **P2** | `UseSerilog` lambda overload hands it to the caller; calls `.CreateLogger()` to get the logger to wrap |
| `Serilog.Log` static facade (`Log.Logger` slot, `Log.CloseAndFlush()`) | **P1** | `UseSerilog()` parameterless overload reads `Log.Logger`; host shutdown flushes it |
| `ReadFrom.Configuration(IConfiguration)` extension on the shim `LoggerConfiguration` | **P5** | resolves inside the `UseSerilog((ctx,svc,cfg)=>…)` lambda body; P6 only verifies it binds, does not implement it |
| `HeraldLoggerProvider` | **already shipped** (MelAdapter) | the single bridge all three entry points register |
| `StructuredLogger` / `PipelineBuildResult` | **core** | the logger instance wrapped by the provider |

If any cross-plan symbol above is absent at integration time, Task 7 (the cross-plan smoke) goes RED — that is the intended signal, not a P6 bug.

---

### Task 0: the-fool pre-mortem gate (no product code)

**Files:**
- Create: `docs/serilog-compat/plans/P6-aspnetcore-premortem.md`

- [ ] **Step 1: Run the pre-mortem.** Invoke `Skill(the-fool)` framed as: *"A new `MMP.Herald.Serilog.AspNetCore` assembly provides `UseSerilog`/`AddSerilog`/`UseSerilogRequestLogging` over the existing `HeraldLoggerProvider`. Where does the request-logging middleware emit zero, two, or wrong-status lines? Where does double-provider-registration (both `AddSerilog` and a default MEL provider) duplicate every log line? Where does `UseSerilog` swallow the P2 `LoggerConfiguration` and silently log nothing? Where does an exception thrown mid-request make the middleware skip its one summary line?"*
- [ ] **Step 2: Write the risk list** to `P6-aspnetcore-premortem.md` — each risk + the Task below that mitigates it. Any risk without a mitigating task means this plan is missing one. Known seeds to capture: (a) middleware emits 0 lines when the pipeline short-circuits (static file, 304); (b) middleware emits 2 lines when registered twice; (c) status code read **before** `await _next` captures `200` not the real final code; (d) elapsed-ms measured with `DateTime.Now` (clock skew) vs a monotonic stopwatch; (e) `AddSerilog` not clearing default providers → every line logged twice by both MEL console and Herald.
- [ ] **Step 3: Commit.**

```bash
git add docs/serilog-compat/plans/P6-aspnetcore-premortem.md
git commit -m "docs(serilog-compat): the-fool pre-mortem on P6 ASP.NET Core wiring"
```

---

### Task 1: New assembly skeleton + test project wiring

**Files:**
- Create: `src/Serilog.AspNetCore/MMP.Herald.Serilog.AspNetCore.csproj`
- Create: `tests/AspNetCore/Herald.OSS.Serilog.AspNetCore.Tests.csproj`
- Read first: `Herald.OSS.csproj` (TFM is inherited from `Directory.Build.props` — match it; `RootNamespace`/`AssemblyName` pattern), `tests/Herald.OSS.Tests.csproj` (xUnit version pins, MEL package style).

- [ ] **Step 1: Author the product csproj.** `AssemblyName = MMP.Herald.Serilog.AspNetCore`, `RootNamespace = Serilog` (the consumer-facing surface lives in the `Serilog`/`Microsoft.Extensions.DependencyInjection`/`Microsoft.AspNetCore.Builder` namespaces so standard wiring code recompiles unchanged — Layer-1 lives in `MMP.Herald.Serilog.*` *assemblies* but exposes the `Serilog` *namespace* per Richard §"Assembly topology"). Reference: `Herald.OSS.csproj` (ProjectReference), `Microsoft.AspNetCore.Http.Abstractions`, `Microsoft.Extensions.Hosting.Abstractions`, `Microsoft.Extensions.Logging`, and the P1 `MMP.Herald.Serilog` + P2 shim project references. Multi-target net9+net10 to match the rest.
- [ ] **Step 2: Author the test csproj.** xUnit + `Microsoft.AspNetCore.TestHost` (for the middleware request test) + ProjectReference to the new assembly + the in-memory capturing-sink harness from `tests/Infrastructure/` (the `TestLoggers.CreateCapturing` fixture established in P0 Task 4 — reuse, do not rebuild).
- [ ] **Step 3: Empty build check** (no types yet — just confirm the project graph resolves).

```bash
cd /e/dev/Herald.OSS && dotnet build src/Serilog.AspNetCore/MMP.Herald.Serilog.AspNetCore.csproj -c Debug 2>&1 | tail -5
```

- [ ] **Step 4: Commit.**

```bash
git add src/Serilog.AspNetCore/ tests/AspNetCore/
git commit -m "build(serilog-compat): scaffold MMP.Herald.Serilog.AspNetCore + test project"
```

---

### Task 2: `AddSerilog()` MEL provider registration — write the test failing first

This is the thinnest skin: register `HeraldLoggerProvider` as an `ILoggerProvider` in the MEL `ILoggingBuilder`. CUPID/DRY: it constructs nothing the provider doesn't already do.

**Files:**
- Create: `src/Serilog.AspNetCore/SerilogLoggingBuilderExtensions.cs` (`Microsoft.Extensions.Logging` namespace — the `AddSerilog` extension on `ILoggingBuilder`)
- Test: `tests/AspNetCore/AddSerilogTests.cs`

- [ ] **Step 1: Write the failing test** — `AddSerilog` routes a MEL `ILogger<T>` through Herald.

```csharp
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Herald.OSS.Serilog.AspNetCore.Tests;

public sealed class AddSerilogTests
{
    [Fact]
    public void AddSerilog_routes_MEL_ILoggerOfT_through_Herald()
    {
        // Arrange — a Herald logger with an in-memory capturing sink (P0 harness).
        var (heraldLogger, captured) = TestLoggers.CreateCapturingStructured();

        var services = new ServiceCollection();
        services.AddLogging(b => b.ClearProviders().AddSerilog(heraldLogger));
        using var sp = services.BuildServiceProvider();

        // Act
        var melLogger = sp.GetRequiredService<ILogger<AddSerilogTests>>();
        melLogger.LogInformation("Player {Name} joined", "Ada");

        // Assert — the event went through Herald, not a MEL console.
        Assert.Single(captured);
        Assert.Equal("information", captured[0].Level.Key); // post-P0 Serilog key
        Assert.Equal("Ada", captured[0].Properties.Single(p => p.Name == "Name").Value);
    }

    [Fact]
    public void AddSerilog_registers_exactly_one_HeraldLoggerProvider()
    {
        var (heraldLogger, _) = TestLoggers.CreateCapturingStructured();
        var services = new ServiceCollection();
        services.AddLogging(b => b.ClearProviders().AddSerilog(heraldLogger));

        var providerRegs = services.Count(d => d.ServiceType == typeof(ILoggerProvider));
        Assert.Equal(1, providerRegs); // no double-registration (the-fool risk e)
    }
}
```

- [ ] **Step 2: Run — expect FAIL** (`AddSerilog` undefined).

```bash
dotnet test tests/AspNetCore/Herald.OSS.Serilog.AspNetCore.Tests.csproj --filter "FullyQualifiedName~AddSerilogTests" -v minimal
```

- [ ] **Step 3: Implement `AddSerilog`.** Two overloads: `AddSerilog(this ILoggingBuilder, StructuredLogger logger)` and parameterless `AddSerilog(this ILoggingBuilder)` (reads the **P1** `Serilog.Log.Logger` static slot — FLAG: P1 dependency). Both call `builder.AddProvider(new HeraldLoggerProvider(logger))` and register via `TryAddEnumerable`/`Services.AddSingleton<ILoggerProvider>` so the count stays one. No level mapping, no state walking — the provider owns that.
- [ ] **Step 4: Run — expect PASS.**
- [ ] **Step 5: Commit.**

```bash
git add src/Serilog.AspNetCore/SerilogLoggingBuilderExtensions.cs tests/AspNetCore/AddSerilogTests.cs
git commit -m "feat(serilog-compat): AddSerilog() registers HeraldLoggerProvider as ILoggerProvider"
```

---

### Task 3: `UseSerilog(...)` host hook — write the test failing first

The host hook builds (or accepts) the Herald logger, registers the provider, and supports the three Serilog overload shapes. CUPID: a single private `RegisterHerald(IServiceCollection, StructuredLogger, bool dispose)` does the work; the public overloads only resolve *which* logger.

**Files:**
- Create: `src/Serilog.AspNetCore/SerilogHostBuilderExtensions.cs` (`Microsoft.Extensions.Hosting` namespace — `UseSerilog` on `IHostBuilder` **and** `IHostApplicationBuilder`)
- Test: `tests/AspNetCore/UseSerilogTests.cs`

- [ ] **Step 1: Write the failing test** — `UseSerilog` wires the provider; the lambda overload receives the P2 shim.

```csharp
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Herald.OSS.Serilog.AspNetCore.Tests;

public sealed class UseSerilogTests
{
    [Fact]
    public void UseSerilog_with_prebuilt_logger_wires_the_provider()
    {
        var (heraldLogger, captured) = TestLoggers.CreateCapturingStructured();

        using var host = Host.CreateDefaultBuilder()
            .UseSerilog(heraldLogger)            // pre-built logger overload
            .Build();

        var melLogger = host.Services.GetRequiredService<ILogger<UseSerilogTests>>();
        melLogger.LogWarning("disk {Pct}% full", 91);

        Assert.Contains(captured, e => e.Level.Key == "warning");
    }

    [Fact]
    public void UseSerilog_lambda_receives_the_P2_LoggerConfiguration_shim()
    {
        var (heraldLogger, captured) = TestLoggers.CreateCapturingStructured();

        using var host = Host.CreateDefaultBuilder()
            .UseSerilog((context, services, loggerConfiguration) =>
            {
                // loggerConfiguration is the P2 shim; in this test we hand back a
                // pre-built logger via the harness seam rather than exercise P2/P5
                // construction (those are covered by G-CORPUS.1/.2). This asserts
                // the lambda is INVOKED and its result is what the host registers.
                TestLoggers.SeedConfiguration(loggerConfiguration, heraldLogger);
            })
            .Build();

        host.Services.GetRequiredService<ILogger<UseSerilogTests>>()
            .LogInformation("boot");

        Assert.Contains(captured, e => e.Level.Key == "information");
    }
}
```

- [ ] **Step 2: Run — expect FAIL** (`UseSerilog` undefined).
- [ ] **Step 3: Implement `UseSerilog`.** Overloads (match Serilog's public surface):
  1. `UseSerilog(this IHostBuilder, StructuredLogger logger, bool dispose = false)` — wrap + register the provider.
  2. `UseSerilog(this IHostBuilder)` — read the **P1** `Serilog.Log.Logger` slot.
  3. `UseSerilog(this IHostBuilder, Action<HostBuilderContext, LoggerConfiguration> configure)` and the `(context, services, loggerConfiguration)` 3-arg form — build a P2 `LoggerConfiguration` shim, invoke the lambda to let the caller configure it (including `.ReadFrom.Configuration(...)` which resolves to **P5**), then call `.CreateLogger()` to get the `StructuredLogger`, then register.
  4. Mirror the same four onto `IHostApplicationBuilder` (the minimal-API/`WebApplicationBuilder` path).
  All overloads funnel into one private `ConfigureServices(services => services.AddLogging(b => b.ClearProviders().AddSerilog(logger)))` — **DRY: reuses Task 2's `AddSerilog`, does not re-register by hand.** `ClearProviders()` defends the-fool risk (e) double-logging. Register an `IHostApplicationLifetime.ApplicationStopped` hook that calls the **P1** `Log.CloseAndFlush()`/the logger's flush on shutdown.
- [ ] **Step 4: Run — expect PASS.**
- [ ] **Step 5: Commit.**

```bash
git add src/Serilog.AspNetCore/SerilogHostBuilderExtensions.cs tests/AspNetCore/UseSerilogTests.cs
git commit -m "feat(serilog-compat): UseSerilog host hook over HeraldLoggerProvider (IHostBuilder + IHostApplicationBuilder)"
```

---

### Task 4: `RequestLoggingOptions` + `IDiagnosticContext` — the net-new surface (no middleware yet)

Serilog's request logging exposes a configurable message template, level, and an enrich hook (`EnrichDiagnosticContext`) plus an `IDiagnosticContext.Set(name, value)` the application calls mid-request to attach properties to the summary line. Build the options + diagnostic-context primitives first; the middleware (Task 5) consumes them.

**Files:**
- Create: `src/Serilog.AspNetCore/RequestLoggingOptions.cs`
- Create: `src/Serilog.AspNetCore/IDiagnosticContext.cs` + `DiagnosticContext.cs` (scoped collector, `AsyncLocal`-free — stored per-request in `HttpContext.Items` to avoid cross-request bleed)
- Test: `tests/AspNetCore/DiagnosticContextTests.cs`

- [ ] **Step 1: Write the failing test** — diagnostic-context properties are per-request and don't leak.

```csharp
[Fact]
public void DiagnosticContext_collects_per_request_without_bleed()
{
    var ctx = new DiagnosticContext(/* request-scoped collector */);
    ctx.Set("TenantId", "acme");
    var collected = ctx.Complete(); // snapshot of properties for this request
    Assert.Equal("acme", collected.Single(p => p.Name == "TenantId").Value);
}
```

- [ ] **Step 2: Run — expect FAIL.**
- [ ] **Step 3: Implement.** `RequestLoggingOptions` mirrors Serilog's public shape: `MessageTemplate` (default `"HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms"`), `GetLevel` (a `Func<HttpContext, double, Exception?, LogLevel>` defaulting Information, Error on exception/5xx, Warning on slow — FLAG: uses post-P0 `KnownLogLevels.Information/Error/Warning`), and `EnrichDiagnosticContext` (`Action<IDiagnosticContext, HttpContext>`). `DiagnosticContext` is registered scoped/singleton-with-per-request-storage; `Set` appends a `LogProperty`; `Complete()` returns the snapshot. Keep each type < 50 lines.
- [ ] **Step 4: Run — expect PASS.**
- [ ] **Step 5: Commit.**

```bash
git add src/Serilog.AspNetCore/RequestLoggingOptions.cs src/Serilog.AspNetCore/IDiagnosticContext.cs src/Serilog.AspNetCore/DiagnosticContext.cs tests/AspNetCore/DiagnosticContextTests.cs
git commit -m "feat(serilog-compat): RequestLoggingOptions + per-request DiagnosticContext"
```

---

### Task 5: `UseSerilogRequestLogging()` middleware — exactly one line per request (G-CORPUS.3 core)

The one net-new behavioural component. It times the request with a monotonic stopwatch, runs the pipeline, then writes **one** summary event with method/path/status/elapsed, merging the diagnostic-context properties and the `EnrichDiagnosticContext` hook.

**Files:**
- Create: `src/Serilog.AspNetCore/RequestLoggingMiddleware.cs`
- Create: `src/Serilog.AspNetCore/SerilogApplicationBuilderExtensions.cs` (`Microsoft.AspNetCore.Builder` namespace — `UseSerilogRequestLogging` on `IApplicationBuilder`, with the `Action<RequestLoggingOptions>` and `string messageTemplate` overloads)
- Test: `tests/AspNetCore/RequestLoggingMiddlewareTests.cs` (uses `Microsoft.AspNetCore.TestHost`)

- [ ] **Step 1: Write the failing tests** — the load-bearing G-CORPUS.3 assertions: exactly one line, right fields, right status, exception path still emits one line.

```csharp
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Herald.OSS.Serilog.AspNetCore.Tests;

public sealed class RequestLoggingMiddlewareTests
{
    private static async Task<(System.Collections.Generic.IReadOnlyList<CapturedEvent> log, HttpResponseMessage resp)>
        RunAsync(RequestDelegate terminal, System.Action<RequestLoggingOptions>? configure = null)
    {
        var (heraldLogger, captured) = TestLoggers.CreateCapturingStructured();
        using var host = await new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .UseSerilog(heraldLogger)
                .Configure(app =>
                {
                    app.UseSerilogRequestLogging(configure ?? (_ => { }));
                    app.Run(terminal);
                }))
            .StartAsync();

        var resp = await host.GetTestClient().GetAsync("/orders/42");
        return (captured, resp);
    }

    [Fact]
    public async Task Emits_exactly_one_summary_line_per_request()
    {
        var (log, _) = await RunAsync(ctx => { ctx.Response.StatusCode = 200; return Task.CompletedTask; });
        Assert.Single(log); // exactly one — the core G-CORPUS.3 contract
    }

    [Fact]
    public async Task Summary_line_carries_method_path_status_and_elapsed()
    {
        var (log, _) = await RunAsync(ctx => { ctx.Response.StatusCode = 201; return Task.CompletedTask; });
        var e = log.Single();
        Assert.Equal("GET",        e.Properties.Single(p => p.Name == "RequestMethod").Value);
        Assert.Equal("/orders/42", e.Properties.Single(p => p.Name == "RequestPath").Value);
        Assert.Equal(201,          e.Properties.Single(p => p.Name == "StatusCode").Value);
        Assert.Contains(e.Properties, p => p.Name == "Elapsed"); // present + numeric
    }

    [Fact]
    public async Task Captures_final_status_set_after_next_runs()
    {
        // status set inside the terminal, AFTER the middleware calls _next — must be 404, not 200.
        var (log, _) = await RunAsync(ctx => { ctx.Response.StatusCode = 404; return Task.CompletedTask; });
        Assert.Equal(404, log.Single().Properties.Single(p => p.Name == "StatusCode").Value);
    }

    [Fact]
    public async Task Still_emits_one_line_when_request_throws()
    {
        var (log, _) = await RunAsync(_ => throw new System.InvalidOperationException("boom"));
        var e = Assert.Single(log);
        Assert.Equal("error", e.Level.Key);              // default GetLevel: Error on exception
        Assert.Equal(500, e.Properties.Single(p => p.Name == "StatusCode").Value);
    }

    [Fact]
    public async Task EnrichDiagnosticContext_properties_land_on_the_summary_line()
    {
        var (log, _) = await RunAsync(
            ctx => { ctx.Response.StatusCode = 200; return Task.CompletedTask; },
            opts => opts.EnrichDiagnosticContext = (diag, http) => diag.Set("Host", http.Request.Host.Value));
        Assert.Contains(log.Single().Properties, p => p.Name == "Host");
    }

    [Fact]
    public async Task Custom_message_template_is_honored()
    {
        var (log, _) = await RunAsync(
            ctx => { ctx.Response.StatusCode = 200; return Task.CompletedTask; },
            opts => opts.MessageTemplate = "req {RequestMethod} -> {StatusCode}");
        Assert.Contains("req", log.Single().Message); // template applied
    }
}
```

- [ ] **Step 2: Run — expect FAIL** (`UseSerilogRequestLogging` undefined).
- [ ] **Step 3: Implement the middleware.** Skeleton (kept under the cognitive-complexity bar with guard clauses):
  - Resolve the Herald logger from DI (the registered provider's logger, or an injected `StructuredLogger`).
  - `var start = Stopwatch.GetTimestamp();` (monotonic — defends the-fool risk d).
  - `try { await _next(ctx); } catch (Exception ex) { LogSummary(ctx, elapsed, ex); throw; }` then `LogSummary(ctx, elapsed, null)` on the normal path. **One `LogSummary` call site per outcome, never two** — exactly-one-line is structural, not incidental (the-fool risks a/b).
  - `LogSummary`: compute elapsed ms from `Stopwatch.GetElapsedTime(start)`; read `ctx.Response.StatusCode` (final, post-`_next`); pick level via `options.GetLevel(ctx, elapsedMs, ex)`; build the property set = template fields (`RequestMethod`/`RequestPath`/`StatusCode`/`Elapsed`) + the request-scoped `DiagnosticContext.Complete()` snapshot + the `EnrichDiagnosticContext` hook output; emit through the Herald logger at the chosen level with `options.MessageTemplate`.
  - Skip-condition parity with Serilog: if the request was already logged (re-entrancy) or the response is a known framework no-log path, still emit exactly once — do **not** add silent skips that drop the line.
  - `SerilogApplicationBuilderExtensions.UseSerilogRequestLogging` registers the middleware + the scoped `DiagnosticContext`; overloads: `()`, `(Action<RequestLoggingOptions>)`, `(string messageTemplate)`.
- [ ] **Step 4: Run — expect PASS** (all six).
- [ ] **Step 5: Commit.**

```bash
git add src/Serilog.AspNetCore/RequestLoggingMiddleware.cs src/Serilog.AspNetCore/SerilogApplicationBuilderExtensions.cs tests/AspNetCore/RequestLoggingMiddlewareTests.cs
git commit -m "feat(serilog-compat): UseSerilogRequestLogging middleware — one summary line per request"
```

---

### Task 6: G-CORPUS.3 wiring-shape suite (real-Serilog snippet corpus)

Per the test inventory, G-CORPUS.3 is a **SUITE**: standard Serilog ASP.NET wiring code recompiles and runs. Tasks 2/3/5 proved the units; this task pins the *idiomatic snippets a user would copy from Serilog docs* compile and produce the right shape.

**Files:**
- Test: `tests/AspNetCore/AspNetWiringCorpusTests.cs`

- [ ] **Step 1: Write the corpus tests** — each is a verbatim-shaped Serilog snippet:
  - `builder.Host.UseSerilog((ctx, cfg) => cfg.MinimumLevel.Information().WriteTo.Console());` (drives P2 + P5 — FLAG cross-plan; if P5 console mapping isn't landed, this row is `[Fact(Skip="P5")]` with the skip reason naming the plan).
  - `app.UseSerilogRequestLogging();` (default template) → one line, default fields.
  - `services.AddSerilog();` (parameterless, reads P1 `Log.Logger`) → MEL `ILogger<T>` routes to Herald.
  - The `(context, services, loggerConfiguration)` 3-arg `UseSerilog` overload with `.ReadFrom.Configuration(context.Configuration)` → resolves to P5; assert it binds (or skip-with-named-reason if P5 absent).
- [ ] **Step 2: Run.** Rows whose cross-plan dependency is present → PASS; rows gated on a not-yet-landed plan → explicit `Skip` naming the plan (never a silent omission — the skip *is* the cross-plan flag).
- [ ] **Step 3: Commit.**

```bash
git add tests/AspNetCore/AspNetWiringCorpusTests.cs
git commit -m "test(serilog-compat): G-CORPUS.3 ASP.NET wiring corpus (UseSerilog/AddSerilog/RequestLogging)"
```

---

### Task 7: Cross-plan integration smoke + AOT/build close

**Files:**
- Test: `tests/AspNetCore/CrossPlanIntegrationTests.cs` (un-skip the rows P2/P5 gate once those plans land)

- [ ] **Step 1: Full solution build + test.**

```bash
cd /e/dev/Herald.OSS && bash build.sh --all --test 2>&1 | tail -20
```
Expected: green (P6 rows that depend on un-landed P1/P2/P5 symbols remain `Skip`-flagged, not failing).

- [ ] **Step 2: AOT-clean check (G-GAP.7).** The new assembly publishes with no new trim/AOT warnings vs the Herald.OSS baseline. Middleware uses reflection-free DI resolution; confirm no analyzer regressions.

```bash
dotnet test tests/AOT/Herald.OSS.Aot.Tests.csproj -v minimal 2>&1 | tail -10
```

- [ ] **Step 3: DRY tripwire grep** — the new assembly must not reimplement MEL or duplicate `HeraldLoggerProvider` logic. No level `switch`, no `{OriginalFormat}` parsing, no `Log<TState>` body should appear in `src/Serilog.AspNetCore/`.

```bash
grep -rnE "OriginalFormat|switch.*LogLevel|class .*: .*ILoggerProvider|Log<TState>" src/Serilog.AspNetCore || echo "clean (thin over HeraldLoggerProvider)"
```
Expected: `clean` — any hit means P6 started reimplementing the provider; reject.

- [ ] **Step 4: Confirm cross-plan flags are resolved or documented.** For every `[Fact(Skip="Pn")]` still present, confirm the owning plan is not yet merged; list them in the self-review note below so integration knows what to un-skip.
- [ ] **Step 5: Final commit + note P6 done.**

```bash
git add -A docs/serilog-compat tests/AspNetCore src/Serilog.AspNetCore
git commit -m "chore(serilog-compat): P6 ASP.NET Core complete — thin over HeraldLoggerProvider"
```

---

## Self-review notes

- **Spec coverage:** P6 implements Richard §A.1 (`MMP.Herald.Serilog.AspNetCore`; `UseSerilog`/`AddSerilog` over the existing `HeraldLoggerProvider`; `UseSerilogRequestLogging` as the one net-new component) + Jared §Open-Q2 (provide our own host/MEL wiring; identity wall) + Echo **G-CORPUS.3** (wiring output shape; exactly one request line) and **G-GAP.7** (AOT-clean). G-VM/G-HOT/G-LEVEL/G-SEC live in P1/P2/P0 — out of P6 scope.
- **CUPID/DRY:** all three entry points funnel through one `AddSerilog` registration; the provider, level mapping, and `{OriginalFormat}` parsing stay in the already-shipping `HeraldLoggerProvider` (Task 7 Step 3 grep enforces this). The only net-new behaviour is the request-logging middleware + its options/diagnostic-context.
- **Cross-plan dependencies (FLAGGED):** P2 `LoggerConfiguration` shim + `.CreateLogger()`; P1 static `Serilog.Log.Logger` + `CloseAndFlush()`; P5 `ReadFrom.Configuration` extension. Where a P6 path needs an un-landed symbol, the test row is `[Fact(Skip="Pn")]` — the skip is the explicit integration signal, never a silent gap.
- **Every gap → a test:** the-fool's six request-logging failure modes (zero/two lines, wrong status, clock skew, double-registration, exception path) each map to a test in Tasks 2/3/5.
- **Open decisions for integration:**
  1. **Namespace vs assembly** — confirm with Richard that the consumer-facing extensions live in the `Serilog`/`Microsoft.AspNetCore.Builder`/`Microsoft.Extensions.Hosting`/`Microsoft.Extensions.Logging` namespaces (so user wiring recompiles) while the *assembly* is `MMP.Herald.Serilog.AspNetCore`. Task 1 assumes yes.
  2. **`Log.CloseAndFlush` on shutdown** — whether P6 owns the `ApplicationStopped` flush hook or P1's facade does. Task 3 wires it in P6; confirm no double-flush with P1.
  3. **Default `GetLevel` slow-request threshold** — Serilog has none by default (Information unless 5xx/exception). Task 4 mirrors that; confirm we don't add a Herald-opinionated slow-warning default that diverges from the parity oracle.

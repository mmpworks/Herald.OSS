# P6 ASP.NET Core Wiring — the-fool Pre-Mortem

> Generated: 2026-05-30. Input: the-fool skill, pre-mortem mode (Find the Failure Modes).
> Framing: "A new `MMP.Herald.Serilog.AspNetCore` assembly provides `UseSerilog`/`AddSerilog`/
> `UseSerilogRequestLogging` over the existing `HeraldLoggerProvider`. Where does the
> request-logging middleware emit zero, two, or wrong-status lines? Where does
> double-provider-registration duplicate every log line? Where does `UseSerilog` swallow the P2
> `LoggerConfiguration` and silently log nothing? Where does an exception thrown mid-request make
> the middleware skip its one summary line?"

---

## Summary

| Severity | Count | All mitigated? |
|----------|-------|----------------|
| CRITICAL | 1     | Yes — mitigated by Task 5 (structural constraint) |
| HIGH     | 5     | 2 of 5 mitigated; **3 have no mitigating task** |
| MEDIUM   | 3     | 1 of 3 mitigated; **2 partially or not mitigated** |

**Three ship-blockers (no mitigating task today):**

- **HIGH: FM-2** — double middleware registration emits two summary lines per request; no sentinel
  guard exists in the plan. Mitigating test: `Registers_twice_still_emits_exactly_one_line`. Must go
  RED when the sentinel is removed.
- **HIGH: FM-7** — `ApplicationStopped` flush ownership ambiguity between P6 and P1 can silently
  drop the final queued log events at shutdown. OD-2 in the plan's self-review names it but does not
  resolve it. Requires an idempotent-flush test before Task 3 ships.
- **HIGH: FM-8** — a throwing user-supplied `EnrichDiagnosticContext` or `GetLevel` callback
  suppresses the summary line entirely. `LogSummary` must wrap user callbacks in their own
  try/catch. No current Task 5 step names this.

**Highest-severity unmitigated risk:** FM-7 (shutdown flush ownership) — produces silent log loss
at the exact moment monitoring is most critical.

---

## Risk Catalog

### FM-1: Middleware Emits Zero Lines — Wrong Pipeline Registration Order

**Severity: HIGH**

**Description.**
`RequestLoggingMiddleware` only logs requests that flow *through* it. If `UseSerilogRequestLogging()`
is registered after `UseStaticFiles()`, `UseHealthChecks()`, or any other response-capable middleware,
those earlier middlewares short-circuit the pipeline and return before reaching the logging middleware.
Zero summary lines are emitted for every static asset, health probe, 304 Not Modified, and CORS
preflight. No exception fires.

**Why invisible in a happy-path test.** The G-CORPUS.3 test suite uses `TestHost` with a single
terminal delegate. `TestHost` pipelines do not include `UseStaticFiles` by default. Tests pass while
production traffic produces silent blind spots.

**Second-order effect.** Monitoring dashboards configured to alert on "one line per request" silently
undercount. Static assets and health probes vanish from logs entirely. The team only discovers this
when cross-referencing infrastructure metrics against log counts — typically after an incident.

**Mitigating Task: Task 5 (Step 3).**
Document the required registration order as a hard constraint — `UseSerilogRequestLogging()` must
appear before `UseStaticFiles()`, `UseRouting()`, and authentication middleware. Task 5 Step 3
should also add a startup-time `ILogger<RequestLoggingMiddleware>` warning when the middleware
detects it is registered after a response-producing middleware (check `app.Properties` for known
sentinel keys set by `UseStaticFiles` etc.).

**Negative test required:** A test that registers `UseStaticFiles` before `UseSerilogRequestLogging`
and asserts that a request for a static path still produces exactly one summary line — OR documents
explicitly that static-file paths are a known exclusion and monitoring must account for it.

**Does the test go RED if the mitigation is removed?** The startup-order check does — but only if
the test exercises the wrong-order case. Without that test row, the mitigation is documentation-only.
**FLAG: add the test.**

---

### FM-2: Middleware Emits Two Lines — Double Registration, No Sentinel Guard

**Severity: HIGH — NO MITIGATING TASK**

**Description.**
A developer calls `app.UseSerilogRequestLogging()` twice — once in `Program.cs` and once in a shared
startup helper, an `IStartupFilter`, or a library that "always adds request logging." No guard
exists. Both middleware instances wrap `_next` independently. The inner instance calls `LogSummary`;
control returns to the outer instance, which also calls `LogSummary`. Two summary lines per request.
No exception. Tests pass because every test registers the middleware exactly once.

Note that Task 5 Step 3's "exactly-one-line is structural, not incidental" statement refers to the
one-`LogSummary`-call-site discipline *within* a single middleware instance — it does not prevent two
instances coexisting in the pipeline.

**Second-order effect.** Log aggregation alert rules that fire on "more than N error lines per
request" double-fire. Downstream deduplication by `{RequestId}` sees two structurally identical
events and must arbitrarily pick one. Error-rate percentages double silently. Billing on log volume
doubles. Teams spend days diagnosing "apparent double-traffic" before discovering the configuration
issue.

**Mitigating Task: NONE.**

**Required addition to Task 5 Step 3:** Register `UseSerilogRequestLogging` with a sentinel key in
`app.Properties` (e.g., `"herald.requestlogging.registered"`). On registration, check for the key
and throw `InvalidOperationException("UseSerilogRequestLogging() has already been registered in this
pipeline. Call it once.")` or, at minimum, log a CRITICAL startup-time warning. This is the pattern
ASP.NET Core itself uses for middleware ordering guards (`UseRouting`/`UseEndpoints`).

**Negative test required (ship-blocker):**
```csharp
[Fact]
public async Task Registers_twice_still_emits_exactly_one_line_OR_throws()
{
    // Either: the sentinel throws at registration time (preferred).
    // Or: two registrations are idempotent and produce exactly one summary line.
    // The test must go RED if the sentinel is removed and double-registration
    // silently produces two lines.
}
```

**Does the test go RED if the mitigation is removed?** Yes, if the test asserts
`Assert.Single(captured)` against a double-registered pipeline. Without this test, the double-line
failure ships silently.

---

### FM-3: Status Code Always 200 — Read Before `_next` Returns

**Severity: CRITICAL**

**Description.**
An implementation that reads `ctx.Response.StatusCode` before `await _next(ctx)` captures the
default value (200) regardless of what the endpoint sets. This is the canonical middleware ordering
mistake. The endpoint sets 404, the middleware logs 200. No exception. The error is structurally
invisible on a happy-path test unless the test explicitly asserts the *value* (not just presence).

**Second-order effect.** Error-rate monitoring based on logged status codes silently underreports
4xx/5xx. A 500-producing endpoint appears healthy in the log dashboard. SLO dashboards based on
logged status are wrong. Postmortem evidence is corrupt.

**Mitigating Task: Task 5 (Steps 1 and 3).**
Task 5 Step 3 specifies reading `ctx.Response.StatusCode` post-`_next`. The test
`Captures_final_status_set_after_next_runs` explicitly asserts `Assert.Equal(404, ...)` — the value
assertion, not just presence. This is the correct test shape.

**Negative test:** The test itself IS the negative test — if the implementation reads status before
`_next`, the assertion fails (`200 != 404`). **Mitigated by Task 5**, conditional on the test
shipping with the explicit `404` value assertion (not `Assert.NotNull` or `Assert.Contains`).

**Does the test go RED if the guard is removed?** Yes — if status is read before `_next`, the
`404` assertion fails immediately. Confirmed mitigation.

---

### FM-4: Elapsed Measured with `DateTime.Now` — NTP Clock Skew

**Severity: HIGH**

**Description.**
If elapsed time is computed as `(DateTime.Now - startTime).TotalMilliseconds` rather than
`Stopwatch.GetElapsedTime(start)`, an NTP clock adjustment mid-request produces a negative or wildly
wrong elapsed value. `Elapsed = -23.0` does not throw — it formats into the `{Elapsed}` template
hole as a negative number. Happy-path tests complete in microseconds and never encounter clock
adjustments.

**Second-order effect.** A single NTP correction produces a batch of requests with impossible elapsed
times. P99 latency dashboards become untrustworthy. Alert rules that threshold on `Elapsed > 5000ms`
silently fail to fire on long-running requests that show as negative elapsed (since `-500 < 0 <
5000`). Worse: the alert-suppression goes unnoticed until an incident postmortem.

**Mitigating Task: Task 5 (Step 3).**
Task 5 Step 3 specifies `Stopwatch.GetTimestamp()` / `Stopwatch.GetElapsedTime(start)`. This is the
correct monotonic path. The review check is binary: does the implementation reference `DateTime`
anywhere in elapsed computation? If yes, reject.

**Negative test required:** The test suite does not need to simulate NTP drift. The code review
tripwire in Task 7 Step 3 (the `grep` for non-monotonic patterns) covers this. Add `DateTime.Now`
to the grep pattern alongside `{OriginalFormat}` and level-switch reimplementation:
```bash
grep -rnE "DateTime\.(Now|UtcNow)|TimeSpan\.FromTicks" src/Serilog.AspNetCore/ || echo "clean"
```

**Does the test go RED if the guard is removed?** No automated test catches clock-skew directly.
The grep tripwire in Task 7 catches the wrong implementation pattern at code-review time.
**Mitigation is review-gate only — acceptable for this risk class.**

---

### FM-5: `AddSerilog` Without `ClearProviders` — Runtime Double-Logging

**Severity: MEDIUM**

**Description.**
A developer who uses `AddSerilog` directly (bypassing `UseSerilog`) and does not call
`ClearProviders()` retains the default MEL console provider alongside `HeraldLoggerProvider`. Every
log line goes to both. No exception. The test `AddSerilog_registers_exactly_one_HeraldLoggerProvider`
checks the DI registration count — but it calls `ClearProviders()` in the test setup. It passes
while a real caller who forgets `ClearProviders` gets silent double-logging in production.

The plan notes that `UseSerilog` calls `ClearProviders()` (correct — high-level hook, full
ownership). `AddSerilog` deliberately does not (correct — low-level building block, caller is
responsible). The gap is that this convention is not documented at the `AddSerilog` call site and
no test exercises the misconfigured path.

**Second-order effect.** Structured logs appear twice in container stdout. Datadog/Splunk ingest
volume doubles. Deduplication is imperfect (two providers may format the message differently even
for the same event). Log storage costs double silently.

**Mitigating Task: Partial — Task 2 (documentation only).**
Task 2 Step 3 specifies the `TryAddEnumerable` guard against double-*Herald*-registration, which is
correct. It does not address the `ClearProviders` documentation gap.

**Required addition to Task 2:**
- Add an XML-doc comment on `AddSerilog` overloads: `/// <remarks>If the default MEL console
  provider should be suppressed, call <see cref="ILoggingBuilder.ClearProviders"/> before this
  method. <see cref="SerilogHostBuilderExtensions.UseSerilog"/> calls ClearProviders automatically.
  </remarks>`
- Add a test: `AddSerilog_without_ClearProviders_produces_two_providers_in_DI` — asserts that a
  `services.AddLogging(b => b.AddSerilog(logger))` (without `ClearProviders`) contains more than
  one `ILoggerProvider` registration. This makes the footgun visible and documented rather than
  discovered in production.

**Does the test go RED if the guard is removed?** The proposed test documents the behavior rather
than blocking the footgun — it asserts that two providers ARE present (a warning, not a gate).
That is the correct disposition for `AddSerilog` (which must remain a building block).

---

### FM-6: `UseSerilog` Lambda Invoked After `Build()` — Provider Not in DI Container

**Severity: MEDIUM**

**Description.**
The lambda overload `UseSerilog((context, services, loggerConfiguration) => ...)` must be invoked
during `ConfigureServices` (before `Build()`). If the implementation defers the lambda to an
`IHostedService.StartAsync` or a post-`Build()` `IStartupFilter`, the `HeraldLoggerProvider` is not
registered during `Build()`. Services with `ILogger<T>` constructor injection throw
`InvalidOperationException` at resolution time — but only when those services are first resolved.
If a critical service is not resolved until the first request, the application appears to start
successfully (health probes pass, load balancer marks the instance healthy) and dies on the first
real call.

**Second-order effect.** Blue-green deployments succeed. Traffic shifts to the new instance. The
first user request fails. Rollback begins. The root cause ("provider registered too late") is not
obvious from the exception stack trace, which points to the consuming service, not the DI
registration.

**Mitigating Task: Task 3 (Step 3).**
Task 3 Step 3 specifies the lambda is invoked inside `IHostBuilder.ConfigureServices`. The test
`UseSerilog_lambda_receives_the_P2_LoggerConfiguration_shim` calls `Build()` and immediately
resolves the logger — this is the correct test shape and catches the late-registration failure.
**Mitigated by Task 3**, conditional on the test calling `host.Services.GetRequiredService<ILogger<...>>`
immediately after `Build()` (which the current test does).

**Does the test go RED if the guard is removed?** Yes — if the lambda is deferred past `Build()`,
the provider is absent during `Build()`, and `GetRequiredService` throws in the test. Confirmed
mitigation.

---

### FM-7: Double-Flush on `ApplicationStopped` — P6 and P1 Both Own the Hook

**Severity: HIGH — NO MITIGATING TASK**

**Description.**
Task 3 Step 3 specifies that `UseSerilog` registers an `IHostApplicationLifetime.ApplicationStopped`
hook that calls `Log.CloseAndFlush()`. P1's static `Log` facade (Task 7 of P1) may *also* register
an `ApplicationStopped` hook when `Log.Logger` is assigned — this is explicitly noted as a design
choice in P1. If both hooks fire during host shutdown, `CloseAndFlush` is called twice on the same
underlying `StructuredLogger`.

The second call behavior depends entirely on whether `StructuredLogger.Dispose()` is idempotent.
If it throws on a second call, the exception propagates through the host shutdown sequence and
suppresses the `Flushing async drains...` phase. The final queued log events — the ones emitted
during graceful shutdown ("Application stopped", "Connection drained") — are lost silently.

**Why invisible in a happy-path test.** Tests don't exercise `host.StopAsync()` followed by
validation that all pre-stop log events arrived. Shutdown is implicit (`using var host = ...`).

**Second-order effect.** The shutdown log entries that confirm successful drain ("all connections
closed", "pipeline flushed", "N events written") are the entries most likely to be in the async
drain queue at shutdown time. Losing them means the postmortem has a gap at exactly the moment the
system was transitioning state. This is the worst window to lose log coverage.

**Mitigating Task: NONE.**

The plan's self-review (Open Decision 2) names this: "whether P6 owns the `ApplicationStopped` flush
hook or P1's facade does." It does not resolve it. This must be resolved before Task 3 ships.

**Required resolution (must add to Task 3):**
1. Decide ownership: P6 owns the hook, P1 does not register one. OR: P1 owns the hook, P6 does
   not call `CloseAndFlush` (it only unregisters its DI provider).
2. Whichever does NOT own the hook must be a verified no-op on `ApplicationStopped` — confirmed by
   code inspection, not assumption.
3. Ensure `StructuredLogger.Dispose()` is idempotent regardless (defensive programming) — but do
   not rely on idempotency as the primary guard; rely on single ownership.

**Required test:**
```csharp
[Fact]
public async Task ApplicationStopped_flushes_exactly_once_and_does_not_throw()
{
    var flushCount = 0;
    var (heraldLogger, _) = TestLoggers.CreateCapturingStructured(onFlush: () => flushCount++);
    using var host = Host.CreateDefaultBuilder().UseSerilog(heraldLogger).Build();
    await host.StartAsync();
    await host.StopAsync();
    Assert.Equal(1, flushCount); // exactly one flush, no double-dispose exception
}
```

**Does the test go RED if the mitigation is removed?** Yes — a double-flush increments `flushCount`
to 2, failing `Assert.Equal(1, ...)`. If the second call throws, `StopAsync` propagates the
exception, failing the test. Both failure modes are caught.

---

### FM-8: Exception Mid-Request — User Callback Suppresses Summary Line

**Severity: HIGH — NO MITIGATING TASK**

**Description.**
The middleware structure is:
```csharp
try { await _next(ctx); }
catch (Exception ex) { LogSummary(ctx, elapsed, ex); throw; }
LogSummary(ctx, elapsed, null);
```
This correctly ensures one summary line per outcome. The gap: `LogSummary` itself calls
`options.GetLevel(ctx, elapsedMs, ex)` and `options.EnrichDiagnosticContext(diagnosticContext, ctx)`,
both of which are user-supplied delegates. If either delegate throws, the exception escapes
`LogSummary`. The `LogSummary` call inside the `catch` block now throws instead of re-throwing the
original request exception. The summary line is never written. The original exception may be masked
by the `LogSummary` exception depending on exception-chaining.

**Concrete scenario:** A developer writes:
```csharp
opts.EnrichDiagnosticContext = (diag, http) =>
    diag.Set("TenantId", http.Items["TenantId"].ToString()); // NullReferenceException when item absent
```
For every unauthenticated request, `http.Items["TenantId"]` is null, `ToString()` throws, `LogSummary`
throws, and the summary line is suppressed. The endpoint's exception and the suppressed summary line
both go dark. The monitoring gap is total.

**Why invisible in a happy-path test.** All test-suite `EnrichDiagnosticContext` lambdas are
well-behaved. The failure only surfaces when user code is buggy — but this is precisely the class of
failures a production system must survive.

**Second-order effect.** A misbehaving enrichment hook silently suppresses every summary line
for every request where the hook throws. The monitoring gap is indefinite until someone notices that
the request-log volume dropped.

**Mitigating Task: NONE.**

**Required addition to Task 5 Step 3:**
Wrap all user-supplied callbacks in `LogSummary` with their own try/catch:
```csharp
LogLevel chosenLevel;
try { chosenLevel = options.GetLevel(ctx, elapsedMs, ex); }
catch { chosenLevel = LogLevel.Information; /* SelfLog the hook exception */ }

try { options.EnrichDiagnosticContext?.Invoke(diagnosticContext, ctx); }
catch { /* SelfLog the hook exception; continue with partial enrichment */ }
```

**Required test (ship-blocker):**
```csharp
[Fact]
public async Task EnrichDiagnosticContext_that_throws_still_emits_one_summary_line()
{
    var (log, _) = await RunAsync(
        ctx => { ctx.Response.StatusCode = 200; return Task.CompletedTask; },
        opts => opts.EnrichDiagnosticContext = (_, _) =>
            throw new InvalidOperationException("hook bug"));
    // Summary line must still be emitted — the hook's exception must not suppress it.
    Assert.Single(log);
}
```

**Does the test go RED if the mitigation is removed?** Yes — without the try/catch, the throwing
hook propagates through `LogSummary`, the summary line is never emitted, `captured` is empty, and
`Assert.Single(log)` fails. Confirmed ship-blocker.

---

### FM-9: `{StatusCode}` Template Hole — Int vs String Type Parity

**Severity: MEDIUM**

**Description.**
Serilog's request-logging middleware stores `StatusCode` as an `int` property. Herald's template
formatter may render it differently depending on how the property is constructed. If `StatusCode` is
added as `LogProperty("StatusCode", "404")` (string), JSON-structured output renders it as `"404"`
(quoted). Downstream log aggregation queries that filter on `StatusCode == 404` (integer comparison
in Kibana/Splunk/Seq) find zero results. The string case is a latent parity break that only surfaces
when a user migrates from Serilog and runs their existing dashboard queries.

**Second-order effect.** SIEM rules and SLO dashboards written against Serilog's integer `StatusCode`
break silently after migration. The logs look correct (the number appears in the rendered message)
but structured queries return empty. This is a cross-system portability failure that damages trust in
the migration.

**Mitigating Task: Partial — Task 5 (Step 1).**
The test `Summary_line_carries_method_path_status_and_elapsed` asserts `Assert.Equal(201, ...)` on
the `StatusCode` property value. If `LogProperty.Value` stores the value as `object`, this assertion
catches the string-vs-int case IF the test uses value equality (201 != "201"). This is the correct
test shape and is conditionally mitigating.

**Conditional mitigation caveat:** If `LogProperty.Value` is typed as `string` internally (or if
the assert uses `ToString()` comparison), the test passes for both `201` (int) and `"201"` (string),
and the parity break ships silently. Verify that the assertion is a typed integer comparison, not a
string comparison.

**Required addition:** Add a test that reads the `StatusCode` property's runtime type:
```csharp
Assert.IsType<int>(log.Single().Properties.Single(p => p.Name == "StatusCode").Value);
```
This is one line added to the existing `Summary_line_carries_method_path_status_and_elapsed` test.

---

## Risks with NO Mitigating Task

| Risk | Severity | Required Addition |
|------|----------|-------------------|
| FM-2: Double middleware registration — no sentinel guard | HIGH | Add `app.Properties` sentinel in Task 5 Step 3; add `Registers_twice_still_emits_exactly_one_line` test |
| FM-7: `ApplicationStopped` flush ownership ambiguity (P6 vs P1) | HIGH | Resolve OD-2 before Task 3 ships; single owner, no-op on the other; add idempotent-flush test |
| FM-8: Throwing user callback suppresses summary line | HIGH | Wrap `GetLevel`/`EnrichDiagnosticContext` in try/catch in `LogSummary`; add `EnrichDiagnosticContext_that_throws_still_emits_one_summary_line` test |

---

## Conditional Mitigations (Risk Survives if Condition Not Met)

| Risk | Condition for Full Mitigation |
|------|------------------------------|
| FM-1: Wrong pipeline order | Task 5 must add a startup-order test OR document static-file exclusion explicitly; documentation-only mitigation is insufficient |
| FM-3: Status read before `_next` | `Captures_final_status_set_after_next_runs` must assert the explicit value `404`, not just presence; if changed to `Assert.NotNull`, the test becomes vacuous |
| FM-5: `AddSerilog` without `ClearProviders` | Requires XML-doc comment on `AddSerilog` overloads; the DI-count test makes the behavior documented and visible |
| FM-9: `{StatusCode}` int vs string | `Summary_line_carries_method_path_status_and_elapsed` must include `Assert.IsType<int>(...)` on the property value; a string-equality assert is vacuously passing |

---

## Open Decisions Requiring Resolution Before the Named Task Merges

| OD | Must Resolve Before | Risk if Deferred |
|----|--------------------|--------------------|
| OD-P6-1: Who owns the `ApplicationStopped` flush hook — P6 or P1? | Task 3 (before Step 3) | Double-flush silently drops final log events at shutdown |
| OD-P6-2: Does `UseSerilogRequestLogging` throw or warn on double-registration? | Task 5 (before Step 3) | Double-registration silently doubles every summary line forever |
| OD-P6-3: Should `AddSerilog` document or guard the `ClearProviders` omission? | Task 2 (before Step 5 commit) | Footgun is invisible until production log volume mysteriously doubles |

---

## Relationship to G-CORPUS.3 Requirements

| G-CORPUS.3 assertion | Maps to | Status in P6 |
|----------------------|---------|-------------|
| Exactly one line per request | Task 5 (structural) | Mitigated within one instance (CRIT-3); FM-2 double-instance gap is open |
| Right fields (method/path/status/elapsed) | Task 5 Step 1 | Mitigated; FM-9 int-vs-string caveat applies |
| Right status (final, post-`_next`) | Task 5 Steps 1+3 | Mitigated (FM-3) |
| Exception path still emits one line | Task 5 Step 1 | Mitigated for request exceptions; FM-8 user-callback gap is open |
| `EnrichDiagnosticContext` properties on summary | Task 5 Step 1 | Mitigated for well-behaved hooks; FM-8 covers throwing hooks |

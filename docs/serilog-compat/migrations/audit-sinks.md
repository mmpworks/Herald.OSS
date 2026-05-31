---
gap-id: audit-sinks
serilog-surface: AuditTo vs WriteTo (sink-failure semantics)
herald-status: carries-over (auditMode bool on the sink adapter)
population-rank: medium
regression-test-id: G-SEC.2, G-SEC.3
---

<!-- Heather T-H2: STANDALONE companion. COMPLIANCE callout — AuditTo throws,
     WriteTo swallows. Silently swallowing an audit failure is the worst break. -->

# Migrating AuditTo / WriteTo Semantics

## The one difference that matters

`WriteTo` swallows sink failures and reports them via `SelfLog`. A failing `WriteTo` sink does not surface an exception to the caller — the log call completes normally, and the event is silently dropped.

`AuditTo` is different by contract: it re-throws the sink's exception to the caller. A log call through `AuditTo` that encounters a sink failure raises an exception. This is the compliance guarantee — your code can catch the exception and know definitively whether the log event was delivered.

Herald preserves this distinction. Silently swallowing an audit failure is the worst possible break for any compliance-sensitive deployment.

## What you have in Serilog

```csharp
// Standard write — failures are swallowed
Log.Logger = new LoggerConfiguration()
    .WriteTo.Sink(new MyAuditSink())
    .CreateLogger();

// Audit write — failures propagate
Log.Logger = new LoggerConfiguration()
    .AuditTo.Sink(new MyAuditSink())
    .CreateLogger();
```

If your code wraps log calls in a try/catch and relies on the audit exception propagating, that contract matters.

## What changes

Nothing in your configuration code changes. `AuditTo.Sink(new MyAuditSink())` compiles and behaves as before — the `auditMode` bool is threaded through the adapter internally. You do not set it; the verb sets it.

The `AuditTo` verb is present for the same surfaces as `WriteTo` — `.Sink(...)`, the named Herald sinks, and sub-loggers (`AuditTo` inside `WriteTo.Logger(lc => ...)` inherits the throw-on-failure semantics).

## Step-by-step

1. Recompile against the Layer-1 assembly. No configuration changes required.

2. If your sink is source-compiled and uses `ILogEventSink`, follow [custom-sink.md](custom-sink.md) first to update its `using` directives.

3. Rebuild and run verification below.

## Verify the oppositional pair

Two assertions cover the contract (matching G-SEC.2 and G-SEC.3 in the test suite):

**G-SEC.2 — `AuditTo` throws, `WriteTo` swallows:**

```csharp
// A sink that always throws
var throwingSink = new AlwaysThrowingSink();

// WriteTo: the log call completes normally; sink failure is swallowed
var writeLogger = new LoggerConfiguration().WriteTo.Sink(throwingSink).CreateLogger();
writeLogger.Information("test");  // no exception

// AuditTo: the log call propagates the sink exception
var auditLogger = new LoggerConfiguration().AuditTo.Sink(throwingSink).CreateLogger();
Assert.Throws<SinkException>(() => auditLogger.Information("test"));
```

**G-SEC.3 — Redaction runs before audit capture:**

If your pipeline includes a destructuring policy that strips sensitive fields, that policy must fire before the event reaches the audit sink. A secret field must be absent from the event delivered to the audit sink — not just from a post-sink log. Verify by asserting the stripped field is absent from the `LogEvent` received by the sink, not just from the output string.

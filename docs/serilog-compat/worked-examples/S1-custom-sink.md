# S1: WriteTo.Sink — Custom ILogEventSink

> **Hard boundary**: `WriteTo.Sink(mySink)` only works for sinks you compile from
> source against `MMP.Herald.Serilog`. Pre-compiled community packages
> (`Serilog.Sinks.Seq`, `Serilog.Sinks.MSSqlServer`, `Serilog.Sinks.Datadog`, etc.)
> were compiled against real Serilog's assembly identity
> (`PublicKeyToken=24c2f752a8e58a10`), which this shim does not and cannot match.
> Do not assume "custom sinks work" means "Seq works." The identity boundary is
> documented in the parity audit.

## What this covers

`WriteTo.Sink(ILogEventSink)` lets you wire any sink you write yourself into a
Herald pipeline using the familiar Serilog fluent API. The sink receives a
`MMP.Herald.Serilog.Events.LogEvent` — the same P1 mirror that every Serilog-shaped
surface in this shim uses.

This is the right path when you own the sink code. It is not the path for
pre-packaged community sinks.

## Minimal example

```csharp
using MMP.Herald.Serilog.Configuration;
using MMP.Herald.Serilog.Core;
using MMP.Herald.Serilog.Events;

// 1. Implement ILogEventSink from MMP.Herald.Serilog.Core.
public sealed class ConsoleSink : ILogEventSink
{
    public void Emit(LogEvent logEvent)
        => Console.WriteLine($"[{logEvent.Level}] {logEvent.RenderMessage()}");
}

// 2. Wire it via WriteTo.Sink.
var log = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .WriteTo.Sink(new ConsoleSink())
    .CreateLogger();

log.Information("Hello, {Name}!", "Herald");
// Output: [Information] Hello, Herald!
```

## Multiple sinks

Multiple `WriteTo.Sink()` calls are supported. Every registered sink receives
every event that passes the pipeline floor.

```csharp
var log = new LoggerConfiguration()
    .WriteTo.Sink(new ConsoleSink())
    .WriteTo.Sink(new RecordingSink(capturedEvents))
    .CreateLogger();
```

## Minimum-level restriction

Pass `restrictedToMinimumLevel` to apply a per-sink floor in addition to the
pipeline floor. Events below the pipeline floor never reach any sink; events
above the pipeline floor but below the per-sink floor are filtered at the sink.

```csharp
var log = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .WriteTo.Sink(new AlertSink(), restrictedToMinimumLevel: LogEventLevel.Error)
    .CreateLogger();

log.Debug("verbose trace");   // filtered: below AlertSink floor
log.Error("disk full");       // delivered to AlertSink
```

Note: per-sink floor restriction is applied at the JSON config level. The
adapter maps `Verbose` to "no restriction" (inherits pipeline floor) and any
other level to the matching Herald key string.

## Audit mode

Set `auditMode: true` when you need delivery guarantees. In audit mode,
exceptions from the sink propagate rather than being swallowed. This mirrors
Serilog's `AuditTo.Sink` semantics.

```csharp
var log = new LoggerConfiguration()
    .WriteTo.Sink(new ComplianceSink(), auditMode: true)
    .CreateLogger();
```

In normal mode (default), a throwing sink is silently bypassed and the next
sink in the fan-out still receives the event. Task 5 wires `SelfLog` reporting
for swallowed exceptions.

## What you get on the mirror

`ILogEventSink.Emit` receives a `LogEvent` with:

| Property | What you see |
|---|---|
| `Level` | Serilog `LogEventLevel` mapped from Herald's level system |
| `Timestamp` | UTC time converted to local (Serilog convention) |
| `MessageTemplate` | The raw message template string |
| `RenderMessage()` | The rendered message with properties substituted |
| `Properties` | All structured properties projected into `LogEventPropertyValue` |
| `Exception` | The exception if one was passed; otherwise `null` |

The `Properties` dictionary includes `HeraldLevel` when the native level has no
direct Serilog equivalent (e.g. a custom level). The true level key is preserved
there so sink code that needs precision can read it.

## Why pre-compiled sinks do not work

A pre-compiled NuGet sink such as `Serilog.Sinks.Seq` contains a reference to
`Serilog.Core.ILogEventSink` with assembly identity:

```
Serilog, Version=3.x.x.x, Culture=neutral, PublicKeyToken=24c2f752a8e58a10
```

The CLR checks that identity at load time. `MMP.Herald.Serilog` ships a
different assembly with a different identity. The runtime cannot satisfy the
pre-compiled sink's reference, and the type-check fails.

This is not a limitation that can be patched. The only supported path for
pre-compiled sinks is to run the real Serilog assembly alongside Herald and
bridge the two pipelines at the `ILogger` level.

## Wire path (implementation notes)

`WriteTo.Sink(userSink)` does three things:

1. Calls `QuickLogBuilder.WithNullSink()` to emit a `"null"` entry in the Herald
   JSON pipeline config. This is the config hook the runtime resolves to decide
   which provider to instantiate for the sink slot.
2. Registers a `SerilogSinkAdapter` (kind = `"null"`) as a custom provider. It
   overrides the built-in `NullLogSinkProvider` for this pipeline via
   last-write-wins provider registry semantics.
3. On pipeline build, the runtime resolves the `"null"` sink slot to
   `SerilogSinkAdapter`, which creates a `SerilogUserLogger` that fans each
   native `MMP.Herald.Events.LogEvent` through the P1 mirror to every
   registered `ILogEventSink`.

Multiple `WriteTo.Sink()` calls on the same builder share one `SerilogSinkAdapter`
instance and one `"null"` slot. The adapter's internal list grows; the fan-out
happens inside `SerilogUserLogger.Log`.

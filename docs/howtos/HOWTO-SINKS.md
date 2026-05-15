# Sinks — Herald.OSS

How to wire sinks into a Herald.OSS pipeline. The three shapes most
adopters reach for, in order of complexity:

1. **Bridge sinks** — a plain `ILogger` your code controls. The fastest
   path to capture events for tests, custom routing, or quick
   integration with another logger.
2. **Built-in sink providers** — console, channel, audit. Available out
   of the box, configured through `With*` builder extensions.
3. **Custom sink providers** — first-class, kind-keyed sinks that show
   up in the JSON config and survive hot reload.

This doc covers all three, plus how multi-tenancy works structurally
(by wiring) in Herald.OSS.

## Bridge sinks

`WithBridge(ILogger target)` forwards every event the pipeline accepts
into a target `ILogger` your code owns. Use this for:

- Capturing events into a `List<string>` or `ConcurrentBag<string>` in
  tests.
- Forwarding events into another logger framework (e.g. an existing
  `Microsoft.Extensions.Logging.ILogger`).
- Custom downstream routing — your bridge can fan out to multiple
  internal destinations.

```csharp
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using MMP.Herald.Events;
using MMP.Herald.Quick;

public sealed class CapturingLogger : ILogger
{
    public ConcurrentBag<string> Messages { get; } = new();

    public void Log(LogEvent logEvent) => Messages.Add(logEvent.Message);

    public ValueTask LogAsync(LogEvent logEvent, CancellationToken ct = default)
    {
        Messages.Add(logEvent.Message);
        return ValueTask.CompletedTask;
    }
}

var captured = new CapturingLogger();

var herald = QuickLogBuilder.Create()
    .WithBridge(captured)
    .WithMinimumLevel("trace")
    .BuildAndCommit();

herald.Logger.Info(LogCategory.App, "captured via bridge");
// captured.Messages now contains "captured via bridge"
```

Bridges are not kernel-eligible. Events going through a bridge take the
chain path, not the kernel fast path. For per-event nanosecond cost,
implement `IKernelSink` (covered below).

## Built-in sinks

Console sink:

```csharp
var herald = QuickLogBuilder.Create()
    .WithConsoleSink()
    .WithMinimumLevel("info")
    .BuildAndCommit();
```

Channel sinks (named output streams):

```csharp
var herald = QuickLogBuilder.Create()
    .WithConsoleSink()
    .WithChannelSink("combat", combatWriter)
    .WithChannelSink("network", networkWriter)
    .BuildAndCommit();
```

Audit sinks bypass minimum-level filtering — every event reaches the
audit destination regardless of the pipeline's floor:

```csharp
var herald = QuickLogBuilder.Create()
    .WithConsoleSink()
    .WithAuditSink(auditWriter)
    .WithMinimumLevel("warn")    // audit still sees trace/debug/info
    .BuildAndCommit();
```

File, HTTP, OTLP, Elasticsearch, Slack, and other backends ship as
separate `MMP.Herald.Sinks.*` packages. Adding the package
auto-registers its providers into the process-wide
`LogSinkProviderRegistry.Default` — no explicit wireup needed.

## Custom sink providers

A custom `ILogSinkProvider` is a first-class participant: the provider
has a unique `SinkKind`, the sink shows up in the exported JSON config,
and hot reload rebuilds it like any built-in sink.

```csharp
using MMP.Herald.Configuration.Runtime;
using MMP.Herald.Levels;
using MMP.Herald.Output.Rendering;
using MMP.Herald.Routing;

public sealed class MetricsSinkProvider : ILogSinkProvider
{
    public string SinkKind => "metrics";

    public ILogger CreateSink(
        LoggingRuntimeSinkDefinition definition,
        ILogLevelRegistry levelRegistry,
        ILogOutputTransformerRegistry transformerRegistry)
    {
        // Read sink-specific config from definition.Properties.
        // Build whatever ILogger your sink needs.
        return new MetricsSink(/* ... */);
    }
}

var herald = QuickLogBuilder.Create()
    .WithCustomSinkProvider(new MetricsSinkProvider())
    .WithConsoleSink()
    .BuildAndCommit();
```

Custom providers are scoped to the builder that registers them. They do
not leak into other builders, and they do not mutate the process-wide
`LogSinkProviderRegistry.Default`. If two builders need the same custom
provider, each builder calls `WithCustomSinkProvider(...)` independently.

### Optional: implement `IKernelSink` for zero-allocation dispatch

For sinks on the hot path, additionally implement `IKernelSink`:

```csharp
using MMP.Herald.Pipeline.Kernel;

public sealed class FastMetricsSink : ILogger, IKernelSink
{
    public void Log(in LogEventBuffer buffer)
    {
        // Consume the stack-allocated event directly.
        // Do NOT retain the buffer past this call.
    }

    public void Log(LogEvent logEvent) { /* fallback path */ }
}
```

When every sink in a route set implements `IKernelSink`, the kernel
fans out without materializing a heap `LogEvent`. The one-allocation
boundary cost is paid only when at least one downstream sink lacks the
interface.

### Auto-wrap: legacy sinks keep the kernel active

A sink that does not implement `IKernelSink` does not disqualify the
pipeline. The factory wraps each such sink in `MaterializingKernelSink`
at build time; the kernel fast path still activates, and the wrapped
sink receives a heap `LogEvent` on every emit. Native `IKernelSink`
sinks in the same pipeline keep their zero-allocation path.

Two adopter signals affect the wrap cost:

- A sink that implements `IKernelSink` directly is fastest — the kernel
  fans out a `LogEventBuffer` straight to it, no boundary allocation.
- A sink that implements `IStructuredOnlySink` (a marker that says "I
  never read `LogEvent.Message`") gets auto-wrapped but skips the
  message render step. Pays the heap `LogEvent` allocation but no
  string-render cost. JSON sinks, OTLP exporters, and the null sink
  are the canonical examples.
- A sink that implements neither gets auto-wrapped and renders the
  message at the boundary so the sink sees the same rendered text it
  would have seen on the chain path.

Inspect `QuickLogResult.KernelDiagnostic` to see whether the kernel
activated and which sinks (if any) got auto-wrapped:

```csharp
var result = builder.BuildAndCommit();
var diag = result.KernelDiagnostic;
if (diag is not null && diag.LegacySinks.Count > 0)
{
    foreach (var sink in diag.LegacySinks)
    {
        Console.WriteLine($"Auto-wrapped sink #{sink.Index} ({sink.TypeName})");
    }
}
```

Use the diagnostic to find sinks worth upgrading: implement
`IKernelSink` on the sinks you can change, or claim
`IStructuredOnlySink` when the sink genuinely doesn't read rendered
text.

## Multi-tenancy

Multi-tenancy in Herald.OSS is structural: each tenant builds its own
pipeline. Pipelines do not share sinks, so isolation is a property of
how the system is wired — not a runtime check.

```csharp
var tenantA = QuickLogBuilder.Create()
    .WithBridge(tenantASink)
    .WithMinimumLevel("info")
    .BuildAndCommit();

var tenantB = QuickLogBuilder.Create()
    .WithBridge(tenantBSink)
    .WithMinimumLevel("debug")
    .BuildAndCommit();

tenantA.Logger.Info(LogCategory.App, "for A only");
tenantB.Logger.Debug(LogCategory.App, "for B only");
```

`tenantASink` only ever sees events from `tenantA.Logger`. The two
pipelines have independent minimum levels, independent sink references,
and independent hot-reload state. Disposing one does not affect the
other.

For tenant-routing within a single pipeline (one builder, multiple
tenants distinguished by event property), put a tenant-aware predicate
on each sink's route. That keeps the dispatch path fast and the tenant
boundary visible in the JSON config.

## Sink resolution order

When the pipeline resolves a sink kind to a provider, it walks two
locations in order:

1. **Local set** — providers registered on this builder via
   `WithCustomSinkProvider(...)`.
2. **Fallback registry** — the process-wide
   `LogSinkProviderRegistry.Default`, auto-populated by every
   `MMP.Herald.Sinks.*` package on assembly load.

Local entries take precedence on collision. A builder that registers a
custom `console` provider overrides the built-in `ConsoleSinkProvider`
for that builder only.

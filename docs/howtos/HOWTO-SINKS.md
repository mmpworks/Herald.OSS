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
fans out without materializing a heap `LogEvent`. Every built-in
Herald.OSS sink (console, file, JSON, null, archive, ring-buffer,
SSE capture, channel) implements both `ILogger` and `IKernelSink`, so
default pipelines take the kernel fast path automatically.

### What if a custom sink reads `LogEvent.Message`?

Kernel-path `LogEventBuffer` carries the template and properties but
**not** the rendered message — the accept path skips that work to stay
zero-allocation. Sinks that genuinely need the rendered text (file
writers with text templates, SSE broadcasters, NDJSON archives, etc.)
materialize a heap `LogEvent` at their own boundary using the
`KernelBufferAdapter` helper:

```csharp
using MMP.Herald.Events;
using MMP.Herald.Pipeline.Kernel;

public sealed class MyTextFileSink : ILogger, IKernelSink
{
    public void Log(LogEvent logEvent)
    {
        // existing heap-event path
        WriteLine(logEvent.Message);
    }

    public void Log(in LogEventBuffer buffer) =>
        Log(KernelBufferAdapter.MaterializeAndRender(in buffer));
}
```

`MaterializeAndRender` calls `buffer.ToLogEvent()` and, when
`LogEvent.Message` is empty, renders it from the template using the
same `MessageTemplateParser` the chain path uses. The sink sees the
same rendered text it would have seen on the chain path.

A sink that does **not** read `LogEvent.Message` (a structured JSON
writer, an OTLP exporter, a sink that only inspects properties)
should skip `MaterializeAndRender` entirely and consume the buffer in
place — that's the pure zero-allocation path:

```csharp
public void Log(in LogEventBuffer buffer)
{
    // Read template + properties directly from the buffer.
    // No allocation, no rendered message needed.
    WriteJson(buffer.MessageTemplate, buffer.CompactProperties);
}
```

### Async sinks: network, disk, anything that can suspend

`IKernelSink` has a second method that mirrors the sync entry:

```csharp
ValueTask LogAsync(in LogEventBuffer buffer, CancellationToken ct = default);
```

This is the buffer-shaped async entry. Its default body is a sync
forward to `Log(in buffer)` and returns `ValueTask.CompletedTask` —
sinks that don't perform real I/O inherit it and pay no async cost.
Sinks that genuinely need to suspend on I/O (HTTP, OTLP, file
rotation, network publish) override this method.

**The capture-before-await rule.** C# forbids a `ref struct` from
crossing an `await`. The buffer cannot survive into the async
continuation. The method body cannot be marked `async` when its
parameter is `in LogEventBuffer` (compiler error CS4012). The
sync-outer / async-inner pattern is the load-bearing idiom:

```csharp
public sealed class MyHttpSink : HeraldSinkBase
{
    private readonly HttpClient _http;
    private readonly Uri _endpoint;

    public override void Log(LogEvent logEvent)
    {
        // Sync path: post on the calling thread.
        using var req = BuildRequest(logEvent);
        _http.Send(req);
    }

    public override ValueTask LogAsync(in LogEventBuffer buffer, CancellationToken ct = default)
    {
        // 1) Capture the buffer's contents synchronously. The ref struct
        //    cannot survive past this point.
        var heap = KernelBufferAdapter.MaterializeAndRender(in buffer);

        // 2) Hand off to a normal async helper. Buffer is no longer
        //    referenced.
        return SendAsync(heap, ct);
    }

    private async ValueTask SendAsync(LogEvent ev, CancellationToken ct)
    {
        using var req = BuildRequest(ev);
        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
    }
}
```

The `LogAsync` outer method is NOT marked `async` — it returns a
`ValueTask` from `SendAsync`. The state machine is generated for
`SendAsync` only. If `_http.SendAsync` completes synchronously
(cached connection, immediate failure), `ValueTask` stays a struct
and the call allocates nothing.

**Forward-compat note.** No kernel call site dispatches through
`LogAsync(in buffer, ct)` at v0.x. The pair (sync + async) locks the
v1.0 contract shape in source today so buffer-aware drain decorators
can wire through to it later. Adopters wanting real async delivery
today wrap their pipeline with `WithAsync()`; the resulting
`AsyncLogger` decorator drains via `ILogger.LogAsync(LogEvent, ct)`
on the chain path.

### Kernel eligibility diagnostics

A pipeline can still fall back to the chain path when the
configuration disqualifies the kernel — deferred rendering enabled,
hot reload on, dynamic level policy, an enricher that isn't
`IKernelEnricher`, a custom decorator that isn't `IKernelDecorator`,
or a routed sink that doesn't implement `IKernelSink`. Inspect
`QuickLogResult.KernelDiagnostic` after build to see which rule
disqualified the pipeline:

```csharp
var result = builder.BuildAndCommit();
var diag = result.KernelDiagnostic;
if (diag is { KernelEligible: false })
{
    Console.WriteLine($"Kernel fast path disabled: {diag.RejectionReason}");
}
```

The rejection reason names the specific rule that failed (e.g.
`"sink 2 (MyTextFileSink) does not implement IKernelSink"`), so it's
straightforward to find and fix the disqualifying sink or decorator.

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

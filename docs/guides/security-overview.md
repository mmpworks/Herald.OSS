# Security overview

What Herald.OSS protects, what it does not, and where the line is.
The point of this guide is to be honest about both so an operator
can wire the rest of the defences from a place of knowing exactly
what's underneath them.

Herald.OSS is the open-source upstream. It carries the core
correctness defences — the things that protect events as they flow
through the pipeline. It does **not** carry the in-process
injection defence; that lives in Herald.Core. The split is
explicit and called out below.

## Trust boundaries

A log event travels across a few boundaries. Each one is a chance
for something to go wrong if nobody's looking.

```mermaid
flowchart LR
    App[Application code]
    Config[JSON config file]
    Pipeline[Herald.OSS pipeline]
    External[External destinations<br/>HTTP, TCP, UDP, files]
    Tenants[Other tenants in the<br/>same host process]

    App -->|level, category,<br/>template, properties<br/>(often user-derived)| Pipeline
    Config -->|secrets, paths,<br/>endpoint URLs| Pipeline
    Pipeline -->|formatted events| External
    Pipeline -->|separate builder<br/>per tenant| Tenants

    style App fill:#ede7f6,stroke:#7c3aed
    style Config fill:#fff4c2,stroke:#c9a227
    style External fill:#d6f5d6,stroke:#3a3
    style Tenants fill:#d6f5d6,stroke:#3a3
```

Four boundaries matter for the OSS surface:

1. **Application → pipeline.** User-derived property values land
   here. Most common content-injection vector.
2. **Config → pipeline.** JSON is read at bootstrap and on hot
   reload. Values may include endpoint URLs and credentials the
   operator supplied.
3. **Pipeline → external destinations.** Sinks talk to remote
   services or write to disk. Whatever leaves the pipeline must be
   well-formed.
4. **Tenant ↔ tenant.** When two tenants share a host process,
   one tenant's pipelines must not see events meant for the other.

The fifth boundary — **in-process → sink** — is **not** defended
in Herald.OSS. Any code in the same process that obtains a sink
reference can construct a `LogEvent` and call `sink.Log(...)`
directly, bypassing every filter and redactor in the pipeline.
That gap is closed in Herald.Core via the GenSource provenance
gate (see [below](#what-herald-oss-does-not-defend)).

## What Herald.OSS defends

### JSON output encoding

A property value containing a raw newline (`0x0A`), a BEL
character (`0x07`), or any C0 control character would break
downstream JSON parsers if it appeared unescaped. A CLEF parser
splitting on newlines drops half the event. A terminal eats the
BEL and corrupts the operator's view.

`JsonEscaper.Escape` at `src/Services/JsonEscaper.cs` handles the
full C0 range (0x00–0x1F) plus 0x7F per **RFC 8259**. Shorthand
forms (`\b`, `\f`, `\n`, `\r`, `\t`) for the common ones; the
remainder emitted as `\uXXXX`. Every JSON formatter path routes
through it. `LineSanitizer` at `src/Output/Writers/LineSanitizer.cs`
handles the same character classes for non-JSON destinations.

**What this guarantees.** Whatever value reaches the formatter
survives as legal JSON. A user-derived property cannot break the
output's structure.

**What this does not cover.** Content-level controls — masking
credit cards, removing passwords — are a separate defence. See
redaction below.

### Source-generated JSON parsing

The config front door uses a source-generated
`JsonSerializerContext` named `HeraldJsonContext` at
`src/Configuration/HeraldJsonContext.cs`. The shape of every JSON
record is known at compile time. No reflection at runtime.

That matters for two reasons:

- **AOT publish stays clean.** No `IL2026` or `IL3050` warnings
  bubble out of the config path. See
  [`aot-and-trimming.md`](aot-and-trimming.md).
- **Trim-safety.** A trimmer can safely remove unused JSON
  contracts; the generator emits exactly what's reachable.

### Property-level redaction

A logger captures a request body as a property. The body contains
a credit-card number. Without rebuilding the rendered message,
the redactor could mask the property value but leave the
credit-card number in the formatted `Message` text — the sink
writes the secret straight to disk.

Two redaction shapes ship in Herald.OSS:

- `FastPathRedactor` (`native/dotnet/Pipeline/Kernel/FastPathRedactor.cs`)
  — runs at the dispatch boundary on the kernel path. Allocation-free
  when no rule fires. Wired via `QuickLogBuilder.WithFastRedaction(...)`.
- `RedactionProcessor` / `RedactionHelper` in
  `src/Output/Rendering/` and the chain-side
  `CompiledRedactionProcessor` for events that took the heap-event
  path.

Both shapes **re-render the message after redaction**. The
redacted property value is the value that appears in the rendered
`Message` field. A secret captured as a property cannot survive
into the text output, even if the message template originally
referenced it.

```csharp
var herald = QuickLogBuilder.Create()
    .WithFastRedaction(
        CompiledRedactionRule.Mask(propertyName: "Password"),
        CompiledRedactionRule.Mask(propertyName: "ApiKey"))
    .WithFileSink("logs/app.ndjson")
    .BuildAndCommit();
```

**What this guarantees.** Configured patterns mask property values
and the message text re-rendered from those properties. Both
representations are scrubbed together.

**What this does not cover.** Patterns the operator did not
configure. Redaction is a content control, not a discovery tool —
it acts on rules you give it.

### Registry thread safety

The shared registry that holds sink providers, pipeline
registrations, and component dispatch state is backed by
`ConcurrentDictionary` end-to-end. Concurrent registration from
multiple threads cannot corrupt the bucket chain. Readers and
writers stay lock-free.

`HeraldRegistry` at `src/Quick/HeraldRegistry.cs` uses a nested
`ConcurrentDictionary<string, ConcurrentDictionary<string, ...>>`
keyed on (tenant, pipeline name). Atomic publish uses
`Interlocked.CompareExchange` so a reader either sees a complete
registration or no registration — never a torn intermediate state.

### Structural tenant isolation

Herald.OSS does not enforce a runtime tenant gate. Tenant
isolation is **structural**: each tenant builds its own
`QuickLogBuilder` and gets its own pipeline.

```
   Tenant A's code        Tenant B's code
        │                        │
        ▼                        ▼
  QuickLogBuilder          QuickLogBuilder
  (Tenant-A pipeline)      (Tenant-B pipeline)
        │                        │
        ▼                        ▼
    Tenant-A sinks            Tenant-B sinks
   (file://A.ndjson)         (file://B.ndjson)
```

A pipeline reaches only the sinks its builder configured. A sink
reference belongs to one tenant. The registry is keyed on
`(tenant, pipeline)` so a tenant can't enumerate or address
another tenant's pipelines through the public API.

`HeraldTenantScope` at `src/Quick/HeraldTenantScope.cs` uses
`AsyncLocal<string?>` with a lexical `using` block — the scope
restores on exit and cannot leak across `await` boundaries.

**What this guarantees.** A tenant's events reach the sinks that
tenant's pipeline wired, and no others. The pipelines do not share
state.

**What this does not cover.** Two tenants that *deliberately*
share a sink reference (because the operator wants shared egress
infrastructure) will both write to that sink. The shared sink is
itself responsible for any per-tenant routing it does — the
pipeline can't enforce a sharing rule it was told to allow.

### FFI null safety

Herald.OSS's interop bridges to native parsers null-check inputs
**before** crossing the managed/native boundary.

`src/Interop/RustTemplateParse.cs` calls
`ArgumentNullException.ThrowIfNull` on the template and properties
before entering the `fixed` block. A null reference cannot become
a null pointer in native code. The managed caller gets a clean
exception; the native side never sees the null.

**What this guarantees.** A null input fails predictably at the
managed boundary instead of producing undefined behaviour inside
the native parser.

## What Herald.OSS does not defend

### In-process sink injection

Anywhere a piece of code has a sink reference, it can construct
a `LogEvent` and call `sink.Log(...)` directly:

```csharp
// Hypothetical hostile-or-careless plugin in the host process:
var rogueEvent = new LogEvent(
    TimeUtc: DateTimeOffset.UtcNow,
    Level: KnownLogLevels.Info,
    Category: LogCategory.App,
    MessageTemplate: "user password = {Password}",
    Message: $"user password = {secret}",
    Properties: LogEvent.EmptyProperties,
    Context: LogEvent.EmptyContext);
sink.Log(rogueEvent);   // bypasses every filter and redactor
```

That call lands at the sink's writer with the secret intact.
Every filter, redactor, rate limiter, and level gate the pipeline
builder set up sits **upstream** of the sink — the rogue call
routes around all of them.

**Herald.OSS has no defence against this.** It is a deliberate
omission. The provenance-gate machinery that stops this attack —
per-event `GenSource` stamps, per-sink gates that validate the
stamp, an external-source registrar that issues derived keys —
lives in **Herald.Core** (the commercial distribution).

If your threat model includes plugins or in-process callers you
do not fully trust, take a look at Herald.Core. The OSS pipeline
is designed for hosts where every caller is in your own
codebase.

### Server-side concerns

Herald.OSS does not ship an HTTP surface. Defences that belong at
the HTTP boundary — rate limiting, path-traversal validation,
scope-based authorization — live in `Herald.Server` (a separate
package) and travel with whichever transport you put in front of
the pipeline. If you embed Herald.OSS into your own ASP.NET host,
you wire those at your host's edge.

### Reflection-based attacks

Any in-process attacker with reflection privileges can read
internal fields, call private methods, and step around managed
visibility. The defences in this document target honest mistakes
and unprivileged in-process code, not adversaries with full
reflection access. If a hostile process can run reflection inside
your AppDomain, the threat model is at a different level than
this defence boundary.

### Operator-supplied secrets in config

If you commit a webhook URL with embedded credentials, Herald
cannot tell. The pipeline reads what the config says. Credential
handling at the config layer (Key Vault references, managed
identity, environment variables) is an operator choice and lives
outside the OSS scope.

### Resource exhaustion under flood

A caller logging at an unbounded rate fills the async queue and
pressures the GC. The async logger's drop strategy bounds the
damage to the pipeline, but the underlying flood is an
application-level decision. Herald.OSS reports drops via
`IPipelineDropSink` so an operator can see when a flood is
happening; it doesn't pretend to stop one upstream.

## Summary

| Defence | Status in OSS | Where |
|---|---|---|
| JSON output encoding (RFC 8259) | Defended | `src/Services/JsonEscaper.cs` |
| Source-generated JSON config | Defended | `src/Configuration/HeraldJsonContext.cs` |
| Property + message redaction | Defended | `FastPathRedactor`, `RedactionProcessor` |
| Registry thread safety | Defended | `src/Quick/HeraldRegistry.cs` |
| Tenant isolation (structural) | Defended | `src/Quick/HeraldTenantScope.cs` |
| FFI null safety | Defended | `src/Interop/RustTemplateParse.cs` |
| Drop attribution | Reported | `src/Metrics/IPipelineDropSink.cs` |
| In-process sink injection | **Not defended in OSS** | (lives in Herald.Core: GenSource gate) |
| Rate limiting | Out of scope (transport) | (lives in Herald.Server) |
| Path traversal | Out of scope (transport) | (lives in Herald.Server) |
| Operator-supplied secrets | Operator responsibility | — |

## Where to look next

- [`architecture.md`](architecture.md) — the layered shape that
  makes structural isolation work without a runtime gate.
- [`aot-and-trimming.md`](aot-and-trimming.md) — the AOT side of
  the JSON-config defence.
- [`building-sinks.md`](building-sinks.md) — what a custom sink
  is responsible for (and what the pipeline already handled).
- `src/Services/JsonEscaper.cs` — the escape table.
- `src/Output/Writers/LineSanitizer.cs` — the non-JSON line
  sanitizer.
- `src/Quick/HeraldRegistry.cs` — registry + tenant-keyed dispatch.
- `src/Quick/HeraldTenantScope.cs` — AsyncLocal tenant scope.
- `FORK_SCOPE.md` — the explicit list of what's stripped from
  Herald.Core to produce this OSS distribution. The provenance-gate
  removal is documented there with the same honesty.

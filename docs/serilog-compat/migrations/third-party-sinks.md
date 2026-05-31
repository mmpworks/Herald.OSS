---
gap-id: third-party-sinks
serilog-surface: pre-compiled community sinks (Seq / MSSql / Datadog / long tail)
herald-status: hard-wall (strong-name identity; no drop-in path)
population-rank: high
regression-test-id: G-SINK-WALL.1
---

<!-- Heather T-H2: STANDALONE companion. HARD WALL. Must state plainly there is no
     drop-in path and link the parity-audit wall. Must NOT imply a workaround that
     does not exist. -->

# Migrating Off Pre-Compiled Community Sinks

## There is no drop-in path

Pre-compiled community sinks — `Serilog.Sinks.Seq`, `Serilog.Sinks.MSSqlServer`, `Serilog.Sinks.Datadog`, and the rest of the long tail — cannot bind to Herald's `Serilog.*` shim. This is a structural boundary, not a deferred feature.

The realistic options are described in [The honest alternatives](#the-honest-alternatives) below.

## Why (the identity wall)

> Third-party and community Serilog sinks (`Serilog.Sinks.Seq`, `.Sinks.MSSqlServer`, `.Sinks.Datadog`, and the long tail) cannot bind to the Herald `Serilog.*` shim. Each is compiled against `Serilog, PublicKeyToken=24c2f752a8e58a10` and depends on the real strong-named `Serilog.ILogEventSink`/`Serilog.Core` types. The shim is unsigned and exports types of a different assembly identity; the CLR will not satisfy the sink's `Serilog` reference with the shim. Referencing such a sink transitively loads the real `Serilog.dll`, producing duplicate `Serilog.*` types (CS0433 at compile, or InvalidCastException at runtime). This is a structural identity wall, not a deferral. Herald's own equivalents (Console/File/HTTP/TCP/UDP/Elasticsearch/OTLP/Null) cover the popular sinks; Seq and the long tail are named gaps with no drop-in path absent a strong-named signing key we do not have and will not spoof.

Source-compiling an adapter wrapper does not resolve this. The adapter class exists in your codebase and recompiles cleanly — but the NuGet package it wraps still pulls in the real `Serilog.dll` as a transitive dependency, which then collides with the Layer-2 shim and produces CS0433. The identity wall is on the pre-compiled NuGet package, not on your adapter class.

For the full engineering statement see [parity-audit.md § Third-party sinks — the identity wall](../parity-audit.md).

## The honest alternatives

**Alternative 1 — Use the Herald equivalent sink.**

For the popular targets, Herald ships a built-in equivalent. See [Popular-target mapping](#popular-target-mapping) below. Console, File, HTTP, Elasticsearch, and OTLP cover the majority of production setups.

**Alternative 2 — Route events to the target via an OTLP or HTTP sink.**

Seq, Datadog, and many other backends accept log events over OTLP or HTTP. If the backend supports OTLP ingestion, use Herald's OTLP sink (`WriteTo.OpenTelemetry(endpoint)`). If it exposes an HTTP ingestion API, use the Herald HTTP sink and configure the endpoint and any required authentication.

This is not an identity-level drop-in — the wire format may differ. Check your backend's ingestion documentation. For many teams this is the practical path.

**Alternative 3 — Keep that one logging path on real Serilog in a separate process.**

If the community sink is non-negotiable and has no equivalent OTLP/HTTP path, run it in a separate process. The Herald application emits events over HTTP, TCP, or OTLP; a separate lightweight process receives them and forwards to the community sink via real Serilog. This keeps the two assemblies in separate processes — no identity collision, no CS0433.

This is the most work. It is the right call when the community sink carries business-critical behavior (specific Seq query format, MSSql schema, etc.) that cannot be replicated behind a generic HTTP/OTLP path.

## Popular-target mapping

| Community sink | Herald equivalent | Configuration change |
|---|---|---|
| `Serilog.Sinks.Console` | Built-in Console sink | `WriteTo.Console()` — same verb |
| `Serilog.Sinks.File` | Built-in File sink | `WriteTo.File(path)` — same verb |
| `Serilog.Sinks.Http` | Built-in HTTP sink | `WriteTo.Http(url)` — same verb |
| `Serilog.Sinks.Elasticsearch` | Built-in Elasticsearch sink | `WriteTo.Elasticsearch(url)` |
| `Serilog.Sinks.OpenTelemetry` | Built-in OTLP sink | `WriteTo.OpenTelemetry(endpoint)` |
| `Serilog.Sinks.Seq` | No Herald equivalent | Use OTLP or Elasticsearch, or keep on a separate path |
| `Serilog.Sinks.MSSqlServer` | No Herald equivalent | Use Herald HTTP sink → SQL ingestion layer, or keep on a separate path |
| `Serilog.Sinks.Datadog` | No Herald equivalent | Use OTLP export to Datadog |

For the full mapping with configuration details see [migration-runbook.md § Community sink gaps — Herald equivalents](../migration-runbook.md).

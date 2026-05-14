# Fork Scope — Herald.OSS v0.1.0

This document records exactly what was stripped from Herald.Core to
produce Herald.OSS, and why. Treat it as the authoritative reference
when reconciling diffs against upstream Herald.Core.

Snapshot date: 2026-05-14
Source commit (Herald.Core): `98d23fd` (post-Option-E redaction patch)

## What stays the same

- License: Apache 2.0 (identical to Herald.Core's `LICENSE`).
- Root namespace: `MMP.Herald.*`. Type names are unchanged.
- Kernel + pipeline shape: `StructuredLogger`, `LogEventBuffer`,
  `IKernelSink`, the four accept-path call shapes (typed-args, params-span,
  interpolated, level-bound).
- Source generators that produce non-gated API surface
  (`TypedArgsOverloadGenerator`).
- Redaction fast-path (`FastPathRedactor`, `RedactionHelper`).
- The kernel-aware redactor + enricher hooks on `StructuredLogger`.

## What was stripped

### 1. Edition machinery

Removed: the runtime types that mark a build as Community / Pro /
Enterprise and gate features by edition.

| Path | Reason |
|---|---|
| `src/HeraldEdition.cs` | Edition enum + accessors. Not meaningful in a single-edition OSS distribution. |
| `src/HeraldEditionGate.cs` | Runtime gate that rejects gated APIs at non-matching editions. Always-allow in OSS. |
| `src/Licensing/` | Licensing infrastructure that backs the gate. The OSS build has no licence. |
| `tests/HeraldEditionGateTests.cs` | Tests for the removed gate machinery. |

### 2. Provenance gate

Removed: the per-event provenance stamp that lets paid sinks reject
events from non-paid pipelines.

| Path | Reason |
|---|---|
| `src/Pipeline/Kernel/GenSourceGatedSink.cs` | The sink decorator that enforces the gate. |
| `src/Pipeline/Kernel/ExternalSourceRegistrar.cs` | The external-caller registration surface. |

Plus references to `_genSource` / `GenSource` removed from these
consumers (they kept the surrounding logic; just the gate field
dropped):

- `src/Addons/GamePerformance/HotPathLogger.cs`
- `src/Addons/Reduction/WindowedMeanLogger.cs`
- `src/Bootstrap/LoggingBootstrap.cs`
- `src/Bootstrap/LoggingBootstrapResult.cs`
- `src/Events/DeferredLogEventFactory.cs`
- `src/Events/ILogEventFactory.cs`
- `src/Events/LogEvent.cs`
- `src/Events/LogEventFactory.cs`
- `src/Pipeline/Kernel/KernelCompiler.cs`
- `src/Pipeline/Kernel/LogEventBuffer.cs`
- `native/dotnet/Pipeline/DefaultLogPipelineFactory.cs`
- `native/dotnet/Pipeline/StructuredLogger.cs`
- `native/dotnet/Routing/DefaultLogSinkRouterFactory.cs`

In each case the field, parameter, or call-site argument is dropped;
constructors lose one parameter; default-value overloads collapse.

### 3. Source-gen analyzer that emits gate checks

The HERALDxxx analyzers in `Modules/Generators/` warn callers about
gated APIs at compile time. In Herald.OSS there are no gated APIs, so
the analyzer produces no useful warnings. The Herald.OSS build does
not reference the gate-enforcement generator output. The
`TypedArgsOverloadGenerator` (non-gate) is kept.

### 4. Distribution hardening

Removed: the IP-protection step that obfuscates type/member names in
the Pro / Enterprise release pipeline.

| Path | Reason |
|---|---|
| `obfuscar.xml` | Obfuscar configuration template. |
| `obfuscar.resolved.xml` | Per-target-framework resolved config. |
| `build.sh` Obfuscar invocation | The build script's obfuscation step is removed. |
| `bin/Release/{Edition}/.../protected/` output path | The hardened-output path doesn't exist. |
| `promote.sh` / `promote.ps1` | The release promotion scripts assume the paid release pipeline. |

### 5. Pro / Enterprise-only addons

Removed: subdirectories under `src/Addons/` that exist only because a
paid edition is configured to include them.

| Path | Disposition |
|---|---|
| `src/Addons/ManagementApi/` | Management API ships in Pro. Removed from OSS. |

Addons that exist in both Community and Pro/Enterprise are kept;
the v0.1.0 selection prioritises code that lives at the Community
boundary.

### 6. Documentation

Removed: all of `Modules/Core/docs/`, `CLA/`, `manifests/`, the
benchmark history under `docs/benchmarks/`, and the `Herald/` Obsidian
vault.

Rationale: the documentation is in active flux, much of it discusses
edition gating + paid-distribution mechanics, and the user-driven
strategy is to seed Herald.OSS's docs deliberately rather than
inheriting them wholesale. Selected docs will be ported toward v1.0.0
once Herald.OSS source is settled.

### 7. Tests + benchmarks

Tests: copied selectively. `HeraldEditionGateTests.cs` and any tests
referencing `GenSource` are dropped. Other tests come over as-is and
must build green against the stripped source.

Benchmarks: deferred to v1.0.0. The pipeline + kernel performance
characteristics are documented in Herald.Core's `docs/benchmarks/`
runs; that data applies to Herald.OSS modulo the bits removed above.

## What's preserved in `src/Addons/`

(Filled in after Phase 2 — Phase 1 lists scope only.)

## Diff against Herald.Core

Once Phases 2–4 land, a `compare-vs-core.md` artifact will record the
file-level diff between Herald.OSS v0.1.0 and Herald.Core@`98d23fd`.
That diff is the auditable evidence for what "fork minus paid bits"
actually means at the byte level.

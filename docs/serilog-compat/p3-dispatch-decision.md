# OD-2: P3 Dispatch Decision

**Date:** 2026-05-30
**Tasks covered:** Task 10 (architecture guard)
**Pre-mortem risk closed:** CRIT-4

## The question

When a developer configures a sink via the Serilog-compat layer (`WriteTo.Console()`,
`WriteTo.File()`, etc.), which formatter runs? Could Herald's native
`OutputTemplateFormatter` silently fire instead of the user's formatter?

## Decision

### `WriteTo.Console(ITextFormatter formatter)` — user formatter via S3 bridge

This is the full-control path. The user supplies an `ITextFormatter`; it receives a
Serilog-shaped `LogEvent` mirror and writes to a `TextWriter`. Herald's console
styling pipeline is bypassed entirely. Wired in Task 9
(`TextFormatterConsoleSinkProvider` + `TextFormatterConsoleLogger`).

### `WriteTo.Console()` (no formatter) — Herald's default console rendering

No formatter is injected. The built-in console sink runs with Herald's own output
transformer. This is the existing Herald behavior and is acceptable for the
Serilog-compat layer: the Serilog drop-in contract does not mandate any specific
default renderer for the console case. Developers who want Serilog-style text output
pass a `MessageTemplateTextFormatter` (Task 7); developers who want Serilog CLEF
pass a `CompactJsonFormatter` or `RenderedCompactJsonFormatter` (Task 8).

### All other `WriteTo.*` verbs — Herald's native rendering

`WriteTo.File(path)`, `WriteTo.Http(url)`, `WriteTo.TCPSink(...)`, etc. do not
accept a formatter parameter. Herald's native rendering applies. This is acceptable
because those sinks produce structured JSON output — there is no text-grammar
mismatch to worry about. The developer can observe rendered text only on the console
path, which is exactly where the S3 bridge applies.

**Summary table:**

| Call form | Formatter | Acceptable? |
|---|---|---|
| `WriteTo.Console(ITextFormatter)` | User formatter via S3 bridge | Yes — full control |
| `WriteTo.Console()` | Herald default console renderer | Yes — Herald behavior |
| `WriteTo.File(path)` | Herald native rendering (JSON) | Yes — structured output |
| `WriteTo.Http(url)` | Herald native rendering (JSON) | Yes — structured output |
| `WriteTo.TCPSink(...)` | Herald native rendering (JSON) | Yes — structured output |
| `WriteTo.UDPSink(...)` | Herald native rendering (JSON) | Yes — structured output |
| `WriteTo.Elasticsearch(...)` | Herald native rendering (JSON) | Yes — structured output |
| `WriteTo.OpenTelemetry(...)` | Herald native rendering (JSON) | Yes — structured output |

## What CRIT-4 actually warned about

The pre-mortem CRIT-4 concern was: could `LoggerSinkConfiguration` accidentally
reference `MMP.Herald.Formatting.OutputTemplateFormatter` (Herald's native
formatter)? That type uses Herald's own template grammar and has no knowledge of
Serilog's `{:u3}` / `{:lj}` specifiers. If it appeared in the dispatch path it would
silently produce wrong output when developers expected Serilog-grammar rendering.

The compat layer does not reference `OutputTemplateFormatter` at all. The architecture
test in `tests/Serilog/Configuration/DispatchArchitectureTests.cs` enforces this at
the member-signature level. Any future change that adds a field or parameter of that
type to the compat assembly will fail the test immediately.

## What is NOT in scope for P3

- `outputTemplate:` parameter on `WriteTo.File`, `WriteTo.Console`, etc. — Serilog
  real API has this on most sinks. Herald's compat layer does not. This is a P8
  parity-audit item.
- Formatter injection for non-console sinks — not needed because those sinks emit
  structured JSON. Documented here as an explicit non-goal.

## Architecture test

`tests/Serilog/Configuration/DispatchArchitectureTests.cs` — two facts:

1. `Console_with_ITextFormatter_overload_exists_on_LoggerSinkConfiguration` — positive
   seam check. The bridge is wired.
2. `Compat_assembly_has_no_member_typed_as_OutputTemplateFormatter` — negative
   containment check. The native formatter has not leaked into the compat dispatch path.

Test approach used: member-signature walk (fields + method parameters + return types)
across all types in the compat assembly. Method-body inspection (`GetMethodBody()`)
was considered but rejected — it requires IL presence, only catches typed locals, and
is fragile across runtimes. Signature inspection is sufficient because any use of
`OutputTemplateFormatter` in the dispatch path must surface in at least one
field, parameter, or return type.

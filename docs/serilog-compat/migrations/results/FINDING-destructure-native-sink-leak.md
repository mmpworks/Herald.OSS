# HIGH / SECURITY — Destructuring redaction silently no-ops on native sinks (PII leak)

- **Found by:** Richard, 2026-06-01 (Wave 1, Ref4.Filtering migration)
- **Package:** MMP.Herald.Serilog 0.12.5 (Layer-1) — same code mirrored into Herald.OSS assembly
- **Severity:** HIGH. It is a redaction-bypass: a registered destructuring policy that
  strips a secret is silently ignored on the most common sink path, and the secret
  reaches the sink output. This violates the S5 security contract verbatim:
  "A no-op destructuring policy is a PII leak. Herald never silently drops your redaction work."

## Minimal repro (8 lines)

```csharp
using MMP.Herald.Serilog;
Log.Logger = new LoggerConfiguration().MinimumLevel.Debug()
    .Destructure.ByTransforming<Customer>(c => new { c.Name, c.Email })  // strips ApiKey
    .WriteTo.Console()                                                   // NATIVE sink
    .CreateLogger();
Log.Information("Customer {@Customer}", new Customer("Ada","ada@acme.test","sk_SECRET"));
Log.CloseAndFlush();
public sealed record Customer(string Name, string Email, string ApiKey);
```

- **Real Serilog output:** `{"Name": "Ada", "Email": "ada@acme.test"}`  (ApiKey stripped)
- **Herald 0.12.5 output:** `Customer { Name = Ada, Email = ada@acme.test, ApiKey = sk_SECRET }`  (SECRET LEAKED)

## Diagnosis (pinned)

The `SerilogDestructuringApplicator` is only invoked inside `SerilogSinkAdapter`
(`src/Serilog/Events/LogEvent.cs:172`, reached only via `WriteTo.Sink(userSink)` —
the custom-sink mirror-projection path). Herald-native sinks (`WriteTo.Console()`,
`WriteTo.File(...)`) receive the event through the native pipeline, which never consults
the Serilog-shaped destructuring applicator. So:

- `WriteTo.Sink(customSink)` + ByTransforming  → policy FIRES, secret stripped (verified OK).
- `WriteTo.Console()` / `WriteTo.File()` + ByTransforming → policy BYPASSED, secret leaks.

Verified both branches in a controlled repro. The custom-sink path redacts correctly;
the native-sink path does not.

## Why it matters

`WriteTo.Console()` + a redaction destructuring policy is the single most common shape for
this feature. The leak is silent (no error, no SelfLog) — exactly the failure mode S5 says
cannot happen. Any migrated app that relied on a destructuring policy for PII redaction and
writes to a Herald-native sink is leaking today.

## Scope / not-fixed-tonight

Not patched in this run: it is a shipped-package (0.12.5 / Herald.OSS assembly) change that
needs design + review + the Glenn/Max release lanes, not an overnight edit. The honest path:
- Apply the `SerilogDestructuringApplicator` on the native capture path too (at property
  capture time, before the event reaches ANY sink), so redaction is sink-independent.
- Until then, the migration playbook for destructuring-policy apps must state that redaction
  currently only holds on the `WriteTo.Sink(...)` path, and treat native-sink redaction as a
  known gap. (S5 worked-example needs this caveat.)

## Regression test (every-bug-becomes-a-regression-test)

- **ID:** REG-SERILOG-DESTRUCTURE-NATIVE-SINK
- **Class:** redaction-coverage (this is a CLASS bug — add a functionality suite, not one case).
- **Assert:** for EACH sink kind (console, file, custom WriteTo.Sink, and every native sink the
  compat layer exposes), a registered `Destructure.ByTransforming<T>` / `Destructure.With(policy)`
  that strips a field MUST keep that field out of the rendered output and out of the event's
  Properties. Drive with a sentinel secret string; grep the sink output for the sentinel; fail
  if present. The suite protects every downstream product that trusts Herald for redaction.

---

## RESOLVED (2026-06-02, Richard, branch feat/four-project-migration — local commits only)

Fixed per the prescribed direction: the destructuring policy is now applied on the
NATIVE capture path, at property-capture time, before the event reaches any sink.

- `SerilogDestructuringApplicator.TryRedactNative(object?, out object?)` runs the policy
  chain and returns a native-renderable redacted value (Structure→Dictionary,
  Sequence→List, Scalar→raw). Covers `ByTransforming` and raw `IDestructuringPolicy`.
- `SerilogLoggerAdapter` threads the applicator in (`CreateLogger` → internal `FromBuild`
  overload) and, in `BuildProperties`, substitutes the redacted value for `{@}`-mode holes
  a policy claims. Zero cost when no policy is registered (`HasPolicies` guard).

**Covered surface:** the Serilog `ILogger` interface + the static `Log` facade — every
real migration call site routes through `WriteCore` → `BuildProperties`. Verified the
exact FINDING repro (static `Log.Information("...{@Customer}...")` → `WriteTo.File`) keeps
the secret out of the file.

**Documented residual (pinned by test, not a migration leak):** the typed generic
`Information<T1>` overload on the *concrete* `SerilogLoggerAdapter` rides the kernel
compact path and is not covered for native sinks. It is unreachable through the Serilog
`ILogger` interface (which declares only `params object?[]` overloads) or the static
facade, so no real Serilog migration hits it. Closing it would require kernel-hot-path
work (the compact path deliberately never materializes a `LogEvent`), tracked separately.

**Regression suite REG-SERILOG-DESTRUCTURE-NATIVE-SINK** (7 tests) at
`tests/Serilog/Destructuring/NativeSinkRedactionRegressionTests.cs`: ByTransforming + raw
policy hold on custom `WriteTo.Sink` AND native file sink; static `Log` facade holds on
native file sink; no-policy control proves the suite catches leaks; typed-path boundary
pinned.

**Publish note:** fixes are local commits on `feat/four-project-migration` and are NOT
published. The packaged `MMP.Herald.Serilog` on NuGet (0.12.5) still carries the leak
until these ship.

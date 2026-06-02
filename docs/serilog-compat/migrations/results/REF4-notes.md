# Ref4.Filtering — Destructuring Policy + Expressions Filter (find-replace + boundaries)

**What it does.** The advanced case: a `Destructure.ByTransforming<Customer>` policy that
redacts an `ApiKey` secret, plus a `Serilog.Expressions` string-DSL filter
(`Filter.ByExcluding("RequestPath like '/health%'")`) that drops health-check noise. Inline-wired.

**Vehicle.** Find-replace — but this project is where the honest boundaries live.

**What migration surfaced (both measured, neither hidden).**

1. **No `.Filter` on the config chain.** Herald's `LoggerConfiguration` has no `.Filter`
   property, so `.Filter.ByExcluding("...")` does not compile. The string-DSL engine *does*
   ship (`Filter.ByExcluding(string)` / `Filter.ByIncludingOnly(string)` return an `ILogFilter`),
   which contradicts the stale `expressions-dsl.md` "hard wall" claim — the parser exists. The
   real gap is the fluent integration: there is no inline way to apply that `ILogFilter` from
   the config chain. After migration the `/health/live` line is **not** dropped.

2. **SECURITY — redaction bypass on native sinks.** `Destructure.ByTransforming` redaction
   only fires on the `WriteTo.Sink(custom)` mirror path. With the native `WriteTo.Console()`
   the policy is **bypassed and the `ApiKey` secret leaks** into the output. Real Serilog
   redacts; Herald 0.12.5 does not on native sinks. This violates the S5 security contract and
   is filed as `FINDING-destructure-native-sink-leak.md` with regression test
   `REG-SERILOG-DESTRUCTURE-NATIVE-SINK`.

**Status.** The migrated project *builds*, but it does **not** faithfully reproduce the
baseline at runtime (filter not applied; secret leaks), so it is recorded as `runs: false`.
This is the one project of the four that does not cleanly carry over, and the reasons are named.

**Before/after worth showing.** The baseline output (`{"Name": "Ada", "Email": "ada@acme.test"}`,
secret stripped; `/health` dropped) vs the migrated output (full record incl. `ApiKey`; `/health`
present). Use it as the honest "here is the current boundary" panel — not a win, a named edge.

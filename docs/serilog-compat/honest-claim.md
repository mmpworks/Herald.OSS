<!-- Heather T-H1: honest-claim.md is the SINGLE SOURCE of the headline wording.
     parity-audit.md and migration-runbook.md QUOTE it; they must never re-author it.
     Lives in engineering-owned docs/serilog-compat/ (the source of truth per scope-PRD
     open-decision #2). Dawn references this file from the website copy-deck; she owns
     tone + reading-level on the website surface, NOT the truth conditions in §3/§4.
     Any new claim goes back through engineering before it reaches the site. -->

# Honest Claim — Serilog Drop-In Compatibility

- **Date:** 2026-05-30
- **Branch:** `feat/serilog-compat`
- **Status:** Draft — §3 perf citation fills after P1 lands.
- **Owner:** Engineering owns the truth of the claim. Dawn (website) owns its presentation.

---

## 1. The approved headline

> Swap the package, rebuild, and your standard Serilog code runs on Herald — popular sinks map over; the third-party-sink ecosystem and the expression DSL are a documented boundary.

This is the single canonical string. It is byte-identical to the wording ratified in
`docs/serilog-compat/2026-05-29-scope-prd.md` §"Design-round outcomes". Every doc in
this initiative quotes it from here; none re-authors it.

---

## 2. The one-line version

> Swap the package, rebuild — your Serilog code runs on Herald.

This is the hero/CTA form. Dawn refines tone and reading level; she does **not** alter
the truth it encodes. The boundary is carried in the approved headline (§1) and stated
plainly in §5 — the one-liner trades on that context.

---

## 3. What we may say

These statements are engineering-true and pre-approved for any Herald.OSS consumer-facing
surface.

- **Source-compatible on recompile.** Herald mirrors Serilog's type shapes. We do not
  have Serilog's strong-name signing key, so this is *source identity, not binary
  identity* — a recompile is required, not just a package swap at the binary layer.
- **Popular sinks map directly over.** Console, File, Elasticsearch, OTLP, HTTP, TCP,
  UDP, and Null all have Herald equivalents. The parity audit names them row by row.
- **`appsettings.json` configuration drops in.** `ReadFrom.Configuration(IConfiguration)`
  parity is in scope. Apps that configure Serilog through `appsettings.json` swap the
  package and rebuild with no config-file changes.
- **ASP.NET Core wiring drops in.** `UseSerilog(...)`, `AddSerilog()`, and
  `UseSerilogRequestLogging()` are all covered. The `HeraldLoggerProvider` MEL adapter
  already ships; the ASP.NET surface wires over it.
- **Custom user-authored sinks, enrichers, and destructuring policies work — source-compiled
  only.** A sink or enricher you wrote and compile yourself will recompile against Herald.
  Pre-compiled community packages will not (see §5).
- **No measurable allocation on the accept path for the six hot primitives** (int, long,
  double, bool, DateTime, string) **on the typed fast path — 0 B at every arity 1–16.**
  Measured net10, BenchmarkDotNet InProcess, RyuJIT AVX2. The typed fast path means the
  `SerilogLoggerAdapter` is the concrete receiver and the template is a cached interned
  string. At arity 2 the accept path runs ~69 ns / 0 B; at arity 12 it runs ~430 ns / 0 B.
  The reject path — a level guard that turns the call away before any work — runs ~3 ns / 0 B.
  Source: `benchmarking/comparisons/net10/serilog-compat/` on `feat/serilog-compat`.
- **Scope, stated plainly.** The 0 B figure is the *typed fast path*. The
  `Serilog.Log.*` surface a consumer writes today routes through the Layer-2 `params object[]`
  shim, which boxes its arguments — the same thing real Serilog does. That surface costs
  ~108 ns / 1,271 B at arity 2 today. It is not 0 B, and we do not claim it is. Real Serilog
  4.3.1 at arity 12 runs ~551 ns / 1.49 KB; Herald's typed fast path at the same arity runs
  ~430 ns / 0 B.

---

## 4. What we may never say

The statements below are banned from every Herald consumer-facing surface — docs, website,
release notes, social copy, and any other output.

| Banned phrase | Why it is wrong |
|---|---|
| "1-to-1" | Implies binary identity. We cannot provide it — we don't have Serilog's signing key. |
| "binary drop-in" | Same reason. A recompile is required; binary passthrough is not the claim. |
| "100% compatible" | False on its face — community sinks and the expression DSL are a documented hard wall. |
| "fully compatible" | Same as "100% compatible" — the walls are real and named. |
| "seamless" | Implies zero friction. Recompiling and migrating any hard-wall gaps is friction. |
| Anything implying Seq or community sinks work | They don't. Pre-compiled against Serilog's identity. Will not load against Herald. |
| Anything implying `Serilog.Expressions` string DSL works | It doesn't. Predicate form maps; the string-DSL form is a hard wall. |
| Anything implying strong-name / `PublicKeyToken` compatibility | We will not spoof a key we do not have. |

These terms are listed here as banned — this is the only place they may appear in the
`docs/serilog-compat/` doc set. Every other doc in this initiative must pass the
banned-wording grep clean (the §4 list itself is the exception, by definition).

---

## 5. The boundary, stated for marketing

Herald mirrors Serilog's type shapes so your source code compiles and runs against Herald
after a rebuild. Popular sinks — Console, File, Elasticsearch, OTLP, and others — have
direct Herald equivalents and carry over.

Pre-compiled community packages like `Serilog.Sinks.Seq` are a different matter. Those
packages were compiled against Serilog's own cryptographic assembly identity — a specific
signing key we do not have and will not impersonate. They cannot load against Herald's
shim. This is a structural identity boundary, not a bug we will fix. The parity audit
names every affected sink and describes the realistic alternatives.

The `Serilog.Expressions` string DSL is the second hard boundary. Predicate-style
filtering maps to Herald processors; the string-DSL form does not carry over. This is
documented as an open design problem and surfaced to the OSS community as an RFC.

Both walls are documented, named, and bounded. Everything outside them carries over on
recompile.

---

## 6. Hand-off note to Dawn

Dawn owns **tone and reading level** for every Herald.OSS website surface that draws on
this file. The `mmpworks-writing-voice` skill governs that work: simple sentences, positive
framing (with/keeps/lower over without/avoids/no), mixed-style emphasis (*italic* /
**bold** / ***bold-italic***) for visual interest.

What Dawn does **not** edit:

- §3 — these are engineering-true statements. They change only when the engineering
  changes, and only after an engineering review confirms the new claim is still true.
- §4 — this is the banned list. Adding to it requires an engineering decision. Removing
  anything from it requires an engineering decision. Dawn's voice work happens above or
  around this list, never inside it.

Any new factual claim — anything not already in §3 — goes back through engineering review
before it appears on the site. The one-liner in §2 is the form Dawn works from for hero
and CTA copy; the full headline in §1 is available wherever the full boundary statement
is appropriate.

# Current State
_as of 2026-08-12_

## What we're building right now
Herald.OSS just shipped 0.13.0 (short-vocabulary level aliases: `Trace/Info/Warn/Critical`, additive, zero wire-format change) and 0.12.12 (RootNamespace `Herald`, was `MMP.Herald`) on `main`. The active uncommitted work is the NLog and log4net migration-compat shims (`src/NLog/`, `src/Log4net/`, `tests/Migration/`) — sibling packages to the existing Serilog shim (`src/Serilog/`), per Richard's design-complete spec at `docs/design/nlog-log4net-shim-spec.md`. These are new, real source files sitting untracked in the working tree, not yet committed.

## Active decisions
- 2026-06-03 (Richard's spec): NLog and log4net shims mirror the Serilog shim's architecture exactly — types live inside the Herald.OSS assembly (`src/NLog/`, `src/Log4net/`), each ships as a thin forwarding package (`MMP.Herald.Log.Migration.NLog`/`.Log4net.csproj`) with `<Compile Remove>` and a `ProjectReference` back to `Herald.OSS.csproj`. TFMs `net8.0;net9.0;net10.0`, matching the Serilog shim.
- 2026-06-03 (same spec): the log4net `IAppender` output-routing boundary (e.g. `Form1 : IAppender`) is explicitly OUT of scope for the shim — documented as a rewrite-to-Herald-sink pattern, not bridged.
- 2026-06-02 (net8-parity-inventory): net8 is an equal release tier with net9/net10; zero `IMPOSSIBLE-ON-NET8` walls remain. ~80% of remaining net8-parity work is un-gating the `#if NET9_0_OR_GREATER` Serilog-compat test suite (~55 files) so net8's Serilog runtime is actually tested, not just built.
- 2026-08-11: gitleaks pre-commit secret scanning rolled out (Phase 1).
- Standing rule (CODING_INSTRUCTIONS.md/CLAUDE.md-equivalent guidance in the Herald umbrella): every benchmark and published number must be .NET 10 only; the Serilog-compat static `Log.*` surface is 0-alloc and must never be described as boxing.

## Open questions
- The NLog/log4net shim source is untracked and unbuilt-verified in this pass — whether it currently compiles clean across all three TFMs and passes `tests/Migration/*SmokeTests.cs` hasn't been confirmed in this session.
- Three net8-parity scope rulings are still open (per `docs/_wip/net8-parity-inventory-2026-06-02.md`): should the three net9/net10-only Layer-2 "cutover mirror" assemblies (`MMP.Herald.Compat.Serilog`, `.AspNetCore`, `.Nuget`) gain a net8 target; should the `Console(ITextFormatter, …)` bridge overload be un-gated for net8; does `AspNetCore` compat stay net9+ only.
- Test-isolation flake fixes from `docs/_wip/test-isolation-flakes-2026-06-02.md` (cross-class races on shared static state) — status of whether Glenn's sequenced write-pass fully landed them is not re-verified here.

## Next action
Run `bash build.sh --release --all --test` (or the OSS-repo equivalent) to confirm the untracked NLog/Log4net shim source actually builds and its smoke tests pass across net8.0/net9.0/net10.0 before doing anything else with it — it has never been committed, so there is no green-build receipt for it yet.

## Stop condition
Halt and return to Steve before: committing the NLog/Log4net shim source, cutting a new release/version bump, or publishing a NuGet package. If the shim fails to build cleanly, stop and report rather than reshaping the design to force a pass — the spec is Richard's and changes to it need his sign-off.

## Needs approval
- Any NuGet publish (irreversible) — standing rule.
- Cutting a new Herald.OSS release/version tag.
- Committing large untracked new source trees (`src/NLog/`, `src/Log4net/`, `tests/Migration/`) — confirm build-green first, then this is a normal commit, not necessarily an approval-gated one, but flag it since it's Richard-designed work landing for the first time.
- Agents can freely: build, test, and read the shim source and design docs; commit small doc/status updates.

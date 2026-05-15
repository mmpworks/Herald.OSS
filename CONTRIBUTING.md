# Contributing to Herald.OSS

Thanks for the interest. Herald.OSS is the Apache 2.0 upstream of the
Herald structured-logging kernel for .NET. Contributions are welcome —
from fixing typos to landing new addons.

## Before you start

- **Bugs**: open a [bug report](.github/ISSUE_TEMPLATE/bug_report.md).
  Include a minimal repro.
- **Security vulnerabilities**: do **not** open a public issue. Follow
  [SECURITY.md](SECURITY.md).
- **Feature requests**: open a
  [feature request](.github/ISSUE_TEMPLATE/feature_request.md) and
  describe the problem you're trying to solve, not just the proposed
  solution.
- **Questions**: GitHub Discussions is the right place. The issue
  tracker is reserved for bugs and feature requests.

## Contributor License Agreement (CLA)

Herald.OSS uses a CLA. The CLA is shared with Herald.Core and other
MMPWorks Herald repositories; signing it once covers every Herald
project.

The CLA repo is at
[mmpworks/cla-signatures](https://github.com/mmpworks/cla-signatures).
A bot will comment on your first pull request with the signing flow.
The PR cannot merge until the bot is satisfied.

If you're contributing on behalf of an employer, you'll be asked to
sign the Corporate CLA instead of the Individual CLA. The bot handles
that distinction automatically.

## Pull request expectations

1. **Fork and branch.** Branch off `main` with a descriptive name
   (`fix/kernel-mixed-sink`, `feat/redactor-fast-path`).
2. **Build clean.** `dotnet build Herald.OSS.csproj -c Release` must
   pass with zero warnings on all three target frameworks.
3. **Tests pass.**
   `dotnet test tests/Herald.OSS.Tests.csproj -c Release` must pass on
   net8, net9, and net10. CI enforces this.
4. **New behaviour gets tests.** New public surface or a fix to a
   reported bug should land with a test that would fail without the
   change.
5. **Performance-sensitive changes need benchmarks.** Anything that
   touches the accept path, the kernel, or a hot decorator should
   include a benchmark before/after. Benches live under
   `benchmarking/comparisons/net10/herald/` and
   `benchmarking/library/{net8,net9,net10}/`.
6. **One logical change per PR.** Easier to review, easier to revert.
7. **Open the PR with a short summary** describing the change and the
   motivation. CI will run; the CLA bot will comment.

## Coding standards

A short summary; the goal is to keep the bar consistent across
contributors without requiring everyone to read a separate standards
doc:

- Target **C# 12 on .NET 8** for source compatibility.
- **CUPID first**, **DRY second**, **low cognitive complexity** always.
- **Nullable reference types** are enabled and respected.
- **No `!` (null-forgiveness)** without a clearly documented reason.
- Prefer **guard clauses** over nested conditionals.
- Prefer **records** for immutable data carriers; **sealed** when
  inheritance is not part of the design.
- Default to **no comments** — well-named identifiers carry intent. Add
  a comment only when the *why* is non-obvious (a hidden invariant, a
  workaround for a specific bug, behaviour that would surprise a
  reader).
- Don't add error handling, fallbacks, or validation for scenarios that
  can't happen. Trust internal code and framework guarantees. Validate
  at system boundaries only.

## Scope

Herald.OSS accepts:

- Bug fixes and reliability improvements
- Performance improvements with benchmark evidence
- New addons under `src/Addons/` when the addon makes sense at the
  Community boundary (i.e., does not require edition gating)
- Documentation improvements
- Conformance tests, especially edge-case coverage

Herald.OSS does NOT accept:

- Edition-gating machinery — Herald.OSS is single-edition by design;
  paid-feature gating lives downstream in Herald.Core
- Closed-source dependencies
- Telemetry or phone-home behaviour
- Distribution-hardening / obfuscation tooling (paid-distribution
  concern; lives in Herald.Core)

## Code review

Two reviewers are required for changes that touch:

- `src/Pipeline/Kernel/`
- `native/dotnet/Pipeline/`
- `src/Events/`
- Anything that affects the public SDK surface (types or signatures
  consumers depend on)

One reviewer is sufficient for documentation, benchmarks, addon-internal
changes, GitHub workflows, and build infrastructure.

## Releasing

Releases are cut from `main` once CI is green, the CHANGELOG is updated
with the new version's notes, and the package metadata in
`Herald.OSS.csproj` reflects the target version. Tagging and NuGet push
are owned by the maintainers.

## License

Herald.OSS is licensed under the Apache License 2.0. By contributing,
you agree your contribution is licensed under the same terms (see
`LICENSE` and the CLA above).

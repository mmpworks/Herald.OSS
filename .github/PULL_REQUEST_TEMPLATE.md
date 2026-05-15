<!--
Thanks for the pull request. A few quick checks before you submit:

- The CLA bot will ask first-time contributors to sign the Herald CLA at
  https://github.com/mmpworks/cla-signatures. The PR will not merge until
  the bot is happy.
- CI runs build + test on net8 / net9 / net10 on push and PR. Local
  parity: `dotnet build Herald.OSS.csproj -c Release` and
  `dotnet test tests/Herald.OSS.Tests.csproj -c Release`.
- Performance-sensitive changes should include a benchmark before/after.
  Benches live under `benchmarking/comparisons/net10/herald/` and
  `benchmarking/library/{net8,net9,net10}/`.
-->

## Summary

<!-- One or two sentences. What does this change do? -->

## Motivation

<!-- Why is this change needed? Link an issue if one exists. -->

## Approach

<!-- Brief design notes. Anything subtle a reviewer should know? -->

## Test plan

<!-- How did you verify the change? -->

- [ ] `dotnet build Herald.OSS.csproj -c Release` passes
- [ ] `dotnet test tests/Herald.OSS.Tests.csproj -c Release` passes on net8/net9/net10
- [ ] (If perf-sensitive) benchmark numbers attached
- [ ] (If new behaviour) tests added covering the new path

## Breaking change?

<!-- Yes / No. If yes, describe migration. -->

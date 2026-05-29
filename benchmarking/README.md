# Herald.OSS benchmarking suite

## .NET 10 only for published numbers

Every Herald benchmark number that gets published — headline figures,
website data files, release notes, any externally visible number — must
come from a .NET 10 run. Run the published numbers from:

- `library/net10/`
- `comparisons/net10/`

The `library/net8/` and `library/net9/` projects exist for internal
runtime-delta checks only. They are useful when we want to characterize
how Herald behaves across runtimes, but they must never feed a published
or website number. When a number can only be sourced from a net8/net9
run, re-run it on .NET 10 and publish that figure instead.

The reason is coherence. A hidden runtime mix — a net8 headline beside
net10 detail rows — reads as incoherence to anyone checking the work.
And because net10 is faster on the hot path, off-runtime cells looked
wrong when they were only off-runtime. Pinning every published number to
.NET 10 keeps the surface honest and easy to verify.

## Layout

- `library/` — in-process Herald benchmarks. Subfolders `net10/`,
  `net9/`, `net8/`, plus `sharedproject/` for the shared bench code.
  `net10/` is the source of published numbers; `net8/` and `net9/` are
  internal runtime-delta only.
- `comparisons/` — Herald-vs-competitor benchmarks. `net10/` only;
  this is a published surface.

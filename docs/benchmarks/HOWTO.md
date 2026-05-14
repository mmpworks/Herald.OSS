# How we do benchmark reporting — Herald.OSS

Operational notes for every Herald.OSS benchmark run. Read this before
kicking off a re-run; every gotcha here cost a real failed run to learn.

## Scope — Herald.OSS owns its own benchmarks

Herald.OSS ships its own BenchmarkDotNet harness at
`benchmarks/Herald.OSS.Benchmarks.csproj`. The harness is intentionally
narrow: it measures the kernel and accept-path that are public surface
in Herald.OSS, nothing more. There is no competitive head-to-head suite
in this repo — no Serilog / NLog / ZLogger packages are pulled in, and
no comparative tables are published from here. Adopters who want
head-to-head numbers should consume Herald.OSS as a NuGet package and
write the comparison in their own bench project.

`benchmarks/Herald.OSS.Benchmarks.csproj` is the single benchmark
csproj this repo ships. Every result published from Herald.OSS comes
from this one project.

## Default scope — net10 only

Every bench in this suite runs on net10.0. The OSS benchmark csproj
does not multi-target on purpose — the headline numbers are net10
numbers and a multi-TFM matrix would dilute the reading without a
useful comparison. If you need net8 or net9 figures, build a separate
consumer project that pins the older TFM and references the package.

A net10-only run lands in 3–6 minutes on a 12900K. Don't burn that
time speculatively.

## The naming convention — the part that's policy, not preference

Every benchmark run gets a sortable, UTC-stamped folder under
`docs/benchmarks/runs/`. Every doc emitted by that run shares the same
stamp so the doc and its raw artifacts can never drift apart.

```
docs/benchmarks/
  HOWTO.md                                 # this file
  kernel-fan-out-net10-{u}.md              # latest per (kind, dotNetVer)
  accept-path-net10-{u}.md
  runs/
    run-{u}/                               # full history of every run
      kernel-fan-out-net10-{u}.md
      accept-path-net10-{u}.md
      net10/                               # raw BDN artifacts
```

`{u}` is a **filesystem-safe sortable UTC timestamp** at minute
precision. Dashes throughout — date and time both — so the path is
creatable on every supported OS without quoting:

```
yyyy-MM-ddTHH-mmZ
```

Examples: `2026-05-14T02-13Z`, `2026-05-02T11-55Z`. Two runs in the
same minute are vanishingly rare; if it happens, append `-2`, `-3`,
etc.

### Workflow — runs go to `runs/`, latest gets promoted

1. **Run lands in `docs/benchmarks/runs/run-{u}/`.** Raw BDN artifacts
   (logs, CSVs, the joined GitHub-formatted reports) and the per-doc
   `.md` files all live together inside that folder. This is the
   citation-key directory; reviews and claims docs link to it directly.
2. **Write the docs in place** — write
   `kernel-fan-out-net10-{u}.md`,
   `accept-path-net10-{u}.md`, etc. inside `runs/run-{u}/`, alongside
   the raw artifacts they describe.
3. **When the docs are final, copy each one up to `docs/benchmarks/`**
   and **remove the prior version that shares the same uniqueness key**
   from `docs/benchmarks/`. The uniqueness key is the filename with the
   `{u}` stamp stripped out. Older docs stay in their `runs/run-{u}/`
   folder; they are never deleted from there.
4. **Goal:** `docs/benchmarks/` always reflects the latest data per
   uniqueness key. A re-run of the same kind replaces the same-kind
   doc at the top level; running a new kind adds a new doc; nothing
   already at the top level disappears unless its same-shape successor
   lands.

A run that did not produce a particular doc does **not** trigger a
promotion for that doc — the existing top-level entry stays put until
a later run refreshes it.

---

## Running the benchmarks

### Build the assembly

Always build with `Release`:

```bash
cd E:/dev/Herald.OSS
dotnet build benchmarks/Herald.OSS.Benchmarks.csproj -c Release
```

### Run a specific bench

`BenchmarkSwitcher` lets you pick a subset with `--filter`:

```bash
cd E:/dev/Herald.OSS

# Kernel fan-out arities
dotnet benchmarks/bin/Release/net10.0/Herald.OSS.Benchmarks.dll \
  --filter "*KernelFanOutBenchmarks*" \
  --artifacts docs/benchmarks/runs/run-{u}/net10

# Accept-path latencies
dotnet benchmarks/bin/Release/net10.0/Herald.OSS.Benchmarks.dll \
  --filter "*AcceptPathBenchmarks*" \
  --artifacts docs/benchmarks/runs/run-{u}/net10
```

Replace `{u}` with the actual UTC timestamp at run start. The
`--artifacts` path co-locates raw BDN output with the run's writeup
folder.

### Run everything

```bash
dotnet benchmarks/bin/Release/net10.0/Herald.OSS.Benchmarks.dll \
  --filter "*" \
  --artifacts docs/benchmarks/runs/run-{u}/net10
```

### BDN filter syntax — gotchas

- **Scope by class name.** `--filter "*Kernel*"` matches every benchmark
  with "Kernel" anywhere in its qualified name. Always include the
  class fragment if you want to narrow: `"*KernelFanOutBenchmarks*"`.
- **Multiple `--filter` arguments are OR'd.** Listing four patterns is
  how you select "everything except X" — additive filters union, BDN
  has no built-in negation.
- **Glob is case-insensitive** but matches the fully-qualified name,
  not the `[Benchmark(Description = ...)]` label. Method-name fragments
  are reliable; description fragments may not match the way you
  expect.

### BDN iteration-cap minimums

BDN's `Job.Default` sets `MinIterationCount = 15` and
`MinWarmupIterationCount = 6`. The CLI rejects caps that don't strictly
exceed those minima:

| Flag | Minimum allowable value |
|---|---|
| `--maxIterationCount` | 16 |
| `--maxWarmupCount` | 7 |

The OSS benches do not hit the cumulative time budget that Core's
competitive suite struggles with — they're short, allocation-free, and
don't bring in third-party packages whose pilot phases balloon. Default
caps are fine.

---

## Adding new benchmark rows

Apples-to-apples is the goal. When adding a row:

- **Match the input shape across benchmarks within a class.** Same
  template, same property values, same property count. The existing
  benches pass identical literal strings so per-call argument
  allocation isn't a confounder.
- **Use `[MemoryDiagnoser]` on every class.** Allocations per call are
  as load-bearing as wall-clock time for a logging library.
- **Avoid `[Params]` if a fixed shape suffices.** A bench with five
  parameterized arities is fine when arity is the variable being
  measured; a bench whose arity could just be split into two methods
  reads more clearly with two methods.
- **OSS rows only.** Herald.OSS benches must be measurable from this
  repo's source — no external native-binary fallbacks, no closed-source
  pipeline stages. If a row depends on a private build artifact, it
  doesn't belong here.

---

## Reproduce shape (paste into every doc)

Every published `.md` ends with a reproduce block that points at the
run's own directory. Keep it concrete and copy-pasteable; replace
`{u}` with the actual stamp.

```bash
cd E:/dev/Herald.OSS
dotnet build benchmarks/Herald.OSS.Benchmarks.csproj -c Release

dotnet benchmarks/bin/Release/net10.0/Herald.OSS.Benchmarks.dll \
  --filter "*KernelFanOutBenchmarks*" \
  --artifacts docs/benchmarks/runs/run-{u}/net10
```

---

## Package pins

The benchmark package is pinned in `Directory.Packages.props`:

| Package | Pinned version |
|---|---|
| BenchmarkDotNet | 0.14.0 |

When bumping the version, do it in `Directory.Packages.props` and
re-run a smoke bench to confirm BDN's output shape hasn't shifted.

---

## After the run

1. **Write the doc in `runs/run-{u}/`** alongside its raw artifacts.
   Each doc carries: trigger, scope, host, captures (per-class
   subfolder list), what the harness measures, headline tables,
   methodology notes, reproduce block, package versions.
2. **Promote** each finalized doc to `docs/benchmarks/` and **remove
   the prior doc of the same kind** from there. The top-level
   directory always shows the most recent set, one of each kind.
3. If the run reveals a perf shift large enough to invalidate prior
   reviews / claims docs, refresh those alongside the promoted docs in
   a single commit so the documentation and the numbers move together.

# How we do benchmark reporting — Herald.OSS

Operational notes for every Herald.OSS benchmark run. Read this before
kicking off a re-run; every gotcha here cost a real failed run to learn.

## Two benchmark suites

Herald.OSS ships two benchmark suites under `benchmarking/`. Each has
its own purpose, audience, and run cadence.

### `benchmarking/library/` — internal library benches

The library suite measures Herald.OSS's own public surface. It exists
so a future refactor can prove "we did not regress the hot path"
through a numeric check, not a vibe. Three target frameworks (net8,
net9, net10) are each their own exe project, sharing a single library
of bench classes.

```
benchmarking/library/
  sharedproject/
    Herald.OSS.LibraryBenchmarks.Shared.csproj    (multi-target lib)
    KernelFanOutBenchmarks.cs
    AcceptPathBenchmarks.cs
  net8/
    Herald.OSS.LibraryBenchmarks.csproj           (exe, net8.0)
    Program.cs
    results/                                       (BDN artifacts)
  net9/
    Herald.OSS.LibraryBenchmarks.csproj           (exe, net9.0)
    Program.cs
    results/
  net10/
    Herald.OSS.LibraryBenchmarks.csproj           (exe, net10.0)
    Program.cs
    results/
```

### `benchmarking/comparisons/` — competitive head-to-head

The comparisons suite measures Herald.OSS against named competitors on
matched accept-path shapes. Each competitor lives in its own folder
with its own csproj — the per-competitor isolation is structural: a
package vulnerability in one competitor never blocks another row, and
each row builds and runs independently. net10.0 only; the headline
numbers are net10 numbers.

```
benchmarking/comparisons/net10/
  herald/
    Herald.Comparison.csproj
    Program.cs
    AcceptCallBenchmarks.cs
    results/
  serilog/
    Serilog.Comparison.csproj
    Program.cs
    AcceptCallBenchmarks.cs
    results/
  nlog/
    NLog.Comparison.csproj
    ...
  zlogger/
    ZLogger.Comparison.csproj
    ...
  log4net/
    Log4Net.Comparison.csproj                     (folder named log4net,
    ...                                            wiki calls it log4j)
  MEL/
    MEL.Comparison.csproj                         (Microsoft.Extensions.Logging)
    ...
```

Every competitor's `AcceptCallBenchmarks.cs` ships the same three
methods: zero-property / one-property / four-property `Info`-level
emit. The same template shapes, the same property values, the same
discarding-sink terminus — different libraries, same input.

## Default scope — net10 only

Every competitive bench in this suite runs on net10.0. The headline
numbers are net10 numbers and a multi-TFM matrix on the comparison
side would dilute the reading without a useful comparison. If you need
net8 or net9 figures from a competitor, build a separate consumer
project that pins the older TFM.

The library suite multi-targets net8/net9/net10 because the question
"did we regress on older TFMs?" is real and worth pinning. The
comparison suite is single-target on purpose.

A net10-only competitive run for one competitor lands in 1–3 minutes
on a 12900K. A full sweep (six competitors plus the library benches)
runs ~15–25 minutes wall-clock.

## The naming convention — the part that's policy, not preference

Every benchmark run gets a sortable, UTC-stamped folder under
`docs/benchmarks/history/`. Every doc emitted by that run shares the
same stamp so the doc and its raw artifacts can never drift apart.

```
docs/benchmarks/
  HOWTO.md                                    # this file
  kernel-fan-out-net10-{u}.md                 # latest per (kind, dotNetVer)
  kernel-fan-out-net9-{u}.md
  kernel-fan-out-net8-{u}.md
  accept-path-net10-{u}.md
  comparison-accept-call-net10-{u}.md         # six-row competitive table
  history/
    run-{u}/                                  # full history of every run
      kernel-fan-out-net10-{u}.md
      kernel-fan-out-net9-{u}.md
      ...
      net10/                                  # raw BDN artifacts (library)
      net9/
      net8/
      comparisons/                            # raw BDN artifacts (competitive)
        herald/
        serilog/
        nlog/
        zlogger/
        log4net/
        MEL/
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

### Workflow — runs go to `history/`, latest gets promoted

1. **Run lands in `docs/benchmarks/history/run-{u}/`.** Raw BDN
   artifacts and per-doc `.md` files all live together inside that
   folder. This is the citation-key directory; reviews and claims
   docs link to it directly.
2. **Write the docs in place** — inside `history/run-{u}/`,
   alongside the raw artifacts they describe.
3. **When the docs are final, copy each one up to
   `docs/benchmarks/`** and **remove the prior version that shares
   the same uniqueness key** from `docs/benchmarks/`. The uniqueness
   key is the filename with the `{u}` stamp stripped. Older docs
   stay in their `history/run-{u}/` folder; they are never deleted
   from there.
4. **Goal:** `docs/benchmarks/` always reflects the latest data per
   uniqueness key. A re-run of the same kind replaces the same-kind
   doc at the top level; running a new kind adds a new doc; nothing
   already at the top level disappears unless its same-shape
   successor lands.

A run that did not produce a particular doc does **not** trigger a
promotion for that doc — the existing top-level entry stays put until
a later run refreshes it.

---

## Running the library benches

### Build

```bash
cd E:/dev/Herald.OSS
dotnet build benchmarking/library/net10/Herald.OSS.LibraryBenchmarks.csproj -c Release
# or per-TFM:
dotnet build benchmarking/library/net8/Herald.OSS.LibraryBenchmarks.csproj  -c Release
dotnet build benchmarking/library/net9/Herald.OSS.LibraryBenchmarks.csproj  -c Release
```

### Run

The per-TFM exe lives at `benchmarking/library/net{TFM}/bin/Release/net{TFM}.0/`.
Output goes into the matching `results/` folder via BDN's
`--artifacts` flag:

```bash
# net10 library benches
dotnet benchmarking/library/net10/bin/Release/net10.0/Herald.OSS.LibraryBenchmarks.net10.dll \
  --filter "*" \
  --artifacts benchmarking/library/net10/results
```

A filtered subset:

```bash
dotnet benchmarking/library/net10/bin/Release/net10.0/Herald.OSS.LibraryBenchmarks.net10.dll \
  --filter "*KernelFanOut*" \
  --artifacts benchmarking/library/net10/results
```

After the run, the writeups go into `docs/benchmarks/history/run-{u}/`
and are then promoted to `docs/benchmarks/` per the workflow above.

---

## Running the comparison benches

Each competitor is its own exe. Run them one at a time; collect the
results into the same `history/run-{u}/comparisons/{competitor}/`
folder.

### Build a single competitor

```bash
cd E:/dev/Herald.OSS
dotnet build benchmarking/comparisons/net10/serilog/Serilog.Comparison.csproj -c Release
```

### Run a single competitor

```bash
dotnet benchmarking/comparisons/net10/serilog/bin/Release/net10.0/Serilog.Comparison.dll \
  --filter "*" \
  --artifacts benchmarking/comparisons/net10/serilog/results
```

Replace `serilog` with `herald`, `nlog`, `zlogger`, `log4net`, `MEL`
to run the other rows. Each one is independent — a failure in one row
does not affect the others.

### Run the full sweep

```bash
#!/usr/bin/env bash
cd E:/dev/Herald.OSS

for competitor in herald serilog nlog zlogger log4net MEL; do
  case "$competitor" in
    herald)   dll="Herald.Comparison.dll" ;;
    serilog)  dll="Serilog.Comparison.dll" ;;
    nlog)     dll="NLog.Comparison.dll" ;;
    zlogger)  dll="ZLogger.Comparison.dll" ;;
    log4net)  dll="Log4Net.Comparison.dll" ;;
    MEL)      dll="MEL.Comparison.dll" ;;
  esac

  dotnet "benchmarking/comparisons/net10/${competitor}/bin/Release/net10.0/${dll}" \
    --filter "*" \
    --artifacts "benchmarking/comparisons/net10/${competitor}/results"
done
```

A full sweep on a 12900K lands in ~15–25 minutes wall-clock. Per-row
runs land in 1–3 minutes.

### BDN filter syntax — gotchas

- **Scope by class name.** `--filter "*Accept*"` matches every
  benchmark with "Accept" anywhere in its qualified name. Each
  competitor only has one bench class so a bare `--filter "*"` is
  unambiguous within the row.
- **Multiple `--filter` arguments are OR'd.** Listing patterns is how
  you select a subset.
- **Glob is case-insensitive** but matches the fully-qualified name,
  not the `[Benchmark(Description = ...)]` label. Method-name
  fragments are reliable.

### BDN iteration-cap minimums

BDN's `Job.Default` sets `MinIterationCount = 15` and
`MinWarmupIterationCount = 6`. The CLI rejects caps that don't strictly
exceed those minima:

| Flag | Minimum allowable value |
|---|---|
| `--maxIterationCount` | 16 |
| `--maxWarmupCount` | 7 |

Default caps are fine for these benches — they're short and
allocation-light enough that the cumulative time budget that bites
Herald.Core's full competitive suite is not a concern here.

---

## Adding new benchmark rows

Apples-to-apples is the goal.

### Library side

- Match the input shape across benches within a class.
- Use `[MemoryDiagnoser]` on every bench class. Allocations per call
  are as load-bearing as wall-clock time for a logging library.
- Avoid `[Params]` if a fixed shape suffices.

### Comparison side

- **Match the input shape across libraries.** Same template, same
  property values, same property count. Differences in measured cost
  reflect library-level design choices, not setup asymmetry.
- **Configure each library's null sink the way it ships.** Serilog's
  custom sink that skips render. NLog's `NullTarget` that skips
  layout. ZLogger's `Stream.Null` that does end-to-end render-to-bytes.
  MEL's active-null provider that runs the formatter callback.
  log4net's `AppenderSkeleton`-based no-op. These differences are
  *real production behavior* — the benchmark is not measuring the same
  workload across libraries, it's measuring "what does each library
  cost when configured the way you'd actually configure it for a
  discarding sink." Document any asymmetry in the writeup; never
  paper over it.

---

## Reproduce shape (paste into every doc)

Every published `.md` ends with a reproduce block that points at the
run's own directory. Keep it concrete and copy-pasteable; replace
`{u}` with the actual stamp.

```bash
cd E:/dev/Herald.OSS

# Library side
dotnet build benchmarking/library/net10/Herald.OSS.LibraryBenchmarks.csproj -c Release
dotnet benchmarking/library/net10/bin/Release/net10.0/Herald.OSS.LibraryBenchmarks.net10.dll \
  --filter "*" \
  --artifacts docs/benchmarks/history/run-{u}/library/net10

# Comparison side (Serilog row)
dotnet build benchmarking/comparisons/net10/serilog/Serilog.Comparison.csproj -c Release
dotnet benchmarking/comparisons/net10/serilog/bin/Release/net10.0/Serilog.Comparison.dll \
  --filter "*" \
  --artifacts docs/benchmarks/history/run-{u}/comparisons/serilog
```

---

## Package pins

The bench-only packages are pinned in `Directory.Packages.props`:

| Package | Pinned version | Used by |
|---|---|---|
| BenchmarkDotNet | 0.14.0 | every bench csproj |
| Serilog | 4.0.0 | comparisons/net10/serilog |
| Serilog.Sinks.Console | 6.0.0 | comparisons/net10/serilog (transitive only; not used directly in v0.1.0 benches) |
| NLog | 5.3.4 | comparisons/net10/nlog |
| ZLogger | 2.5.10 | comparisons/net10/zlogger |
| log4net | 3.0.3 | comparisons/net10/log4net |
| Microsoft.Extensions.Logging | 8.0.0 | comparisons/net10/MEL (and zlogger transitively) |

When bumping a competitor version, do it in `Directory.Packages.props`
and re-run that competitor's bench to confirm BDN's output shape
hasn't shifted.

The log4net pin carries a moderate-severity NU1902 advisory. The
log4net comparison csproj suppresses `NU1902;NU1903` explicitly
because the bench is not a shipped artifact. Pin to a fixed version
when one ships.

---

## After the run

1. **Write the doc in `history/run-{u}/`** alongside its raw artifacts.
   Each doc carries: trigger, scope, host, captures (per-class or
   per-competitor subfolder list), what the harness measures, headline
   tables, methodology notes, reproduce block, package versions.
2. **Promote** each finalized doc to `docs/benchmarks/` and **remove
   the prior doc of the same kind** from there. The top-level
   directory always shows the most recent set, one of each kind.
3. If the run reveals a perf shift large enough to invalidate prior
   reviews / claims docs, refresh those alongside the promoted docs
   in a single commit so the documentation and the numbers move
   together.

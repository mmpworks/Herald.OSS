# Benchmarks update — 0.12.9

*Per-release benchmark posture note. The single publication source remains
`docs/benchmarks/consolidated-benchmarks.md`; this file records what was
(and was not) re-measured for a given release and why.*

## 0.12.9 — 2026-06-07

### What changed in this release

All three 0.12.9 changes are **off the hot path**:

1. **OTLP protobuf severity-text resolution** — a missing case-insensitive
   map in `OtlpProtobufLogDecoder`. Touches OTLP ingest decode only.
2. **G1.1 crash fix** — whitespace-only `severityText` now routes through
   the existing `optionalLevelDefault` fallback instead of throwing.
   OTLP ingest decode only.
3. **`HeraldAdapterCore` extraction** — internal restructuring of the
   Serilog adapter slow path (destructuring policies, `ICaptureRedactor`)
   into `src/Adapters/`. Public Serilog adapter API unchanged; no
   behavioral change.

None of these touch the accept call, the kernel fan-out, the source
generator, the rejected-call gate, or the redaction fast path — the
surfaces the consolidated headline numbers measure.

### Bench posture for 0.12.9

A representative **net10 sanity pass** on the accept path was run to
confirm the non-hot-path changes introduced no throughput or GC
regression. The 12h/24h endurance soaks were **not** re-run — there is
no code path in this release that could affect sustained-rate behavior,
and the existing consolidated soaks remain the authoritative endurance
figures.

#### net10 accept-path sanity run (ShortRun, MemoryDiagnoser)

Host: 12th Gen Intel Core i9-12900K, .NET SDK 10.0.204, .NET 10.0.8
(10.0.826.23019), X64 RyuJIT AVX2. BenchmarkDotNet v0.14.0.

| Method                     | Mean     | Allocated |
|--------------------------- |---------:|----------:|
| Info_no_properties         | 28.44 ns |     0 B   |
| Info_with_one_property     | 41.34 ns |     0 B   |
| Info_with_three_properties | 46.43 ns |     0 B   |

These match the consolidated accept-path band (≈26–46 ns, 0 B per op).
**No throughput or allocation regression.** A ShortRun (3×3) is
sufficient for a no-regression gate on non-hot-path changes; it is not
a publication-grade run and is not promoted into the consolidated rollup.

### Authoritative endurance figures (unchanged, cited)

The published endurance numbers remain the consolidated multi-hour soaks
(`docs/benchmarks/consolidated-benchmarks.md` §17), all on isolated Azure
VMs, .NET 10, Server GC:

- **100 kHz × 24h, single connection** — 8,400,010,000 events delivered,
  **0 dropped / 0 drain errors**, dead-on pacing across 280 windows
  (`Standard_D8ds_v6`).
- **250 kHz × 12h, 16 connections** — 10,800,160,000 events delivered,
  **0 dropped / 0 drain errors**, ~0 B/event steady state, 125 MB max
  working set (`Standard_D16s_v6`).

(Carry the §17 pause caveat verbatim: the sampled max pause is a lower
bound, not a population max.)

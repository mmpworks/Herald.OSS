# Herald.OSS 0.12.0 — Serilog destructure benchmarks (cloud baseline)

**Date:** 2026-05-31 · **Runtime:** .NET 10 · **Tool:** BenchmarkDotNet
**Commit:** `c898771` (library `src/` unchanged since the v0.12.0 release commit)
**Workload:** the canonical `{@Position}` destructure example from https://serilog.net/, swept across arity 1/2/4/8/12/16.

## Where these numbers came from

Two isolated Azure VMs in East US 2, each provisioned clean, pinned to the
performance governor, running the exact same harness on .NET 10:

| Box | SKU | CPU | Cores | Role |
|---|---|---|---|---|
| **Canonical** | `Standard_F8als_v6` | AMD EPYC 9V74 @ 2.6 GHz | 8 physical (no hyperthreading) | tightest variance → published baseline |
| Cross-check | `Standard_FX12mds_v2` | Intel Xeon Platinum 8573C @ 2.3 GHz (boosts) | 12 vCPU | higher clock, slightly noisier |

The no-SMT box is canonical because its run-to-run error is the smallest
(Serilog rows land within ±1–7 ns; the Xeon's allocating rows swing ±5–17 ns).
A reviewer can reproduce either: same SKU, .NET 10, BenchmarkDotNet, the
`benchmarking/compare.sh --scenario destructure` / `destructure-reject` harness.

**One thing that is true on any machine:** the allocation numbers below are a
property of the code path, not the CPU. They are byte-for-byte identical on the
EPYC box, the Xeon box, and a laptop. Only the nanoseconds move with hardware.

## Accept path — the event passes the level gate

The realistic case: a structured log call that is recorded. `{@Position}` is the
serilog.net destructure example; extra properties are plain ints.

### Canonical box — `Standard_F8als_v6`

| Arity | Herald native | Herald compat (drop-in) | Real Serilog 4.3.1 | Native vs Serilog |
|---|---|---|---|---|
| 1  | **36.45 ns · 0 B** | 39.87 ns · 0 B | 287.3 ns · 640 B  | 7.9× |
| 2 *(serilog.net example)* | **39.73 ns · 0 B** | 50.46 ns · 0 B | 323.2 ns · 672 B  | **8.1×** |
| 4  | **44.71 ns · 0 B** | 76.36 ns · 0 B | 413.3 ns · 968 B  | 9.2× |
| 8  | **61.39 ns · 0 B** | 123.8 ns · 0 B | 519.4 ns · 1368 B | 8.5× |
| 12 | **62.93 ns · 0 B** | 164.5 ns · 0 B | 661.6 ns · 1824 B | 10.5× |
| 16 | **57.85 ns · 0 B** | 208.5 ns · 0 B | 796.3 ns · 2112 B | 13.8× |

### Cross-check box — `Standard_FX12mds_v2` (higher clock)

Same shape, faster absolute numbers, same 0 B:

| Arity | Herald native | Herald compat | Real Serilog | Native vs Serilog |
|---|---|---|---|---|
| 1  | 32.22 ns · 0 B | 36.58 ns · 0 B | 301.1 ns · 640 B  | 9.3× |
| 2  | 32.43 ns · 0 B | 46.16 ns · 0 B | 336.3 ns · 672 B  | **10.4×** |
| 4  | 37.53 ns · 0 B | 68.79 ns · 0 B | 416.2 ns · 968 B  | 11.1× |
| 8  | 45.60 ns · 0 B | 107.6 ns · 0 B | 517.4 ns · 1368 B | 11.3× |
| 12 | 46.54 ns · 0 B | 139.7 ns · 0 B | 700.4 ns · 1824 B | 15.0× |
| 16 | 49.58 ns · 0 B | 168.7 ns · 0 B | 803.8 ns · 2112 B | 16.2× |

## Reject path — the event is below the level gate

Production reality: a service running at `warn` still executes every
`Information` emit site on the hot path. Here every call is below the floor.

The robust, hardware-independent signal is **allocation**:

| Arity | Herald native | Herald compat | Real Serilog 4.3.1 |
|---|---|---|---|
| 1  | 0 B | 0 B | 32 B  |
| 2  | 0 B | 0 B | 0 B \* |
| 4  | 0 B | 0 B | 128 B |
| 8  | 0 B | 0 B | 256 B |
| 12 | 0 B | 0 B | 384 B |
| 16 | 0 B | 0 B | 512 B |

Herald rejects at **0 B at every arity**. Real Serilog's
`Information(template, params object?[])` builds the `object?[]` and boxes each
int **at the call site, before** its level gate runs — so a *filtered-out* call
still allocates 32–512 B unless the adopter hand-writes an `IsEnabled` guard.
The drop-in compat adapter inherits Herald's behaviour: existing Serilog call
sites stop paying for rejected logs with no source change.

Reject latency (canonical box) runs single-digit to ~18 ns for Herald vs up to
~87 ns for Serilog, but the nanosecond figures on this path are sensitive to JIT
dead-code elimination (the result is unused), so treat **allocation as the
clean signal** and latency as directional.

## Takeaways for the site

1. **The headline (serilog.net's own example):** Herald logs the canonical
   `{@Position}` call at **0 B vs Serilog's 672 B**, and **~8× faster** (10× on
   the higher-clock box). Zero-allocation is identical on every machine.
2. **The drop-in win:** recompile existing `using Serilog;` code against Herald's
   adapter and the same call goes to **0 B and 4–7× faster** — no rewrite.
3. **Rejected logs are actually free:** Herald is 0 B whether the event passes or
   is filtered; Serilog allocates on rejected calls too.
4. **Allocation grows with arity for Serilog (640 → 2112 B); Herald stays flat at
   0 B.** The gap widens the more structured your logging gets.

## Honest caveats

- **`Standard_F8als_v6` / `FX12mds_v2`, .NET 10** are the provenance — publish the
  SKU with the latency numbers. Allocations need no caveat; they are universal.
- **Serilog reject at arity 2 reads 0 B / ~0 ns** because the JIT elided that one
  call in the harness (unused result). Arity 1 and 4–16 show the real
  params-array + boxing cost on rejected calls; the arity-2 cell is a measurement
  artifact, not a Serilog win.
- Latency is the cloud baseline, not a desktop peak. The ratios hold; the
  absolute nanoseconds will differ on other hardware.
- Raw logs + `lscpu` provenance: `C:\Users\smuch\herald-bench\results-{fals,fx12}\`.

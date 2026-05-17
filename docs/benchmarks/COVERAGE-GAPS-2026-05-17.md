# Bench coverage gaps - 2026-05-17

## Summary

Herald.OSS competitive matrix today is a thoroughly-measured four-property story plus a Herald-only sixteen-property typed-args measurement. The 16-prop competitor numbers in the rollup are stitched in from a 2026-05-14 Herald.Core citation, not from this repos competitor projects - those projects only ship Zero/One/Four prop benches. The three highest-leverage gaps are (1) competitor 16-prop coverage in-repo, (2) any property-arity coverage between 4 and 16 (8 prop is unmeasured anywhere), and (3) multi-TFM coverage of the accept path on net8/net9 (only the shared 0/1/3 prop bench runs there, while the comparison story runs net10-only).

There is one un-fun finding (G7 below): the rollups MEL allocation matches Herald at 0 B framing is technically true on the BDN Allocated column, but the per-call Gen0/Gen1/Gen2 columns in the same MEL rerun artefact are populated - small but non-zero GC activity that is being rounded to a dash in the headline. Honest framing would either include the Gen0 column or footnote that the zero is sub-byte average.

## Current coverage (what is measured)

| Workload | Property arities | TFMs | Competitors | Last refresh |
|---|---|---|---|---|
| Accept call (competitive) | 0, 1, 4 | net10 only | Herald, MEL, NLog, Serilog, ZLogger, log4net | 2026-05-16 (competitors), 2026-05-14 (Herald cite) |
| Typed-args (Herald only) | 4, 16 - all-strings + mixed | net10 only | none in-repo (16-prop cite from Herald.Core 2026-05-14) | 2026-05-14 (artefact regen on .NET 10.0.8 runtime) |
| Source-gen head-to-head | 4 props (fixed shape) | net10 only | ZLogger, MEL | 2026-05-14 - currently excluded from compilation (see G6) |
| Rejected-call | 0/1/4 props at trace/debug/info, vs warn-accepted | net10 only | none (Herald-only) | 2026-05-14 |
| Redaction | 1 prop, Mask mode | net10 only | none in-repo | 2026-05-14 |
| Hot-reload | level-only fast / structural slow | net10 only | none (Herald-only feature) | 2026-05-14 |
| Hot-reload cutover | 4 prop emits, reload sandwiched | net10 only | none (Herald-only feature) | 2026-05-14 |
| Kernel fan-out | 1 / 2 / 3 / 5 / 8 / 16 sinks, 0 props | net8, net9, net10 (shared) | none (Herald-only) | 2026-05-14 |
| Sink isolation | 5 bridge sinks, 1 prop | net10 only | none (Herald-only feature) | 2026-05-14 |
| Kernel mixed-sink tax | 4 props, kernel vs chain | net10 only | none (Herald-only) | 2026-05-14 |
| MEL adapter | 4 props | net10 only | MEL native, Herald native | 2026-05-14 |
| UTF-8 format | 4 props (fixed template) | net10 only | ZLogger, Serilog | 2026-05-14 |
| Destructure | 5-field POCO, 1 nested ref | net10 only | Serilog | 2026-05-14 |
| Flight recorder | 0-prop trigger / buffer write | net10 only | none (Herald-only feature) | 2026-05-14 |
| Accept path (library bench) | 0 / 1 / 3 props | net8, net9, net10 (shared) | none (Herald-only) | 2026-05-14 |

---

## Gaps (priority-ordered)

### Gap G1: No competitor 16-prop benchmark exists in-repo

- **What is missing:** Every per-competitor csproj under `benchmarking/comparisons/net10/` ships only `ZeroProps / OneProp / FourProps` benches. The 16-prop column in section 2 and Summary, citing MEL inert NullLogger 62 ns, 152 B, comes from `docs/benchmarks/history/run-2026-05-14T19-30Z/typed-args-net10-2026-05-14T19-30Z.md`, which itself attributes the numbers to Herald.Core's published competitive bench, not anything reproducible from this repo.
- **Why it matters:** The README annotation says the 16-prop row is cited from the 2026-05-14 baseline and was not re-run 2026-05-16. Truth is stronger than that: the row has never been measured in this repo at all. A skeptical reviewer running `--filter "*SixteenProps*"` against any competitor dll gets nothing. Customers sizing high-cardinality workloads (audit events, security events, OTLP envelopes) get a 16-prop Herald number with no apples-to-apples competitor benchmark to compare against.
- **Suggested workload:** Add `SixteenProps_AllStrings` and `SixteenProps_MixedTypes` to each of `MEL/AcceptCallBenchmarks.cs`, `serilog/AcceptCallBenchmarks.cs`, `nlog/AcceptCallBenchmarks.cs`, `zlogger/AcceptCallBenchmarks.cs`, and `log4net/AcceptCallBenchmarks.cs`. Match the Herald `TypedArgsBenchmarks` template `"telescope {A}..{P}"` exactly. Net10 only.
- **Effort estimate:** M. Five new bench methods (one per competitor csproj), each about 15 lines mirroring the existing FourProps shape; rerun and update section 2 and the Summary.

### Gap G2: 8-property arity is unmeasured anywhere

- **What is missing:** Herald has an 8-property `LogPropertyBuffer8` shape (the kernel's default inlined size), but no bench measures emit at 8 props. The matrix jumps 4 to 16.
- **Why it matters:** The 4-to-16 jump hides where the cost curve bends. Per-property scaling claims in the typed-args doc (about 0.7-1.2 ns per primitive, about 1.2-1.5 ns per string beyond the 4-prop baseline) are extrapolated from two points. A customer with realistic 6-10 property emits (RPC traces, web request logs) has no measured number; they interpolate. A reviewer can argue the curve is not actually linear without an interior measurement.
- **Suggested workload:** Add `EightProps_AllStrings` and `EightProps_MixedTypes` to `TypedArgsBenchmarks.cs`. Same shape as the existing 4 and 16 bench methods; eight named template tokens; eight pre-allocated string locals for the all-strings case and `string, int, bool, double` twice over for mixed. Net10 first; if G3 lands, copy to net8 and net9.
- **Effort estimate:** S. Two methods in an existing bench file.

### Gap G3: net8 and net9 see only the 0/1/3 prop accept path

- **What is missing:** `benchmarking/library/net8/` and `benchmarking/library/net9/` build the shared project that contains exactly two bench files: `AcceptPathBenchmarks.cs` with 0/1/3 props and `KernelFanOutBenchmarks.cs` with 0-prop fan-out across 1 to 16 sinks. The competitive accept-call bench, typed-args 4 and 16, redaction, hot-reload, MEL adapter, sink isolation - none of these run on net8 or net9.
- **Why it matters:** Herald's README markets "Targets .NET 8, .NET 9, and .NET 10. AOT-clean. Trim-safe." But a customer on net8 LTS has no published number for their TFM beyond a 3-prop accept and a fan-out shape. An AOT-tier customer (the README's explicit claim) gets even less. Performance characteristics meaningfully differ across TFMs because of JIT improvements between net8 and net10; pretending one number covers all three is a sizing risk for the customer.
- **Suggested workload:** Promote the typed-args bench (4-prop and 16-prop, all-strings and mixed) into the shared bench project so it runs across net8, net9, and net10. Same for the accept-call competitive bench against MEL (the only competitor that ships net8 packages cleanly). Do not expand the full competitor matrix to net8 and net9; MEL is the meaningful TFM-comparable competitor.
- **Effort estimate:** M. Move `TypedArgsBenchmarks.cs` into `sharedproject/`, add net8 and net9 MEL competitor csprojs that mirror the net10 one. Three new csprojs, one moved file, three runs.

### Gap G4: All 16-prop coverage is strings or string-and-primitive mixes; no large-string or wide-mix workloads

- **What is missing:** The 16-prop all-strings bench uses 6-9 char strings (alpha, bravo, ..., papa). Real production workloads include URLs (50-200 chars), JSON fragments (200-2000 chars), exception messages, and stack-trace snippets. Nothing in the matrix measures emit cost when property values are larger.
- **Why it matters:** The reviewer argument is straightforward: Herald only optimises when all your strings are tiny. For sinks that materialise the rendered message (UTF-8 format, file sinks) the property-value length dominates downstream cost; for the kernel fast path the per-call cost is bounded by reference store, but render-on-sink workloads should still be measured. The "structured-logging spine for data tracking" ecosystem framing in the README explicitly invites large-payload workloads.
- **Suggested workload:** Add `SixteenProps_LargeStrings` (each value about 256 chars, common URL-like shape) to `TypedArgsBenchmarks.cs`. Pair with a `Utf8FormatBenchmarks` variant at 16 large-string props (the render boundary is where length matters most). Net10 first.
- **Effort estimate:** S-M. One new typed-args method, one new UTF-8 format method, one new section in the rollup acknowledging the render-vs-accept asymmetry honestly.

### Gap G5: No Guid, DateTime, or decimal heavy-prop coverage

- **What is missing:** Mixed-type benches use `int, bool, double, string`. Three property types that show up constantly in real workloads - `Guid` (correlation IDs, request IDs), `DateTime` and `DateTimeOffset` (event time stamps beyond the kernel-populated time), and `decimal` (currency, finance) - are absent.
- **Why it matters:** `Guid` is a 16-byte value type that boxes to a heavier object than `int`. `decimal` is 16 bytes. `DateTime` is 8 bytes but has expensive format paths. If `LogPropertyCompact.From<T>` ScalarBits JIT-specialization handles `Guid` and `decimal` differently than `int`, the per-call cost in the audit and finance use case is unknown. The Compliance and Audit modules in the ecosystem absolutely emit Guids on every event.
- **Suggested workload:** Add `FourProps_AuditShape` (`Guid correlationId, DateTimeOffset eventTime, string actor, string action`) and `FourProps_FinanceShape` (`Guid txId, decimal amount, string currency, DateTimeOffset settledUtc`) to `TypedArgsBenchmarks.cs`. Pin per-shape allocation honestly; if Guid boxes, the bench should show 24 B and the doc should say so.
- **Effort estimate:** S. Two methods, one csv update.

### Gap G6: Source-gen comparison row is currently un-buildable

- **What is missing:** `Herald.Comparison.csproj` lines 33-35 exclude `SourceGenBenchmarks.cs` from compilation, with the comment "Pre-existing visibility bug; out of scope for this rerun." Section 7 source-gen head-to-head in the rollup cites `source-gen-net10-2026-05-14T23-10Z.md` as if it is still reproducible from this repo. A reviewer running the README's `dotnet ... --filter "*"` against the herald comparison dll today gets no source-gen rows at all.
- **Why it matters:** Source-gen is one of Herald's headline differentiators (the README explicitly calls out `[HeraldLog]`). The 26.73 ns / 0 B number is the strongest "we beat ZLogger by 5x and MEL by 6x at the same workload" claim. Right now the claim is documented but not reproducible; a reviewer cannot verify it from this repo. The longer the exclusion stays, the more the claim drifts from the runtime.
- **Suggested workload:** Not a new bench. Fix the `StructuredLogger.RecordCompileTimeResolution()` visibility so `SourceGenBenchmarks.cs` recompiles. Then rerun the existing bench and refresh the section 7 citation timestamp. The bench shape itself is fine.
- **Effort estimate:** S-M. Depends on whether the visibility fix is one internal-to-public flip or implies a generator-emit change.

### Gap G7: MEL 0 B allocated headline understates Gen0 pressure in the rerun artefact

- **What is missing:** `docs/benchmarks/runs/run-2026-05-16-comp-rerun/MEL/results/MMP.Herald.OSS.Benchmarks.Comparisons.MelRow.AcceptCallBenchmarks-report-github.md` shows for `Mel_FourProps`: Mean 160.04 ns, Gen0 0.0041, Gen1 0.0002, Gen2 0.0002, Allocated dash. The rollup picks up the dash in Allocated and reports 0 B everywhere it appears. BDN Allocated column is rounded to whole bytes; non-zero Gen0 means there is heap pressure being averaged to sub-byte, not eliminated. The Mel_OneProp row in the same artefact reports 104 B for context.
- **Why it matters:** This is the un-fun finding. The Summary line MEL is now allocation-equivalent to Herald on that row is technically true on the displayed column but framing-misleading if Herald same column would also read dash with Gen0 0 (genuinely zero) vs MEL Gen0 0.0041. The README and the rollup both quote this as a parity claim. A reviewer who reads the raw artefact will notice and lose trust in the rest of the table.
- **Suggested workload:** Not a new bench. Re-render section 1 and Summary to either (a) include the Gen0/Gen1 columns alongside Allocated for the four-prop row, or (b) footnote Allocated under 1 B per call but Gen0 about 0.0041; MEL allocates on a sub-1%-of-calls path, roughly 1 in 250 calls. Apply the same check to the One-prop row where MEL is 104 B explicitly.
- **Effort estimate:** S. Doc edit, no rerun needed. If the framing change feels expensive, just include the Gen0 column on the four-prop competitive table.

### Gap G8: No long-template / high-placeholder-density workload

- **What is missing:** Every bench template is short (under 80 chars) with the placeholder density a developer would naturally write. Templates of 256 / 512 / 1024 chars with 16 or more placeholders are absent. The closest is the source-gen bench template "User {userId} purchased {sku} for {price} at {timestamp}", about 60 chars and 4 placeholders.
- **Why it matters:** Template parsing is on the cold path for cached compiles but on the hot path the first time a template is seen; an analytics/compliance overlay generating templates programmatically (the ecosystem framing) sees a lot of unique templates. If a customer workload has 1 KB OpenTelemetry semantic-convention templates with 24 placeholders, the per-call story may diverge from the 4-prop number.
- **Suggested workload:** One new bench file `TemplateLengthBenchmarks.cs` with three rows: `Template_64chars_4placeholders`, `Template_256chars_8placeholders`, `Template_1024chars_16placeholders`. All-strings property values, kernel-eligible null sink. Net10 only. Optional: a first-call-vs-warm-cache pair so the template-parse amortization is visible.
- **Effort estimate:** M. New bench file, three methods, new rollup section.

### Gap G9: No cold-category / category-churn workload

- **What is missing:** Every bench reuses `LogCategory.App` on every call. The kernel category lookups warm up immediately. A workload that calls `Info(LogCategory.From("category-" + i), ...)` with i unique per call is unmeasured.
- **Why it matters:** Real telemetry libraries (OTLP receivers, metric exporters, multi-tenant routers) generate per-call distinct categories. If `LogCategory.From` allocates or contends on a cache, the per-call cost in those workloads is unknown. The ecosystem framing explicitly puts Herald.OSS under analytics overlays and OTLP, workloads where category churn is real.
- **Suggested workload:** Add to a new `CategoryChurnBenchmarks.cs` (or extend `AcceptPathBenchmarks.cs`): `Info_WarmCategory_Single` (reference, today number), `Info_CategoryFromString_Cold` (calls `LogCategory.From(string)` with a fresh string per call), and `Info_CategoryRotation_64` (rotates across a fixed set of 64 categories, the realistic upper bound for telemetry tags). Net10 first.
- **Effort estimate:** M. New bench file or extension, three methods, brief rollup acknowledgement of category-resolution cost.

### Gap G10: Sustained throughput / saturation curve is implicit and never measured

- **What is missing:** Every Herald number is nanoseconds per BDN iteration. None of the benches answer what is the events/sec ceiling under sustained load with a real sink that does I/O-shaped work. Sink isolation measures resilience (one throwing sink, others survive); hot-reload cutover measures integrity (no loss across swap, 3.28M iterations is mentioned but not as a throughput claim). Neither claims a steady-state throughput.
- **Why it matters:** 27 ns per call is correct but a customer sizing the spine for high-volume telemetry (ecosystem framing again) will ask "so what is events/sec per core?" The honest answer is in the per-call number, but the rollup does not translate it and does not account for sink back-pressure. A reviewer can argue the per-call number is a microbench artefact that does not survive a real sink. The current matrix has no answer.
- **Suggested workload:** One sustained-emit bench: `ThroughputBenchmarks` that emits 1M events through a kernel-eligible counting sink with `[Benchmark]` `OperationsPerInvoke` set so BDN reports events/sec rather than ns/event. Pair with a variant where the sink does about 100 ns of synthetic work (Thread.SpinWait equivalent) to show the realistic ceiling when a real sink is in the loop.
- **Effort estimate:** M-L. One new bench file, careful methodology doc (sustained throughput is more methodology-sensitive than per-call latency).

### Gap G11: Redaction-at-high-arity is unmeasured

- **What is missing:** The redaction bench is a single-property emit with a single rule. The realistic compliance workload is "16-property event with 2-3 redaction rules firing on a subset." The bench +8 ns for fast redaction headline is a 1-prop number with 1 rule.
- **Why it matters:** Compliance is explicitly part of the ecosystem story (Herald.Compliance module in MEMORY.md). A compliance customer sizing the redaction cost on real audit events (PAN, SSN, email, name; 4 or more rules across 16 properties) has no in-repo number. The Mod-Op-Risk MRM committee will not accept "+8 ns scales linearly" as a substantiation.
- **Suggested workload:** Extend `RedactionBenchmarks.cs` with `WithFastRedaction_SixteenProps_TwoRulesFire` and `WithCompiledRedaction_SixteenProps_TwoRulesFire`. Reuse the existing rule shape but configure two rules (mask Email and drop Password) against the 16-prop telescope template. Net10 only.
- **Effort estimate:** S. Two new methods, existing bench file, refresh section 4.

### Gap G12: log4net 16-prop row is structurally weak; needs acknowledgement, not measurement

- **What is missing:** log4net uses positional `{0}..{15}` placeholders via `InfoFormat`, not structured `{Name}` templates. A 16-placeholder log4net call is a fair API translation but the comparison is increasingly apples-to-oranges as arity grows. The current 4-prop log4net row already acknowledges this in code comments.
- **Why it matters:** When G1 lands (16-prop competitor coverage), a reviewer will see log4net 16-prop number sit alongside Serilog/NLog/MEL/ZLogger and ask "is that comparable?" The honest answer is no, log4net does not have structured logging, the comparison is a formatter-cost comparison. Better to acknowledge that in section 1 / section 2 now than to ship a row that invites the question.
- **Suggested workload:** Doc-only. When G1 lands, add a "(no structured templates; positional only)" footnote to log4net 16-prop row. Consider whether to drop log4net from the 16-prop table entirely on the grounds that the comparison stops being meaningful at that arity.
- **Effort estimate:** S. Doc edit at G1 landing time.

---

## Stale-competitor inventory

Rows whose cited numbers come from artefacts dated before the 2026-05-16 competitor rerun. Each should be regenerated before the next consolidated rollup ships.

| Rollup section | Cited artefact | What is stale |
|---|---|---|
| Section 2 Typed-args (16-prop competitor citation) | `history/run-2026-05-14T19-30Z/typed-args-net10-2026-05-14T19-30Z.md` (which itself cites Herald.Core) | All five competitor numbers (NLog 918, ZLogger 693, Serilog 515, MEL inert 62, log4net 187). Not regeneratable from this repo at all (G1). |
| Section 7 Source-gen head-to-head | `source-gen-net10-2026-05-14T23-10Z.md` | Bench is excluded from compilation (G6). Number is from .NET 10.0.7 runtime; current is 10.0.8. |
| Section 8 MEL adapter | `mel-adapter-net10-2026-05-14T23-10Z.md` | MEL native 152.41 ns predates the 2026-05-16 MEL 10.0.8 rerun that re-measured Mel_FourProps at 160.04 ns. Internal inconsistency: same workload, two numbers, the headline picks the older one. |
| Section 9 UTF-8 format | `utf8-format-net10-2026-05-14T23-10Z.md` | Serilog 4.3.1 / ZLogger 2.5.10 pin versions match the 2026-05-16 rerun but the bench artefact predates the rerun batch. |
| Section 10 Sink isolation | `sink-isolation-net10-2026-05-14T23-10Z.md` | No competitor in this bench, but runtime is .NET 10.0.7. |
| Section 11 Kernel mixed-sink | `kernel-mixed-sink-net10-2026-05-14T23-10Z.md` | Runtime is .NET 10.0.8 per the doc (correct), but the herald bench dll itself was not part of the rerun batch. Refresh would be consistency, not correctness. |
| Section 12 Destructure | `destructure-net10-2026-05-14T23-10Z.md` | Serilog 4.3.1 matches rerun version, but the artefact predates the rerun batch. |
| Section 13 Hot-reload cutover | `hot-reload-cutover-net10-2026-05-14T23-10Z.md` | Herald-only; no competitor version concern. Runtime check only. |

**Section 1 Summary footnote about Herald cited from 2026-05-14 baseline**: this is not stale-competitor; it is stale-self. The 4-prop Herald number (27 ns) on the .NET 10.0.8 runtime is available in the on-disk artefact `benchmarking/comparisons/net10/herald/results/results/MMP.Herald.OSS.Benchmarks.Comparisons.HeraldRow.TypedArgsBenchmarks-report-github.md` (which shows 27.16 / 26.65 / 47.27 / 40.44 for the four typed-args shapes from the same TFM as the competitor rerun, runtime .NET 10.0.8). The footnote framing implies we could not run Herald; actually Herald ran in the same window and the result file exists. Worth re-rendering section 1 with the matched-TFM Herald number from this artefact rather than the older 27.0 ns cite.

---

## Out of scope for this round

- **Adding async sink benches (LogAsync path).** The matrix is sync-emit only. Adding async would require a methodology stance on how to measure async without inflating numbers with task allocation. Worth a separate proposal, not a bench-gap inventory item.
- **Adding cross-process or network sink benches.** OTLP-receive-then-emit (proposed under ecosystem-relevant) opens a methodology can: the network and serialization cost dominate, and the in-repo bench infrastructure is not set up for it. Defer to a separate integration-bench proposal.
- **Adding the audit-chain emission scenario.** Compliance lives in `Modules/Herald.Compliance/`, not in OSS. The audit chain is observable from OSS via `HmacChainLogger` (in Core, not OSS per memory note); the spine framing invites the question but OSS does not ship the relevant decorator.
- **Mutation testing or fuzz-testing-as-bench.** Different discipline (mutation-testing skill, separate workflow). Not a bench-coverage gap.
- **Multi-CPU / NUMA scaling benches.** Real but separate methodology problem; the rollup is single-thread per-call latency. Would dilute the matrix central message if added casually.
- **A 32 / 64 property arity bench.** Herald typed-args overloads cap at 16 (`LogPropertyBuffer16` is the largest `[InlineArray]` shape). Going higher means `params ReadOnlySpan<LogProperty>`, a different API and a different cost story; separate row, not an arity extension.
- **A "what if the redactor is misconfigured or regex-bombs" bench.** Resilience question, not throughput question. Belongs in a chaos-experiment proposal, not a competitive matrix.

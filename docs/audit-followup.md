# Audit follow-up queue

Findings from the 2026-05-15 architect + the-fool audit that didn't ship in
the safe-mechanical batch. Each entry has the finding, the constraint that
deferred it, the files involved, a sketch of the fix, and a rough cost.

The batch that did ship (kernel try/catch isolation, `MMP LLC` → `MMPWorks
LLC` owner-string normalization) lives in the most recent commit on `main`.
Everything below is "real engineering" — pick up one at a time.

---

## 1. WithContext kernel orphan (Fool #2b)

**Finding.** `StructuredLogger.WithContext` returns a child logger whose
`Log*` calls dispatch through the chain path, not the kernel. Callers who
adopt `WithContext` lose the kernel's fast path silently — no warning, no
diagnostic. The Fool flagged this as a "the kernel decoration is invisible
to the caller" surprise: a perf-sensitive callsite that adds `WithContext`
for a correlation id drops back to the chain's per-event allocation cost.

**Why deferred.** This is design work, not a mechanical fix. Options:
1. Recompile the kernel for each WithContext child (cheap at construct
   time, free at hot-path time — but multiplies the kernel-cache count by
   the number of distinct context dictionaries).
2. Thread context as a `LogEventBuffer` field so the kernel can carry it
   without recompilation.
3. Document the cost and leave the chain fallback in place.

The user has to pick one. Each choice has consequences for the kernel's
construction-time budget, the buffer's stack footprint, and the hot-path
shape.

**Files involved.**
- `native/dotnet/Pipeline/StructuredLogger.cs` — `WithContext` factory
- `src/Pipeline/Kernel/LogEventBuffer.cs` — would gain a context field
  under option 2
- `src/Pipeline/Kernel/KernelCompiler.cs` — option 1 caching shape

**Cost.** Medium — option 2 is the lightest touch but breaks the
buffer's tiny-struct discipline; option 1 needs a cache key on the
context dictionary's identity.

---

## 2. HeraldManagementApi divergence (Architect #5)

**Finding.** `src/Addons/ManagementApi/HeraldManagementApi.cs` in
Herald.OSS has drifted from
`Modules/Server/ManagementApi/CoreAddons/HeraldManagementApi.cs` in the
proprietary monorepo. The two files share a common ancestor but have
diverged on the `MinimumEdition` reporting fields, the network-sink
dispatch shape, and the schema-emission code. The Architect flagged this
as a cross-repo refactor that needs its own focused effort to resolve —
either reconcile the two files or split them clearly.

**Why deferred.** Cross-repo refactor. Picking a direction (which side is
authoritative, how do they stay in sync, do they share a generator)
needs the user's input.

**Files involved.**
- `E:/dev/Herald.OSS/src/Addons/ManagementApi/HeraldManagementApi.cs`
- `E:/dev/Herald/Modules/Server/ManagementApi/CoreAddons/HeraldManagementApi.cs`
- The shared schema sources in both trees

**Cost.** High — touches public Management API surface in both repos
and needs a story for how they stay aligned over time.

---

## 3. Hot-reload test coverage (audit cross-cutting)

**Finding.** The audit asked for confirmation that hot-reload preserves
every relevant `StructuredLogger` field — `IsXxxAcceptable`, the kernel
binding, the gate-enabled flag, the level-only fast path. The
`StructuredLoggerHotReload*Tests` suite covers some of these but is
missing pinned tests for:
- `IsDebugAcceptable` flips from `false` to `true` when the minimum
  level drops from Information to Debug via a level-only reload (the
  Fool's #2 finding — see entry 6 below)
- `IsCriticalAcceptable` flips from `true` to `false` when the minimum
  rises to "None"
- Kernel rebinding after a sinks-only reload (sanity for the existing
  `SafeCompositeLogger.SwapChildren` path)

**Why deferred.** The tests need careful seed setup; the level-only
reload path doesn't currently update the `IsXxxAcceptable` fields, so
writing the test now would record a bug as expected behavior. Sequence
the test work after entry 6 lands.

**Files involved.**
- `tests/Pipeline/StructuredLoggerHotReloadTests.cs` (and siblings)
- New: `tests/Pipeline/Kernel/KernelHotReloadTests.cs` for the
  rebinding sanity test

**Cost.** Low — once the fix in entry 6 ships, the tests are mechanical.

---

## 4. Edition residue removal (Phase 3A — RESOLVED in 0.2.0)

**Status.** Stripped in the 0.2.0 release: `src/HeraldEdition.cs`
deleted, `MinimumEdition` removed from `ILogSinkProvider`,
`HeraldTenant.EnsureAllowedForCurrentEdition` removed along with its
two call sites in `HeraldRegistryInstance`, the `Edition` column in
`src/Addons/README.md` rewritten as "Tier (informational)" with a
banner noting the value is non-enforcing in OSS. See `CHANGELOG.md`
0.2.0 entry. Downstream cascade in `E:/dev/Herald/Modules` (sinks +
server + compliance) is tracked as a separate dispatch; the breaking
change is recorded here so the next coordinated release knows the
direction.

The original deferral notes are kept below for context.

---

### Original deferral (historical)

**Finding.** Herald.OSS still carries the `HeraldEdition` enum, the
`MinimumEdition` property on `ILogSinkProvider`, and an empty
`HeraldTenant.EnsureAllowedForCurrentEdition` method. The Architect
flagged this as "OSS has no editions" residue.

**Why deferred — survey verdict UNSAFE.** Downstream consumers in
`E:/dev/Herald/Modules` make heavy use of these types:
- `Herald.Server/Program.cs` calls `HeraldEditionGate.AllowsFeature(HeraldEdition.Pro)`
  at runtime to decide whether to bind TLS endpoints
- `Herald.Server/ManagementApi/HeraldJwtAuth.cs` calls
  `HeraldEditionGate.Require(HeraldEdition.Pro, "Multi-key JWT rotation")`
- All 60+ sinks in `Herald.Sinks` set
  `public HeraldEdition MinimumEdition => HeraldEdition.Community;`
- `Herald.Pro/RetryingLogger.cs`, `DurableBufferLogger`, `CircuitBreakerLogger`,
  `FallbackLogger` all declare `MinimumEdition = Pro`
- `Herald.Enterprise/AuditLogger.cs`, `EnterpriseLicenseGate` declare
  `MinimumEdition = Enterprise`
- The MSBuild property `HeraldEdition` is consumed by `Lean`, `Server`,
  `Core` build scripts and every SampleApp csproj

Stripping from OSS without coordinating with the proprietary repos
would brick every downstream build.

**Suggested fix shape.** This is a packaging decision, not a code
decision. Two viable paths:

1. **Keep HeraldEdition in Herald.OSS as a no-op contract type.** OSS
   ships the enum and the `MinimumEdition` property; OSS-only consumers
   never read the values, so they're functionally inert. The proprietary
   side keeps using them as gates. The README catalog table re-frames
   the `Edition` column as "informational, OSS does not enforce."
2. **Move HeraldEdition out of OSS into a downstream-only shim package.**
   OSS drops the type; the proprietary side defines an
   `MMP.Herald.Editions` package that re-introduces it for the
   Pro/Enterprise gates. Every downstream sink and decorator gains a
   new package reference.

Option 1 is the least disruptive and matches the
`[OSS strip Shape A confirmed]` memory note: "Multi-tenancy + plugin
trust ship as structural features (no gate). Gate enforcement is
Enterprise-only." The gate stays Enterprise-only; the type just exists
in OSS as a contract surface.

**Files involved (OSS side, option 1 — no changes; option 2).**
- `src/HeraldEdition.cs` — delete
- `src/Routing/ILogSinkProvider.cs` — remove `MinimumEdition`
- `src/Quick/HeraldTenant.cs` — remove `EnsureAllowedForCurrentEdition`
- `src/Addons/README.md` — drop or rewrite the `Edition` column

**Cost.** Option 1: trivial documentation pass. Option 2: high —
introduces a new package + coordinates across all downstream repos.

---

## 5. GenSource strip (Phase 3B — RESOLVED in 0.2.0)

**Status.** Stripped in the 0.2.0 release: `GenSource` removed from
`LogEvent`, `LogEventBuffer`, `LogEventFactory`,
`DeferredLogEventFactory`, `ILogEventFactory`; `_genSource` plumbing
removed from `StructuredLogger`, `DefaultLogPipelineFactory`,
`HotPathLogger`, `WindowedMeanLogger`. `FORK_SCOPE.md` §2 rewritten
to say the strip is now complete. Downstream commercial wrappers
that need a provenance carrier can stamp the value into
`Context["gen_source"]` instead.

The original deferral notes are kept below for context.

---

### Original deferral (historical)

**Finding.** `LogEvent.GenSource`, `LogEventBuffer.GenSource`, and the
threading of `_genSource` through `StructuredLogger` /
`DefaultLogPipelineFactory` are still present in Herald.OSS. The
Architect flagged this as "OSS doesn't gate on provenance" residue.

**Why deferred — survey verdict UNSAFE.** Downstream uses are real,
not cosmetic:
- `Herald.Compliance/src/Audit/CanonicalEventBuilder.cs` writes
  `["gen_source"] = logEvent.GenSource` into the canonical audit
  payload — removing it changes the audit-chain hash shape and breaks
  every existing audit record
- `Herald.Plugins.Templating.Binding/HeraldBind.cs` stamps `GenSource`
  on every event it emits (the security-key derivation path)
- `Herald.Plugins.Templating.Binding.Tests/HeraldBindTests.cs` asserts
  `evt.GenSource.Should().Be(derivedKey)` for the security model
- `Core/tests/Pipeline/Kernel/GenSourceGatedSinkTests.cs` exercises the
  full provenance-gating contract
- The kernel's `GenSourceGatedSink` decorator is the enforcement point
  for "events without a stamp are treated as untrusted"

OSS doesn't enable the gate by default, but the field has to exist on
the heap event for the audit chain to hash it. Stripping it from
`LogEvent` would force a coordinated rewrite of the
`CanonicalEventBuilder` audit format — that's a versioning event for
every signed audit log.

**Suggested fix shape.** Two paths:
1. Keep `GenSource` on `LogEvent` and `LogEventBuffer` as an OSS
   field, drop only the *gate* enforcement (which is already the
   current state — gated sinks are opt-in, not on by default). Update
   `FORK_SCOPE.md` §2 to clarify "GenSource is the provenance carrier;
   gate enforcement is Enterprise-only" rather than "GenSource is
   gone."
2. Move `GenSource` to a separate `LogEventProvenance` record carried
   through `Context["gen_source"]`. This is a heavy refactor — every
   downstream call site that reads `event.GenSource` would need a
   context-dictionary lookup.

Option 1 is the right answer. The strip the Architect asked for was
based on "OSS doesn't enforce gating", but the field is more than the
gate — it's the audit carrier. Option 1 keeps the field, clarifies the
doc, and matches the actual security model.

**Files involved (option 1 — doc only).**
- `FORK_SCOPE.md` §2 — rewrite the strip claim
- `src/Events/LogEvent.cs` — clarify the GenSource xmldoc to say
  "carried always; gate enforcement is downstream-opt-in"

**Cost.** Low for option 1; medium-to-high for option 2 (touches the
audit-chain format, requires a compat shim for already-signed logs).

---

## 6. IsXxxAcceptable hot-reload bug (Fool #2, Phase 3C — RESOLVED in 0.2.0)

**Status.** Fixed in the 0.2.0 release. `IsTraceAcceptable` ..
`IsCriticalAcceptable` on `StructuredLogger` and `HotPathLogger`
flipped from `public readonly bool` fields to properties backed by
`Volatile.Read` over private fields. Each type gains an internal
`RecomputeAcceptables(LogLevel?)` method that the level-only branch
of `HotReloadableLoggingBootstrap.ExecuteReload` now calls so
source-gen-emitted code reading `logger.IsDebugAcceptable` sees the
new minimum after a level-only reload. The dedicated test class
`IsXxxAcceptableHotReloadTests` pins the behaviour: construction
with Info minimum rejects Debug; `RecomputeAcceptables(Debug)` flips
it to true; raising the minimum to Error flips Warn/Info to false;
passing null reverts to accept-all. All four tests pass on net8 /
net9 / net10.

The original deferral notes are kept below for context.

---

### Original deferral (historical)

**Finding.** `IsTraceAcceptable` .. `IsCriticalAcceptable` on
`StructuredLogger` and `HotPathLogger` are `public readonly bool` fields
set in the constructor. The `IsLevelOnly` hot-reload path updates
`_currentGlobalSwitch.MinimumLevel` but does NOT update these fields.
Source-gen-emitted code (`HeraldLogGenerator`) reads
`logger.IsDebugAcceptable` and short-circuits before constructing the
arg buffer; after a level-only reload that lowers the minimum, the
short-circuit keeps using the stale `false` value and Debug events are
silently dropped.

**Why deferred — binary-compat caution.** Source-gen output is emitted
into the *consumer's* assembly, so it recompiles against the new
Herald.OSS version each time. That makes a field → property flip safe
at the *source* level. But Herald.OSS 0.1.1 has already shipped to
NuGet with these as fields; any pre-compiled consumer library that
links against 0.1.1 by `ldfld` would break on the property flip when
that consumer upgrades to 0.2.0. The risk is small (most consumers
recompile their source-gen output each build), but the safer ship is
to bundle this with the next clearly-marked breaking-changes release.

**Suggested fix shape.** When the next minor (0.2.x) or v1.0 lands:
- Convert `public readonly bool IsXxxAcceptable` to
  `public bool IsXxxAcceptable => Volatile.Read(ref _isXxxAcceptable);`
- Add `private bool _isXxxAcceptable` backing fields, initialised in
  the constructor by the same `EvalAccept` calls
- In `StructuredLogger.ExecuteReload`'s level-only branch, recompute
  each `_isXxxAcceptable` from the new minimum and `Volatile.Write`
  it
- Mirror the change in `HotPathLogger.cs` (same field shape, same bug)
- Add a unit test: build a logger with minimum Information, verify
  `IsDebugAcceptable` is `false`, trigger a level-only reload to
  Debug, verify `IsDebugAcceptable` is now `true`

**Files involved.**
- `native/dotnet/Pipeline/StructuredLogger.cs` — fields + ctor +
  ExecuteReload
- `src/Addons/GamePerformance/HotPathLogger.cs` — same shape
- `tests/Pipeline/StructuredLoggerHotReloadTests.cs` — new test
- `CHANGELOG.md` — "Breaking: `IsXxxAcceptable` is now a property.
  Source-gen consumers recompile cleanly; pre-compiled consumer
  assemblies linking by field reference must rebuild."

**Cost.** Low. The implementation is mechanical once the binary-compat
break is acknowledged in the release notes.

---

## Reference — what shipped in the safe-mechanical batch

For context. Skip if you already know.

- `MMP LLC` → `MMPWorks LLC` owner-string normalization across 10
  source files.
- `KernelCompiler.cs` — every fan-out shape (Single, Pair, Triple,
  Many) wraps the `sink.Log(in buffer)` call in try/catch. A throwing
  sink no longer kills its peers. Failures route to
  `System.Diagnostics.Trace.WriteLine` with a
  `[Herald.OSS] kernel sink threw` prefix. `AuditLogFailureException`
  still propagates — matches `SafeCompositeLogger` semantics. A
  failure-sink delegate could be threaded through `CompileFanOut`
  later if a richer audit trail is needed; for now the kernel stays
  BCL-only and AOT-clean.
- New test file: `tests/Pipeline/KernelFanOutFailureIsolationTests.cs`
  with five tests pinning the isolation contract.

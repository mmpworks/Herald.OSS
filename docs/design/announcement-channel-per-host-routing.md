# ADR: Route Herald's own runtime notices to the owning host's channel

- Status: Proposed (design-only; implementation sequenced AFTER Glenn's net8 un-gating lands)
- Date: 2026-06-02
- Author: Richard (architecture-designer)
- Pairs with: docs/design/external-event-injection-switch.md (the section 7.6 gate + injection-refusal contract this ADR must preserve).
- TFMs in scope: net8 / net9 / net10 (net8 is now release-gating, equal-tier).

## Context

Herald emits a small set of framework-tier runtime notices — diagnostic signals that must NOT go through the consumer's logging pipeline (that would put framework lines in tenant-scoped sinks). They are published on a parallel surface, the runtime-message channel.

Today every framework writer calls the static facade HeraldRuntimeMessages.Publish(...), which forwards to HeraldHost.Default.RuntimeMessages — one process-wide HeraldRuntimeMessagesInstance with one bounded buffer and one OnNotice invocation list. That static buffer is the bleed surface.

### The flake class, precisely

The naming-policy announcement (and RecordCompileTimeResolution) fire through a deferred path: EnsureAnnouncementFired() flips a per-logger latch with Interlocked.CompareExchange, then queues the publish with ThreadPool.UnsafeQueueUserWorkItem (see native/dotnet/Pipeline/StructuredLogger.Naming.cs, EnsureAnnouncementFired / FireAnnouncement). The publish lands off-thread, a moment later.

Under parallel xUnit runs, a deferred announcement scheduled by test N can land on the shared HeraldHost.Default buffer during test N+1 — after N+1 called ClearRecent() — inflating N+1's count. We have corralled four symptoms:

1. tests/Helpers/DefaultHostCollection.cs — a DisableParallelization collection that serialises every class touching HeraldHost.Default.
2. tests/Helpers/EditionStateCollection.cs — same pattern for the edition/cap static slot (a sibling shared-static problem, NOT the notice buffer).
3. Scoped-id filters inside NamingPolicyAnnouncementTests (lines ~104 and ~299/301): a .Where on Properties.Single().Value == "pascal" — defence-in-depth against residue on the shared buffer.
4. The spin-then-filter pattern in AnnouncementSpinHelpers + PipelineBridgeConsentForwardingTests / ExternalEventInjectionTests, which all read the shared buffer and filter for their own notices.

These are per-test corrals. Steve's ruling: kill the class at the source.

### The five writers, mapped

| # | Writer | File | Path | Has a host today? |
|---|--------|------|------|-------------------|
| 1 | Naming-policy announcement | StructuredLogger.Naming.cs -> FireAnnouncement (line ~381) | deferred thread-pool | No — calls static facade |
| 2 | Injection-switch refusal (sec 7.1) | StructuredLogger.Injection.cs -> RefuseExternalInjection (line ~107) | synchronous, one-shot latch | No — calls static facade |
| 3 | sec 7.6 gate rejection | Pipeline/Kernel/GenSourceGatedSink.cs -> EmitRejectionNotice (line ~287) | synchronous, one-shot latch | No — sink-side, no host |
| 4a | NameResolverCache cap-hit | Templating/NameResolverCache.cs (line ~392) | synchronous, throttled CAS | No — static cache |
| 4b | SerilogTemplateHoleIndex cap-hit | Serilog/SerilogTemplateHoleIndex.cs (line ~199) | synchronous, throttled CAS | No — static/per-process cache |
| 5 | ManagementApi unconfined-path warning | Addons/ManagementApi/HeraldManagementApi.cs (line ~397) | synchronous | No — API-side, no logger |

The infrastructure for per-host channels already exists: HeraldHost.RuntimeMessages is a per-host HeraldRuntimeMessagesInstance (see src/Quick/HeraldHost.cs), and the static HeraldRuntimeMessages facade already forwards to HeraldHost.Default.RuntimeMessages. The gap is that no writer is told which host built me — every writer reaches for the static facade, so everything lands on Default. **The root cause is a missing host reference on the publishing types, not a missing channel.**

A second, independent shared-static problem rides along: writers 4a/4b are static caches with a process-global throttle latch (_lastCapHitNoticeTicks). Even if we gave them a channel, the throttle state is global. Channel routing cannot isolate a global latch. This matters for the honesty of the payoff (see Test-scaffolding payoff).

## Decision

Adopt two-tier routing with a default host that aggregates, not collects.

### Tier 1 — Logger-scoped notices route to the owning host's channel

Thread a single channel reference — HeraldRuntimeMessagesInstance — into StructuredLogger at construction, and have writers 1, 2, and 3 publish through that instance instead of the static facade.

- StructuredLogger gains one field: a readonly HeraldRuntimeMessagesInstance _runtimeMessages, defaulting to HeraldHost.Default.RuntimeMessages when a builder doesn't supply one (preserves today's behaviour for every existing caller).
- QuickLogBuilder already lives on a HeraldHost (it consumes HeraldHost.Default.* registries). The build site (PipelineAssemblyBuilder.Build) passes the builder's host's RuntimeMessages down into the new constructor parameter.
- **ForContext MUST propagate _runtimeMessages by reference** (same discipline as _kernelHolder). A child logger that fell back to Default would re-open the bleed through the back door. This is non-negotiable and gets its own regression test.

Writers 1–3 change from HeraldRuntimeMessages.Publish(...) (static) to _runtimeMessages.Publish(...) (instance). For writer 3 (GenSourceGatedSink), the gated sink does NOT gain a pipeline back-reference (honours the sinks-stay-dumb invariant) — it receives the same channel instance the logger holds, passed at sink construction by the pipeline assembler, exactly as the logger receives it. If the sink construction path cannot cleanly carry the channel, writer 3 stays on Default (Tier 2) and keeps its corral — the ADR does not force it.

### Tier 2 — Genuinely host-less notices stay on Default, by nature

Writers 4a, 4b, 5 have no logger and no host. They keep publishing to HeraldHost.Default.RuntimeMessages via the static facade. This is correct, not a compromise: a process-global cache emits a process-global notice. Their corrals (NameResolverCacheCapHitNoticeTests, SerilogTemplateHoleIndexCapHitNoticeTests, both on DefaultHostCollection) STAY. The throttle-latch state is global too, so these tests need serialisation regardless of channel design.

### The load-bearing move — Default observes, it does not just collect

The injection-refusal contract requires operators to keep watching one place (HeraldRuntimeMessages.OnNotice, the static facade = Default). If Tier-1 notices route to a non-default host's channel, an operator subscribed to the static facade would stop seeing them. That silently weakens un-silenceable to silenceable-by-constructing-your-own-host.

To preserve the contract: the Default host's channel re-broadcasts an aggregation of per-host notices to its own OnNotice. Concretely — when a non-default HeraldRuntimeMessagesInstance is created, it is registered with an aggregation hub that forwards each non-default channel's OnNotice onto HeraldHost.Default.RuntimeMessages.OnNotice (event re-raise only — it does NOT write into Default's buffer). Operators watching the static OnNotice see every notice from every host. Per-host buffers (RecentNotices) stay isolated — which is exactly what kills the test-bleed, because tests assert on RecentNotices (the buffer), not on a live OnNotice subscription.

This is the critical separation:

- **Buffers are per-host and isolated** -> test-bleed dies (tests read buffers).
- **OnNotice aggregates to Default** -> operator observability is preserved (operators subscribe to the live event).

The aggregation must be opt-in-safe for tests: a test that constructs its own host and asserts on that host's buffer is unaffected by aggregation (it isn't reading Default's OnNotice). A test that genuinely needs to assert no-aggregation-cross-talk gets one targeted test, not a corral on every class.

## Why this routing preserves the injection-switch contract

The injection-switch refusal notice keeps all four properties:

| Property | How it survives |
|----------|-----------------|
| **Un-silenceable** | RefuseExternalInjection still has no suppression knob. The only change is the destination instance. The one-shot latch (_externalInjectionNoticeFired) is per-logger and unchanged. |
| **Out-of-band from the user log feed** | Still published on the runtime-message channel, never through _pipeline. The instance is a channel, not a sink. |
| **Operator-observable** | The owning host's buffer holds it AND Default's OnNotice re-raises it via aggregation. An operator watching the static facade sees every refusal from every host — same place they look today. |
| **Loud, non-throwing** | Publish already snapshots the invocation list and swallows subscriber throws (HeraldRuntimeMessagesInstance.Publish). Instance vs static does not change that. Severity stays Warning. |

The sec 7.6 gate notice (EmitRejectionNotice) surfaces identically: same channel type, same one-shot latch, same @herald.runtime.gen-source-gate source token. If it routes per-host (Tier 1), operators still see it via Default aggregation; if construction can't carry the channel cleanly, it stays on Default (Tier 2) with no contract change — operators look in the same place either way.

## Alternatives considered

- **Publish to BOTH owning host AND Default (fan-out at the writer).** Simpler than an aggregation hub, but doubles every notice in Default's buffer — re-creating buffer-bleed in Default. Rejected: it defeats the buffer isolation that kills the flake. The aggregation hub re-raises the event without touching Default's buffer, which is the distinction that matters.
- **Per-test fixtures that swap HeraldHost.Default per class.** Trades one corral for another (every class must remember to swap-and-restore). Doesn't delete scaffolding; adds it. Rejected per Steve's delete-scaffolding bar.
- **Make the static caches host-aware (give 4a/4b a host).** The notice routing would isolate, but the throttle latch is still process-global, so the tests still need serialisation. Net: large surface change for zero corral removal. Rejected as not worth it; Tier 2 keeps them honest and simple.
- **Synchronous announcement (drop the thread-pool defer).** Removes the timing half of the race but not the shared-buffer half. The defer exists to keep the announcement cost off the first emitter's hot path; removing it is a perf regression for a partial fix. Rejected.

## Test-scaffolding payoff (honest accounting)

The root fix DELETES scaffolding for the logger-scoped writers and KEEPS it for the genuinely host-less ones. Precise tally:

**Becomes removable (4 of 6 corralled classes + 3 in-test filters):**

- NamingPolicyAnnouncementTests — drop the [Collection(DefaultHostCollection.Name)] attribute; each test constructs its own host, asserts on host.RuntimeMessages.RecentNotices. The scoped-id filters at lines ~104 and ~299/301 (the .Where on Value == "pascal" / "pascal" or "snake") are deleted — a per-host buffer can't carry a sibling's residue, so the filter has nothing to defend against.
- StructuredLoggerNamingPolicyTests — drop DefaultHostCollection; per-host buffer.
- ExternalEventInjectionTests — drop DefaultHostCollection; assert on the owning host's buffer; the filter-the-shared-buffer-for-its-own-notices comment + filter come out.
- PipelineBridgeConsentForwardingTests — same removal.
- AnnouncementSpinHelpers — keeps the spin (the publish is still deferred), but the helpers stop reading the shared buffer; they take the HeraldRuntimeMessagesInstance to poll as a parameter. The filter-for-our-own-notice rationale is gone.

**Stays (2 of 6 corralled classes), with a one-line reason in each:**

- NameResolverCacheCapHitNoticeTests — writer is a static cache (no host) + a process-global throttle latch. Genuinely host-less; corral stays.
- SerilogTemplateHoleIndexCapHitNoticeTests — same.

**Unrelated to this ADR (do NOT touch):**

- EditionStateCollection and its members (HeraldCapabilityGateTests, HeraldVersionEditionTests, DetourCSurfaceFillInTests). This corral guards the edition/capability static slot on HeraldVersion, a different shared-static problem. The runtime-notice change does not address it; leave it. (Steve's brief listed it as a corral to consider — the honest answer is it is out of scope: the new channel mechanism does nothing for the edition slot.)
- DefaultHostCollection itself does NOT get deleted — the cap-hit classes and the tenant-registry classes (TenantCoexistenceGuardTests, HeraldTenantScopeRegistryContractTests, etc.) still need it for other HeraldHost.Default mutations. The notice-buffer reason for joining it is removed for the 4 logger-scoped classes; the collection survives for its other members.

Net: 4 classes leave the collection, 3 in-test residue-filters die, 1 helper stops touching shared state. 2 classes correctly stay. The fix is scaffolding-negative, as required — but it does not claim to delete what it cannot.

## net8 / net9 / net10 parity

Every API the design touches is BCL-stable across all three TFMs:

- HeraldRuntimeMessagesInstance, HeraldHost.RuntimeMessages — already multi-TFM (they compile today on net8 in the shipped 0.12.0 assembly).
- Interlocked.CompareExchange / Interlocked.Exchange, Volatile.Read, ThreadPool.UnsafeQueueUserWorkItem (T, bool) — all present since netcoreapp; no net9-only surface.
- The aggregation hub is plain events + a list under a lock (or a ConcurrentDictionary of weak refs); no net9/net10-only API.

No net9-only API in the design. The two cap-hit writers that stay on Default are identical across TFMs (the Serilog one self-excludes only its TextFormatter files on net8, per the 0.12.0 release note — the cap-hit notice path is not in that exclusion).

## Migration & risk

This touches a just-shipped feature's notice path (injection switch sec 7.1/7.6). Land it carefully:

1. **Additive constructor parameter, defaulted.** The new HeraldRuntimeMessagesInstance parameter defaults to HeraldHost.Default.RuntimeMessages. Every existing caller compiles unchanged and behaves identically until the build site opts in. Reversible.
2. **Build site opt-in second.** PipelineAssemblyBuilder.Build / QuickLogBuilder start passing the host channel. This is the behaviour flip; it is one wiring change, isolated.
3. **ForContext propagation third**, with its own test (a child logger's refusal lands on the parent's host buffer, never Default's).
4. **Aggregation hub last**, behind the operator-observability test.

**What proves the flake class is gone** — a real concurrent-pipeline stress test, not the corralled ones:

- New test RuntimeNoticePerHostIsolationStressTests: spin up N hosts in parallel (no DisableParallelization), each builds a logger, fires its announcement + an injection refusal + (if Tier-1) a gate rejection, then asserts its own host's buffer contains exactly its own notices and nothing from a sibling. Run it with xUnit parallelism ON. If per-host isolation holds, this is green every run with zero collection attribute. This is the test that would have caught the original bleed.
- A second test asserts the aggregation contract: a subscriber on HeraldRuntimeMessages.OnNotice (Default facade) receives notices published to a non-default host's channel — proving operator observability survived.

**What could regress:**

- **Missed ForContext propagation** -> scoped child loggers publish to Default -> silent re-bleed. Mitigated by the dedicated propagation test.
- **Aggregation double-counting** if the hub writes into Default's buffer instead of only re-raising OnNotice. Mitigated by asserting Default's RecentNotices count is unaffected by per-host publishes.
- **Gate-notice reachability** — if the gated sink genuinely cannot receive the channel without a back-reference, it stays Tier 2. Decide this at implementation time by reading the sink construction path; do not force it.

## Implementation plan (sized)

Effort: ~1.0–1.5 focused days including the stress test. Sequenced AFTER Glenn's net8 un-gating lands (no concurrent writes to Herald.OSS).

| Step | Change | Files | Size |
|------|--------|-------|------|
| 1 | Add _runtimeMessages field + constructor param (defaulted to HeraldHost.Default.RuntimeMessages) | StructuredLogger.cs (both ctors) | S |
| 2 | Propagate _runtimeMessages in ForContext | StructuredLogger.cs (~line 1508) | XS |
| 3 | Repoint writers 1 & 2 to _runtimeMessages.Publish | StructuredLogger.Naming.cs (FireAnnouncement), StructuredLogger.Injection.cs (RefuseExternalInjection) | S |
| 4 | Wire build site to pass the host's channel | PipelineAssemblyBuilder.cs (Build), QuickLogBuilder.Pipeline.cs | S |
| 5 | Writer 3 (gate): pass channel at sink construction IF reachable without a back-ref; else leave on Default | GenSourceGatedSink.cs, pipeline assembler | M (decision point) |
| 6 | Aggregation hub: non-default channels re-raise OnNotice onto Default (event only, not buffer) | new src/Diagnostics/RuntimeNoticeAggregator.cs + HeraldRuntimeMessagesInstance registration hook | M |
| 7 | Delete corrals: 4 classes drop DefaultHostCollection; delete in-test filters at NamingPolicyAnnouncementTests ~104/~299/301; parameterise AnnouncementSpinHelpers on the channel | 4 test files + 1 helper | S |
| 8 | Add RuntimeNoticePerHostIsolationStressTests (parallelism ON) + aggregation-observability test | new test files | M |
| 9 | Leave Tier-2 writers (4a/4b/5) + their 2 corrals + EditionStateCollection untouched | — | — |

Steps 1–4 are the mechanical core (Glenn-shaped). Step 5 is the one judgement call (gate-sink channel reachability). Step 6 is the contract-preserving move. Steps 7–8 are the payoff + proof.

## Consequences

- **Positive:** the flake class dies for logger-scoped notices; test buffers are isolated by construction; 4 corrals + 3 residue-filters deleted; operator observability and the injection-switch contract preserved verbatim; a real parallel stress test guards the property going forward.
- **Negative:** one new constructor parameter to thread; an aggregation hub to maintain; 2 cap-hit corrals legitimately remain (host-less writers). The fix is honest about its boundary rather than over-claiming a total kill.

## CUPID notes

- **Composable** — the channel is an injected HeraldRuntimeMessagesInstance, not a static reach-out; the caller (build site) chooses the channel. Small surface: one field, one param.
- **Unix** — each host's channel does one thing (hold that host's notices); the aggregator does one thing (re-raise to Default's event).
- **Predictable** — buffers isolated, events aggregated; no surprise cross-talk; side effects (publish) visible at the boundary.
- **Idiomatic** — mirrors the existing _kernelHolder propagation discipline and the host-instance pattern already used by HeraldRegistry / SinkRunState.
- **Domain-based** — the host owns its runtime-message channel reads straight from the deployment model (one host per tenant / per test).

## Implementation outcome (2026-06-02, Richard)

Status: **Implemented** on branch `feat/external-event-injection-switch`. All nine steps landed; build + full suite green on net8/net9/net10; the previously-flaky suites ran clean across repeated parallel runs.

### The gate-notice decision (step 5): STAYS ON DEFAULT — zero contract change

Writer 3 (`GenSourceGatedSink.EmitRejectionNotice`) was **not** rerouted. It keeps publishing through the static `HeraldRuntimeMessages.Publish` facade (Tier 2). The reason is decisive and reading-driven, not a fallback:

- **There is no OSS construction site to carry a channel into.** `GenSourceGatedSink.Wrap(...)` and the `GenSourceGatedSink` constructor are **never called anywhere in OSS source** (confirmed; the gate is the dormant Shape-A seam — see `docs/design/external-event-injection-switch.md` line 56 and the `feedback_oss_strip_shape_a` note). The "pipeline assembler" the ADR imagined passing the channel does not wire a gate in this assembly. Adding a `HeraldRuntimeMessagesInstance?` constructor parameter would add a seam that **no OSS caller ever feeds** — speculative generality (YAGNI), and it would not even help the only real constructor: a downstream commercial wrapper composing the gate would pass its own host's channel from its own assembly regardless.
- **The gate notice was never a driver of the flake class.** The four flaky writers were logger-scoped (naming + injection). The gate's tests construct the sink directly and were not in the corral-removal set. Leaving the gate on Default removes nothing and isolates nothing — there is no per-host buffer in play for it.
- **Operator observability is preserved either way.** If a commercial wrapper later routes the gate to a non-default host, the Tier-2 aggregation hub re-raises it on the Default facade — operators look in the same place. So staying on Default costs nothing on the observability axis.

Net: `GenSourceGatedSink.cs` is untouched. The "sinks stay dumb" invariant is honoured by *not* adding the dumb-but-unused channel parameter at all.

### A second shared-static surfaced (NameResolverCache) — corralled separately, in scope-honest fashion

Dropping `DefaultHostCollection` from the two naming-test classes exposed a **different** shared-static race: `StructuredLoggerNamingPolicyTests` asserts exact `NameResolverCache` miss/hit counts, and `NamingPolicyAnnouncementTests` (and other classes) call `NameResolverCache.Reset()`, which clears the whole process cache. A sibling reset between a count-asserting test's first-call-miss and second-call-hit turns the second into a miss (CacheMisses 1 -> 2). Channel routing cannot isolate this — it is the same class of genuinely-host-less shared static as the cap-hit corrals.

Fix: a new, **narrower** `NameResolverCacheCollection` (DisableParallelization) serialises the two naming classes against each other for the cache reason — exactly the protection `DefaultHostCollection` used to give them, minus the now-retired buffer reason. This is net-neutral on the cache axis while delivering the buffer de-corral. The other pre-existing `NameResolverCache.Reset()` classes carry their own private single-class collections and were already running parallel before this change; whether to consolidate them onto `NameResolverCacheCollection` is a pre-existing-flake question flagged for Echo, deliberately not folded into this change's scope.

### Scaffolding actually torn out

- `NamingPolicyAnnouncementTests` — dropped `DefaultHostCollection`; deleted the two scoped-id residue filters (`Value is "pascal"` and `Value is "pascal" or "snake"`); reads its own per-test channel buffer.
- `StructuredLoggerNamingPolicyTests` — dropped `DefaultHostCollection`; routes every build to a fresh per-test channel via a local `Create()` helper.
- `ExternalEventInjectionTests` — dropped `DefaultHostCollection`; per-test `_channel`; `InjectionNotices()` reads `_channel`.
- `PipelineBridgeConsentForwardingTests` — dropped `DefaultHostCollection`; removed the `IDisposable` settle-and-clear of the shared buffer; per-test `_channel`.
- `AnnouncementSpinHelpers` — now takes the channel to poll as a parameter; no longer reads the shared static buffer.
- Both naming classes joined the new `NameResolverCacheCollection` for the cache reason (above).

### Kept (honest exceptions)

- `NameResolverCacheCapHitNoticeTests`, `SerilogTemplateHoleIndexCapHitNoticeTests` — host-less static caches + process-global throttle latch; untouched.
- `EditionStateCollection` and members — different shared-static slot; untouched.
- `DefaultHostCollection` itself — retained for its other (tenant-registry, cap-hit) members.

### Aggregation hub hardening (the fool's three findings, applied)

- Chicken-and-egg: `HeraldHost` uses an intrinsic `private HeraldHost(bool isDefault)` discriminator, never `ReferenceEquals(this, Default)` (which is null during Default's own static init).
- No leak: the aggregation is exactly `channel.OnNotice += forwardToDefault` with **no central registry** — the subscription is the registration, so a short-lived host GCs with its channel and forwarder.
- Loud-non-throwing: `RaiseOnNoticeWithoutBuffering` snapshots the invocation list and wraps each subscriber in try/catch, mirroring `Publish`.

The old G1.1 facade-isolation test was deliberately updated to the new contract (facade OnNotice aggregates non-default notices; facade buffer stays isolated) and pinned by `RuntimeNoticeAggregationObservabilityTests`.

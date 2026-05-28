# Changelog

All notable changes to Herald.OSS are documented here. The format is
based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and
this project adheres to [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Documentation

- **Compact-path default-axes-only contract — documented and enforced.**
  `MMP.Herald.Pipeline.Kernel.LogPropertyCompact` carries name and value
  only. Non-default `LogProperty` axes — `CaptureMode`, `Format`,
  `Visibility` — are not representable on the compact slot. The
  canonical-equivalence statement now lives in the XML doc on the type
  and on `ToLogProperty()`: the inflated record is canonically
  equivalent to a direct `LogProperty(name, value)` because no
  non-default-axis information is present to lose. A caller that needs
  a non-default axis routes through the full `LogProperty` path.

  Compile-time enforcement: HERALD014 in
  `MMP.Herald.OSS.Generators` flags `LogProperty.Silent(...)`,
  `LogProperty.Lazy(...)`, and the named-axis `LogProperty` constructor
  when they flow into a compact-path API. Severity is Warning by
  default; `<HeraldStrictMode>true</HeraldStrictMode>` (HRLD0002)
  escalates the warning to an error.

  Full design-decision posture and the dual-register prose:
  [Compact-path default-axes-only — design decision](https://github.com/mmpworks/Herald.Documentation/blob/main/prose/herald-oss/explanation/design-decisions/compact-path-default-axes-only.md).
  Structured record: [`data/herald-oss/design-decisions/compact-path-default-axes-only.json`](https://github.com/mmpworks/Herald.Documentation/blob/main/data/herald-oss/design-decisions/compact-path-default-axes-only.json).

### Security (staged — fix queued, posture published)

- **Async-sink cross-tenant PII — five-layer fix queued for 0.10.2.**
  `FastPathAsyncSink` defers a log event from the producer thread to a
  background consumer; a `LogProperty.Lazy(...)` closure resolves on
  the consumer thread, where the producer's tenant scope is no longer
  in effect. Latent in shipped code from 0.4.0 onward and byte-identical
  in the Modules/Core mirror. No evidence of exploitation; the fix
  prevents rather than detects.

  The defense lands additively across five layers — default-eager
  capture (L1), factory finalization scan in `LogEventFactory.Create`
  and `DeferredLogEventFactory.Create` (L2), drain-entry assertion as
  defense-in-depth backstop (L3), `PiiSensitive` force-eager-to-string
  on the producer thread (L4), and a fail-loud diagnostic path
  replacing the silent exception swallow in `ConsumeAsync` (L5).

  Compile-time enforcement ships in the existing
  `MMP.Herald.OSS.Generators` assembly: HERALD008–HERALD013 extend
  the `HERALD0xx` analyzer family to flag the unsafe `LogProperty.Lazy(...)`
  shapes. A `[HeraldDrainSafe(Reason = "...")]` attribute provides the
  auditable suppression — `Reason` is required, and the build emits a
  count of reviewed suppressions. Existing `<HeraldStrictMode>true</>`
  (HRLD0002) escalates the new warnings to errors for regulated builds.

  Full posture, threat model, trust boundary, and threat-coverage matrix:
  [Async-sink cross-tenant PII — security posture](https://github.com/mmpworks/Herald.Documentation/blob/main/prose/herald-oss/explanation/security/async-sink-cross-tenant-pii-posture.md).
  Structured record: [`data/herald-oss/security-postures/async-sink-cross-tenant-pii.json`](https://github.com/mmpworks/Herald.Documentation/blob/main/data/herald-oss/security-postures/async-sink-cross-tenant-pii.json).

## [0.10.1-rc.1] — 2026-05-27

Performance release candidate. Collapses the high-arity typed-args
cliff in the interceptor generator and tightens the runtime naming-cache
path. No public API change — the surface is identical to 0.10.0; only
emit shape and an internal cache key change.

### Changed

- **Typed-args high-arity interceptor perf fix.** A lane-inlining gate
  in the interceptor generator collapses the arity-12/16 cliff. A
  16-property typed-args call site that previously measured ~82 ns now
  measures ~46 ns and stays zero-allocation. The arity curve is smooth
  across the swept range; lower-arity call sites and the reject sweep
  are unaffected.
- **Runtime naming-cache path improvement.** The name-resolver cache now
  keys on reference identity and guards inserts to interned strings only.
  This is a runtime-path change behind the existing public surface.

## [0.10.0] — 2026-05-26

Public-API release. Adds a generic network-sink builder seam, a
multi-filter compose seam on the pipeline, and humanized component
display names for the pipeline config surface. The 0.9.0 package id
on nuget.org predates these additions and is superseded by 0.10.0.

### Added

- **`WithNetworkSink(kind, endpoint)` on `QuickLogBuilder`.** A generic
  network-sink builder seam: declare a network sink by kind and endpoint
  without a kind-specific fluent method. New public surface — the reason
  this is a minor bump.
- **Multi-filter compose seam on the pipeline.** Compose more than one
  level/category filter on a single pipeline; filters apply in declared
  order.
- **Humanized component display names.** Pipeline components surface a
  readable display name for config UIs in place of raw type names.

## [0.4.0] — 2026-05-18

V1.1 perf-tightening for the multi-policy interceptor. Consumers who
commit at build time to the default Pascal naming policy
(`<HeraldNamingPolicyAssertion>Default</>`) now get a leaner emit:
per-call-site single-lane interceptor, no switch dispatch, no
`CurrentPolicyKind` read, no kind cache. Closes the residual ~4 ns
gap between V1 (~31 ns) and the pre-regression baseline
(26.64 ns) for asserting consumers — measured 27.32 ns
`Herald_FourProps_NullSink` under assertion vs 31.20 ns unasserted.

Multi-policy emit is unchanged for non-asserting consumers. Both
paths preserve the V1 schema-contract win: template tokens drive
property names regardless of caller variable names.

The real-sink bench (`RealSinkBenchmarks`) settles the
"does the dispatch gap survive a real sink?" question: file sink
and counter sink land within 0.7 ns of null sink (all async-buffered;
per-emit cost is dispatch + buffer-fill regardless of sink shape).
The assertion's 4 ns win is consumer-observable across every
realistic sink configuration.

Two commits: `1f07a99` (forward-compat seams — R1 per-arity
`LogCompactN`, R3 `HeraldPipelineComposition` MSBuild surface, R4
partial interceptor class) and `cde7e7e` (V1.1 implementation —
`HeraldNamingPolicyAssertion` MSBuild property + single-lane emit +
HRLD0051 analyzer + HRLD0011 validation + `NamingPolicyAssertion`
field on `[assembly: HeraldBuildAssertion]`).

### Added

- **`<HeraldNamingPolicyAssertion>` MSBuild property.** Consumer-side
  assertion that the build commits to a single naming policy. V0.4.0
  recognises `Default` (asserts default Pascal). Unset preserves the
  V1 multi-policy emit. When asserted, the interceptor generator
  emits a per-call-site single-lane interceptor; the dispatcher's
  switch + kind-cache + `CurrentPolicyKind` read are all elided.
- **HRLD0051 analyzer.** Warns when `WithNamingPolicy(...)` or
  `InstallNamingPolicy(...)` is called in an assembly that has
  asserted `<HeraldNamingPolicyAssertion>Default</>`. Local-assembly
  only; cross-assembly transitivity is V2 territory. Strict-mode
  (`<HeraldStrictMode>true</>`) escalates to error.
- **HRLD0011 diagnostic.** Validates the
  `<HeraldNamingPolicyAssertion>` value at build time. Only
  `Default` is recognised in V0.4.0; unknown values fail the build
  with a "valid values" message.
- **`<HeraldPipelineComposition>` MSBuild property** + matching
  `HeraldPipelineCompositionAttribute` auto-emitted via
  `build/Herald.OSS.targets`. V0.4.0 recognises `Dynamic` (the only
  current valid value); HRLD0010 validates. V2 will add
  `SingleKernelSink` as a new valid value without breaking V0.4.0
  consumers.
- **`HeraldBuildAssertionAttribute.NamingPolicyAssertion`** — new
  init-only string property carrying the asserted policy name
  (empty when unset). Binary-additive change; existing readers
  ignore it.
- **Internal per-arity `LogCompactN` methods** on `StructuredLogger`
  (arity 1..8). Forward-compat seam; not yet wired into the V1.1
  emit. Reserves the door for future generator-emit shapes that
  bypass caller-side buffer construction.

### Changed

- **Interceptor class emission**: `file static class
  HeraldInterceptors_<hash>` → `internal static partial class
  HeraldInterceptors_<hash>`. Unblocks V2's per-call-site
  specialization layered as sibling `.g.cs` files. No consumer-side
  behavior change.
- **`BakedPolicies` field on `[assembly: HeraldBuildAssertion]`**
  reflects the asserted state: `"Pascal"` when asserted, the V1
  `"Pascal,Snake,Camel"` when unasserted.

### Documentation

- `docs/diagnostics/HRLD-codes.md` — new HRLD0010, HRLD0011, HRLD0051
  entries; property reference table extended; runtime-read examples
  for the build-assertion attribute updated.

## [0.3.0] — 2026-05-17

V1 naming-policy fix. Closes the perf regression introduced in 9cc8940
(bundled `PropertyNamingPolicy` generator change into a config commit)
while delivering the bigger architectural win: a stable template-driven
event schema baked at the consumer's compile time.

Property names now derive from the template tokens, normalized through
the active policy at compile time via the new multi-policy interceptor.
Same template + same policy = same downstream schema across every emit
site, every consumer assembly, every tenant. Downstream consumers
(OTLP exporters, dashboards, audit queries) get a stable schema
contract instead of variable-name accidents at the call site.

Five commits: `472862a` (extract CompileTimeNameResolver + Camel
token-first + public BuiltinPolicy), `692d287` (runtime-floor
cleanup), `af9a8d7` (multi-policy interceptor), `4630edb` (lane-split
+ AggressiveInlining for JIT inlining recovery), `be86249` (honest MEL
allocation framing + matched-TFM Herald numbers).

Bench: `Herald_FourProps` lands at 31 ns (down from the regressed
~56 ns), within ~4 ns of the pre-regression baseline. Allocation-free
across all arities. Three built-in policies (Pascal / Snake / Camel)
all dispatched via baked compile-time literals on every literal-
template call site; custom policies fall through to the runtime
resolver.

### Changed

- **CamelCasePolicy is now token-first**, consistent with PascalCasePolicy
  and SnakeCasePolicy. Property names derive from the template token when
  present; the `ToCamelCase` transform lowercases the first letter on
  names that don't already start lowercase and don't contain underscores
  (mirror of Pascal's restraint, inverted case test). The previous
  CAE-first behavior in `CamelCasePolicy.ResolveAll` is replaced;
  `[HeraldLog]` Camel emission and the runtime typed-args path now
  produce identical property names for any literal-template call site.
  Pre-1.0 alpha; no installed-base migration concern.

- **`BuiltinPolicy` enum is now public.** Lives in
  `MMP.Herald.Templating.BuiltinPolicy` with four values:
  `Pascal` (default, also returned when no policy is installed),
  `Snake`, `Camel`, and `Custom`. The four-value shape is the V1
  contract — future built-in policies extend additively, the V1 four
  stay stable.

- **`StructuredLogger.CurrentPolicyKind` is now public.** Returns the
  `BuiltinPolicy` kind the currently-installed naming policy maps to
  (or `Pascal` when no policy is installed; `Custom` for any
  consumer-supplied non-built-in policy). Used by interceptor-emitted
  dispatch code to pick the right baked-name lane without policy-type
  reflection.

- **Runtime-floor cleanup on the naming-policy dispatch path.** The
  per-logger naming policy is now stored as a nullable
  `IPropertyNamingPolicy?` (null == "use the Pascal default") so the
  multi-policy interceptor can short-circuit through the Pascal lane
  with no extra resolve work. Hit / miss / resolve counters dropped
  from `Interlocked.Increment` to plain `int++` with documented
  drift-tolerant semantics — aligned 32-bit reads/writes are atomic at
  the hardware level on every platform Herald supports, so the
  counters are approximate but never torn. The fallback counter stays
  on `Interlocked.Add` and the compile-time counter stays on
  `Interlocked.Increment` because both are low-frequency. The first-
  dispatch announcement publish is queued to the thread pool via
  `ThreadPool.UnsafeQueueUserWorkItem<T>` so the emitting dispatch
  pays only the `Interlocked.CompareExchange` flip on the hot path.

### Added

- **`CompileTimeNameResolver` (generator-internal).** Shared build-time
  resolver used by `HeraldLogGenerator` and the multi-policy interceptor
  generator. Source selection (token first, then CAE, then `argN`) and
  casing transform are byte-identical to the runtime policies'
  `ResolveAll` output for any (template, CAE, policy) tuple the
  build-time path can reach.

- **Multi-policy interceptor generator.** Bakes the active naming
  policy's resolved property names into every literal-template
  `StructuredLogger.Info` / `.Warn` / `.Error` / `.Debug` / `.Trace`
  call site in the consumer's compilation. Each emitted interceptor
  carries Pascal / Snake / Camel baked lanes selected at dispatch time
  by `StructuredLogger.CurrentPolicyKind`; a custom
  `IPropertyNamingPolicy` falls through the interceptor's `default`
  lane to the runtime resolver (no recursion — interceptor matching is
  keyed by syntactic call-site location, the fall-through call lives
  in the generated file at a different position).
  Explicit `nameN:` override call sites skip the interceptor and stay
  on the runtime path so the documented override contract still holds.

- **`HeraldBuildAssertionAttribute` (`MMP.Herald.Build`).** Assembly-
  level marker emitted by the interceptor generator. Surfaces
  `InterceptorsEnabled`, `StrictMode`, `BakedPolicies`, and
  `InterceptedCallSites` so a host process can confirm at runtime that
  a referenced assembly was built with the expected Herald surface.
  Lookup via `Assembly.GetCustomAttribute<HeraldBuildAssertionAttribute>()`
  is trim-safe and AOT-safe.

- **`buildTransitive/Herald.OSS.props` (NuGet payload).** Auto-applies
  `MMP.Herald.Generated` to consumer projects'
  `InterceptorsNamespaces` and exposes `HeraldInterceptorsEnabled` and
  `HeraldStrictMode` as `CompilerVisibleProperty` items, so a consumer
  who takes a NuGet `PackageReference` on Herald.OSS gets the
  interceptor surface wired up without a manual csproj edit.

- **`HRLD0001..HRLD0099` diagnostic family.** Reserves the range for
  MSBuild-property validation. V1 ships `HRLD0001` (invalid
  `HeraldInterceptorsEnabled`), `HRLD0002` (invalid `HeraldStrictMode`),
  and `HRLD0050` (interceptor surface exceeded the 5,000-site soft
  threshold — operator hint, warning only). Documented in
  `docs/diagnostics/HRLD-codes.md`.

- **`tests/Interceptor.SmokeTests`.** Console smoke that verifies the
  build-assertion attribute lands, the three baked lanes select the
  expected property names per active policy, custom policies fall
  through to the runtime resolver lane, and the diagnostics counters
  reflect interceptor dispatch. Run with
  `dotnet run --framework net10.0 --project tests/Interceptor.SmokeTests`.

- **Cross-path drift coverage.** `CompileTimeNameResolverFixtures` is
  the shared row-based fixture set; `CompileTimeNameResolverTests` and
  `PolicyResolveAllFixtureTests` drive every row through the build-time
  and runtime paths respectively, so any divergence between the two
  resolvers fails the build.

## [0.2.3] — 2026-05-16

Bundles three rounds of review findings on the 0.2.2 surface:
restored hooks on the new diagnostics channel (Rosanne), the
sync-disposable disposal-chain fix (Richard's Option C), and the
test-coverage gaps Echo identified. All additive; no observable
behavior change for current consumers.

### Fixed

- **`QuickLogResult.DisposeAsync` now reaches sync `IDisposable`
  sinks.** Pre-0.2.3, the disposal chain only awaited
  `_bootstrapResult.AsyncResource.DisposeAsync()` — which is null
  for sync pipelines that don't call `WithAsync()`. Sync sinks
  (file sinks, stream-backed writers, anything with a handle that
  needs release without an async drain) were never disposed
  through the registration's disposal chain. File handles stayed
  open; buffered writes never flushed. The testbench's
  `FINDINGS.md` documented this as Finding 1; it's now closed via
  a parallel `SyncResources` field on `LoggingBootstrapResult`
  that the disposal walker traverses after `AsyncResource`.
  Disposal failures route through `ILogFailureSink` so a single
  throwing disposable doesn't block the rest of the chain.

### Added

- **`NoticeSeverity` (rank-based record).** Three canonical
  instances (`Info` = 0, `Warning` = 1, `Error` = 2) with an
  `Includes(required)` comparison. Same shape as `HeraldEdition`.
  Added as a positional parameter on `RuntimeNotice` so
  subscribers can route on tier (pager on Error, dashboard on
  Warning, debug console on Info).

- **`HeraldRuntimeMessagesInstance.OnNoticeDropped` event.** Fires
  when a notice is evicted from the recent-notices buffer because
  the buffer was full. A subscriber watching for chatty publishers
  uses this to see which notices were lost — `DroppedNoticeCount`
  gives the total; this event gives the identities.

- **`HeraldRuntimeMessagesInstance.FallbackSubscriber`.** Optional
  `Action<RuntimeNotice>?` invoked when `Publish` finds no
  subscribers on `OnNotice`. Gives a host that hasn't wired live
  observation a place for unwatched notices to land (typically
  `Trace.WriteLine` or stderr). Mirrors the kernel-failure-sink
  fallback pattern. Default `null` = current silent behavior.

- **`BoundedNoticeBuffer<T>.OnEvicted` event.** Fires when an
  entry is evicted at enqueue time. Backs the `OnNoticeDropped`
  forwarding on the runtime-message channel and is exposed
  directly so any other consumer composing the buffer can observe
  evictions. Handler exceptions are swallowed — buffer integrity
  must not depend on subscriber discipline.

- **`PipelineAssemblyBuilder.TrackSyncResource(IDisposable?)`.**
  Mirrors `TrackAsyncResource` for sync-only disposables. Used
  internally by the pipeline factory's auto-tracking; available
  to downstream consumers that build custom assembly paths.

- **`LoggerComposition.SyncResources`** and
  **`LoggingBootstrapResult.SyncResources`** — new optional
  `IReadOnlyList<IDisposable>?` fields carrying sync sinks
  tracked during pipeline assembly.

- **`DiagnosticLogFailureSink.DroppedEntryCount`.** Property
  surfacing the bounded buffer's dropped-entry count — useful for
  diagnostic dashboards showing "N of M total failures."

### Changed

- **`HeraldRuntimeMessagesInstance.Publish`** gained a
  severity-explicit overload. The original `Publish(source,
  message, properties)` overload still exists and forwards with
  `NoticeSeverity.Info`. Source-compatible for pre-0.2.3 callers.

- **`DefaultLogPipelineFactory` auto-registers IDisposable /
  IAsyncDisposable sinks** with the pipeline assembly builder so
  the disposal chain can reach them. `SafeCompositeLogger.Children`
  flatten on registration; single-sink pipelines register the
  sink directly. Async-disposable sinks land on
  `TrackAsyncResource` (drain semantics); sync-only sinks land on
  `TrackSyncResource`.

### Tests added

30 new tests across:

- `tests/Diagnostics/NoticeSeverityTests.cs` — rank ordering,
  Includes, ToString, value equality.
- `tests/Diagnostics/HeraldGenSourceTests.cs` — pins
  `RuntimeNotice` token value (`@herald.runtime.notice`).
- `tests/Diagnostics/HeraldRuntimeMessagesTests.cs` — severity
  default + override, null-properties normalization,
  `OnNoticeDropped` firing + suppression + throw-isolation,
  `FallbackSubscriber` firing only when no live subscribers
  exist, static-facade isolation against non-default hosts (the
  G1.1 gap Echo identified).
- `tests/Diagnostics/BoundedNoticeBufferTests.cs` — `OnEvicted`
  event firing + throw-isolation, capacity-one and large-capacity
  boundaries.
- `tests/Failures/DiagnosticLogFailureSinkTests.cs` (NEW file) —
  eviction count, file-mirror auto-create-parent, concurrent
  ReportFailure file-line integrity, oldest-first ordering.
- `tests/Quick/SyncDisposableSinkLifecycleTests.cs` (NEW file) —
  Finding 1 regression coverage at the
  `PipelineAssemblyBuilder` surface: SyncResources populated on
  tracked disposables, in registration order, with null entries
  skipped.

Full OSS suite: 313/313 on net10. Multi-TFM clean on net8/9/10.

## [0.2.2] — 2026-05-16

Closes the runtime-notice leak the multi-tenant testbench surfaced.
The naming-policy announcement event was routing through the user's
logging pipeline — `StructuredLogger.FireAnnouncement()` called
`this.Log(...)` so the announcement landed in whatever sinks the
consumer wired. In multi-tenant deployments that meant per-tenant
sinks received framework messages the tenant's application never
logged. The wall the consumer built was breached by the framework
itself.

This release splits runtime signals onto a separate process-wide
channel. User pipelines never see them. Consumers who want
diagnostic visibility subscribe to the channel.

### Fixed

- **Naming-policy announcement no longer leaks into user sinks.**
  `StructuredLogger.FireAnnouncement()` now publishes to
  `HeraldRuntimeMessages` instead of calling `Log()` on itself.
  Per-tenant bridges, file sinks, and other user-wired channels see
  only application events. Consumers who relied on the announcement
  appearing in their user sinks should subscribe to
  `HeraldRuntimeMessages.OnNotice` instead.

- **Throwing subscribers to `HeraldRuntimeMessages.OnNotice` no
  longer propagate into framework code.** Each subscriber runs
  inside its own try/catch. A buggy debug-console handler can no
  longer take down the user's `logger.Info(...)` call. Every other
  subscriber on the invocation list still receives the notice.

### Added

- **`MMP.Herald.Diagnostics.HeraldRuntimeMessages`** — process-wide
  static channel for framework-emitted notices. Forwards to
  `HeraldHost.Default.RuntimeMessages`. Exposes `OnNotice` event,
  `RecentNotices` snapshot (oldest-first, bounded), `ClearRecent`,
  and `Publish`.

- **`MMP.Herald.Diagnostics.HeraldRuntimeMessagesInstance`** —
  per-host instance form. Tests and multi-tenant scenarios that
  need channel isolation construct their own `HeraldHost` and use
  `host.RuntimeMessages` directly. Mirrors the `HeraldRegistry` /
  `HeraldRegistryInstance` pattern established in 0.2.0.

- **`MMP.Herald.Diagnostics.BoundedNoticeBuffer<T>`** — thread-safe
  bounded FIFO with eviction and dropped-count tracking. Composed
  by both `HeraldRuntimeMessagesInstance` and
  `DiagnosticLogFailureSink` so the buffer mechanics live in one
  place. Capacity defaults to 64 for runtime notices and 200 for
  failure records (matching the previous `DiagnosticLogFailureSink`
  default).

- **`MMP.Herald.Diagnostics.HeraldGenSource.RuntimeNotice`** —
  reserved `GenSource` token (`@herald.runtime.notice`) stamped on
  every `RuntimeNotice`. A downstream consumer who later bridges
  runtime notices back into their pipeline can preserve the
  provenance marker and let any downstream gate filter on it.

- **`HeraldHost.RuntimeMessages`** — new instance property
  exposing this host's runtime-notice channel. Mirrors
  `host.Pipelines`.

- **`DiagnosticLogFailureSink.DroppedEntryCount`** — new property
  surfacing the bounded buffer's dropped-entry count, useful for
  diagnostic dashboards that want to indicate "you're seeing N of
  M total failures."

### Changed

- **`DiagnosticLogFailureSink` now composes `BoundedNoticeBuffer<T>`.**
  The buffer mechanics (queue, eviction, snapshot, thread-safe
  write) moved into the shared `BoundedNoticeBuffer<T>` type. The
  failure sink keeps its file-mirroring concern locally. No
  observable behaviour change for existing consumers; the public
  surface gains `DroppedEntryCount`.

- **The pipeline's minimum-level setting no longer silences the
  naming-policy announcement.** Pipeline minimum-level filters user
  events; framework notices live on a separate channel and are not
  subject to user-level rules. An operator who sets
  `MinimumLevel=Warn` still receives the announcement via
  `HeraldRuntimeMessages`.

## [0.2.1] — 2026-05-16

Two-phase release. The first phase fixed two silent-drop paths on top
of 0.2.0 (kernel failure-sink wiring, WithContext kernel orphan). The
second phase reconciled 0.2.0's residue strip with the broader Herald
architectural philosophy that consumer-facing hooks stay present in
OSS even when OSS itself enforces nothing against them. The
"Restored" and "Added" sections below capture the restored hooks;
"Deferred" lists what stays out pending a future release.

### Fixed

- **Kernel path now routes sink failures through the configured failure
  sink.** Previously, a throwing sink on the kernel fast path fell
  through to `System.Diagnostics.Trace.WriteLine` even when the pipeline
  was wired with an `ILogFailureSink` — the chain path (`SafeCompositeLogger`)
  reported failures through the sink, but the kernel did not. Now both
  paths share the same shape: when a failure sink is configured, the
  kernel synthesizes a `LogEvent` from the buffer's level, category,
  template, message, time, and event id, and hands it to
  `ILogFailureSink.ReportFailure(...)`. When no failure sink is wired the
  kernel still falls back to `Trace.WriteLine` with the
  `[Herald.OSS] kernel sink threw` prefix — the previous behaviour for
  vanilla pipelines is unchanged.
- **`WithContext` children now share a kernel holder with the parent.**
  Previously, a child logger built via `StructuredLogger.WithContext(...)`
  captured the parent's `LogKernel` delegate by value at construction.
  A subsequent `SwapKernel` on the parent (hot reload) updated the
  parent's view but left the child dispatching through the orphaned old
  kernel — long-running scope-bearing loggers (per-request ASP.NET
  loggers, typically) kept routing events to retired sinks. The kernel
  now lives behind an internal `KernelHolder` that the parent and every
  child reference together; a swap on the parent is observed by every
  child on the next dispatch.

### Restored

- `HeraldEdition` record + `ILogSinkProvider.MinimumEdition` (B-2).
  Originally stripped in 0.2.0; restored as an Enterprise-gotcha hook
  per the "hooks present even if not used" architectural philosophy.
  OSS does not enforce against the value; downstream commercial
  wrappers read the well-known property to surface tier intent
  through the same hook Dashboard already renders.
- `GenSource` field on `LogEvent` and `LogEventBuffer` + the
  `GenSourceGatedSink` decorator and its `IKernelSink`-aware
  `GenSourceGatedKernelSink` variant (B-3). The provenance-gate
  primitive is the multi-tenant routing hook upstream consumers
  depend on. OSS does not stamp `GenSource` by default and does not
  wrap any sink with the gate by default; out-of-the-box behavior is
  unchanged.

### Added

- `HeraldTenant.TenantAdmissionPolicy` delegate plus tenant-scope-aware
  single-arg registry lookup, `Register`, and `TryRegister` paths on
  `HeraldRegistryInstance` (B-1). Closes a refactor regression where
  `HeraldRegistry.Get(name)` hardcoded `HeraldTenant.Default` instead
  of consulting `HeraldTenantScope.Current`. Multi-tenant hosts that
  set a scope now see their tenant honoured on every single-arg
  lookup. Tenants can also reject inbound registrations via a
  delegate without the registry-instance having to know per-tenant
  policy.
- `HeraldRegistryInstance.OnTenantRegistration` event plus static
  forwarder on `HeraldRegistry` (B-4). Observation hook for
  `(tenantName, providerKey)` pairs at the moment of registration.
- `HeraldRegistryInstance.OnTenantLookupMissed` event plus static
  forwarder on `HeraldRegistry` (B-5). Observation hook for
  `(tenantName, providerKey)` pairs at the moment a tenant-scoped
  lookup falls back to default.
- `HeraldRegistryInstance.AllowDefaultAndScopedCoexistence` strict-mode
  bool guard (B-6). Default `true` preserves prior behavior; setting
  to `false` throws when both a default and a tenant-scoped
  registration would coexist for the same key, surfacing the
  ambiguity at composition time rather than first lookup.
- Unit tests `KernelFanOutFailureIsolationTests.Failure_sink_receives_synthesized_event_when_wired`
  and `...Trace_fallback_fires_when_no_failure_sink_is_wired` pinning
  the dual reporting paths.
- Unit test file `WithContextKernelOrphanTests` pinning parent ↔ child
  kernel sharing, swap propagation in both directions, swap-to-null,
  and grandchild holder sharing.
- Unit test files `HeraldEditionTests` (B-2 ranking + Includes
  semantics), `GenSourceGatedSinkTests` (B-3 gate accept/reject,
  fast path, callback shape, kernel-path overload),
  `HeraldTenantAdmissionPolicyTests` + `HeraldTenantScopeRegistryContractTests`
  (B-1), `OnTenantRegistrationEventTests` (B-4),
  `OnTenantLookupMissedEventTests` (B-5), `TenantCoexistenceGuardTests`
  (B-6).

### Deferred

- `ExternalSourceRegistrar` plus `IRegistrarStore`,
  `RegistrarSnapshot`, `FileRegistrarStore`, `NullRegistrarStore`,
  `RegistrarJsonContext` (B-7). The operational layer on top of the
  B-3 gate — HMAC-derived keys, anti-replay timestamp lock,
  pluggable persistence, hot-reload replay. The gate primitive in
  B-3 is independently usable; this is the multi-tenant
  registration surface that turns it into a working operational
  layer. Plan documented in
  `Herald/wiki/designs/b7-external-source-registrar.md`.

## [0.2.0] — 2026-05-15

Coordinated breaking-changes release. No external adopters yet — the
window for cheap breaking changes is now. The three changes below are
the kind of residue and bug fix that's expensive to land after 1.0.

### Removed (breaking)

- **`HeraldEdition` type and `MinimumEdition` property on
  `ILogSinkProvider`.** Herald.OSS is a single-edition distribution
  with no runtime gate; the type and the property surface were inert
  plumbing. Sink authors that previously declared
  `public HeraldEdition MinimumEdition => HeraldEdition.Community;`
  remove the line. Commercial wrappers that want to keep an edition
  badge can layer it back on as their own type.
- **`HeraldTenant.EnsureAllowedForCurrentEdition` method.** The OSS
  implementation was an empty body; gate enforcement is downstream-
  only. The two call sites in `HeraldRegistryInstance` are removed.
- **`GenSource` field on `LogEvent`, `LogEventBuffer`,
  `LogEventFactory`, `DeferredLogEventFactory`,
  `ILogEventFactory`, and the `_genSource` plumbing through
  `StructuredLogger`, `DefaultLogPipelineFactory`, `HotPathLogger`,
  and `WindowedMeanLogger`.** The provenance gate was already absent
  from the OSS distribution; the field was inert. Downstream
  commercial wrappers that need a provenance carrier can stamp the
  value into `Context["gen_source"]` instead.

### Changed (breaking)

- **`StructuredLogger.IsXxxAcceptable` and
  `HotPathLogger.IsXxxAcceptable` are now properties, not fields.**
  Source-compatible: `if (logger.IsDebugAcceptable) ...` keeps
  binding to the same member name. Binary-breaking for pre-compiled
  consumer assemblies that linked the field by `ldfld`; recompile
  against 0.2.0 to restore. The property getter is a single
  `Volatile.Read` so the emitted reject path stays one load plus
  branch.
- **Level-only hot reload now recomputes the per-known-level accept
  booleans.** A `RecomputeAcceptables` hook on the outer
  `StructuredLogger` is called from the level-only branch of
  `HotReloadableLoggingBootstrap.ExecuteReload`. Without this hook,
  a level-only reload that lowered the minimum left source-gen-
  emitted reject sites reading the stale field value — events at
  the newly-accepted levels were silently dropped.

### Added

- Unit test `IsXxxAcceptableHotReloadTests` pinning the
  IsXxxAcceptable property values at construction and after a
  RecomputeAcceptables call that lowers, raises, or clears the
  minimum.

## [Unreleased]

### Migration

**Default property naming flipped to PascalCase.** Herald 1.0 matches the
.NET ecosystem convention used by Serilog, Microsoft.Extensions.Logging,
and NLog. Calls of the form `logger.Info("user {UserId} signed in", userId)`
now emit the property as `UserId` instead of `userId`.

**This will break adapter-wrapped sinks that key on 0.x property names.**
If you wrap Serilog, Seq, Splunk, or any downstream system whose dashboards,
SIEM rules, or queries were built against pre-1.0 camelCase output, the
rename will silently change the wire format. The mitigation is one line:

```csharp
builder.WithNamingPolicy(PropertyNamingPolicy.Camel);
```

Pin to `Camel` to preserve 0.x behavior. Or run a coordinated cutover:
update the downstream schema first, then drop the override and adopt the
new default.

`Snake` is also available for OpenTelemetry-aligned consumers. Per-method
override via `[HeraldLog(NamingPolicy = "...")]` ships in 1.0.

### Added

- `KernelBufferAdapter.MaterializeAndRender(in LogEventBuffer)` —
  public helper for sinks implementing `IKernelSink` that need a
  fully-materialised heap `LogEvent` with rendered Message at the
  boundary. The four built-in addon sinks
  (`StreamingArchiveLogger`, `CrashSafeRingBuffer`, `LiveLogCapture`,
  `DirectTransformerLogger`) now implement `IKernelSink` via this
  helper. Third-party sink authors porting from `ILogger.Log(LogEvent)`
  can do the same with a one-line method body.
- `QuickLogResult.KernelDiagnostic` reports kernel eligibility at
  pipeline construction. The record carries `KernelEligible` and a
  human-readable `RejectionReason` from `KernelEligibility`.
- Public-release scaffolding: `CONTRIBUTING.md`, `CODE_OF_CONDUCT.md`,
  `SECURITY.md`, `CHANGELOG.md`, `.github/workflows/ci.yml` (build +
  test on net8/net9/net10 + NuGet pack smoke), PR and issue templates.

### Changed

- **Sink contract unified.** Every routed sink must now implement
  `IKernelSink` for the kernel fast path. Every built-in Herald.OSS
  sink — console, file, JSON, null, archive, ring-buffer, SSE
  capture, channel — implements both `ILogger` and `IKernelSink`, so
  default pipelines emit at kernel speed automatically. Custom sinks
  that skip `IKernelSink` fall back to the chain path; the
  disqualifying sink is named in `KernelDiagnostic.RejectionReason`.
- `KernelMixedSinkBenchmarks` reflects the strict eligibility check:
  a pipeline with a non-`IKernelSink` bridge runs the chain path at
  812.47 ns / 1,160 B per emit (vs 28.54 ns / 0 B for pure kernel).

### Removed

- `MaterializingKernelSink` and `IStructuredOnlySink` — the auto-wrap
  path introduced earlier in this development cycle is removed. With
  every built-in sink implementing `IKernelSink` directly, there is
  no legacy sink to wrap and no marker interface to opt into.
- `KernelDiagnostic.LegacySinks` and the `LegacySinkInfo` record —
  no longer meaningful when every sink is required to implement
  `IKernelSink`. The diagnostic now reports only `KernelEligible`
  and `RejectionReason`.

## [0.1.0] — 2026-05-14

Initial open-source bootstrap. Forked from Herald.Core at commit
`98d23fd` with edition-gating machinery, the provenance gate, and
distribution-hardening tooling removed. See `FORK_SCOPE.md` for the
authoritative list of what was stripped and why.

### Added

- Apache 2.0 licensed structured logging core for .NET 8 / 9 / 10.
- Kernel fast path: stack-allocated `LogEventBuffer` passed by `ref`
  to sinks that implement `IKernelSink`; zero-allocation emit on the
  common path.
- Four accept-path call shapes: typed-args, `params ReadOnlySpan<LogProperty>`,
  the interpolated string handler, and the level-bound interpolated
  variant.
- `LogPropertyCompact` typed-slot representation that avoids boxing
  value-type properties through to the kernel.
- Source generator `[HeraldLog]` for `static partial` log methods.
- Pipeline decorator strategy: swappable, filtering, async, rendering,
  batching, fanOut, flightRecorder, postFiltering, eventProcessing,
  plus a registry for custom decorators.
- Hot-reload via JSON config; atomic pipeline swap with zero event
  loss across the cutover.
- Destructuring policies, multi-tenancy via per-tenant
  `StructuredLogger`, MEL adapter (`HeraldLoggerProvider`),
  flight-recorder ring buffer with trigger-level drain, UTF-8 JSON
  formatter.
- AOT-clean: `IsAotCompatible`, `EnableAotAnalyzer`, and
  `EnableTrimAnalyzer` enabled at the project level.
- Workhorse test suite covering build, kernel fan-out, level
  filtering, multi-tenancy, hot reload, sink isolation, and
  plugin-trust paths (17 tests, all passing on net8 / net9 / net10).
- Benchmark suite under `benchmarking/library/{net8,net9,net10}/`
  (narrow Herald-only across TFMs) and
  `benchmarking/comparisons/net10/` (head-to-head vs Serilog, NLog,
  MEL, ZLogger, log4net).

### Removed (relative to Herald.Core 98d23fd)

- All edition-gating machinery (`HeraldEdition`, `HeraldEditionGate`,
  `src/Licensing/`).
- Provenance-gate sink decorator (`GenSourceGatedSink`,
  `ExternalSourceRegistrar`) and the `GenSource` field plumbing
  through `LogEvent`, `LogEventBuffer`, factories, and bootstrap.
- The `HERALDxxx` analyzer set that warns callers about gated APIs at
  compile time.
- Distribution-hardening tooling (Obfuscar config, promote scripts,
  hardened-output paths).
- `src/Addons/ManagementApi/` — the Management API ships in Herald.Pro.
- `Modules/Core/docs/`, `CLA/`, `manifests/` — documentation seeded
  deliberately rather than inherited.

See `FORK_SCOPE.md` for the authoritative diff.

[Unreleased]: https://github.com/mmpworks/Herald.OSS/compare/v0.4.0...HEAD
[0.4.0]: https://github.com/mmpworks/Herald.OSS/compare/v0.3.0...v0.4.0
[0.3.0]: https://github.com/mmpworks/Herald.OSS/compare/v0.2.3...v0.3.0
[0.2.3]: https://github.com/mmpworks/Herald.OSS/compare/v0.2.2...v0.2.3
[0.2.2]: https://github.com/mmpworks/Herald.OSS/compare/v0.2.1...v0.2.2
[0.2.1]: https://github.com/mmpworks/Herald.OSS/compare/v0.2.0...v0.2.1
[0.2.0]: https://github.com/mmpworks/Herald.OSS/releases/tag/v0.2.0
[0.1.0]: https://github.com/mmpworks/Herald.OSS/releases/tag/v0.1.0

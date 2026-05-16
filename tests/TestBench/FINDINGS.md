# Testbench findings — 3-tenant NDJSON scenario

Branch: `testbench` (off `main` at `35817a5`)
Run date: 2026-05-16
Test suite: 8 tests, 8 passing
Last updated: 2026-05-16 after the Finding 2 resolution landed on
`testbench` and the architectural fix went to main as 0.2.2.

## Verdict on the tenant system

**The tenant system itself works.** Cross-tenant routing, scope flow,
ordering, and observation events all behave as the leak-prevention
contract (10 invariants from the B-1..B-7 seam work) requires.

| Failure mode pre-mortem'd | Result |
|---|---|
| F1 — cross-tenant content leak | ✅ no leaks across 600 events |
| F2 — AsyncLocal scope drop under Task.Run | ✅ scope flows correctly |
| F3 — rapid tenant switching | ✅ 999 iterations, zero misroutes |
| F5 — concurrent producers, NDJSON validity | ✅ 3 × 200 events, all complete |
| F6 — distribution sanity (uniform random) | ✅ within 150-250 per tenant |
| F7 — order preservation within tenant | ✅ 100 events, strict order |
| F8 — lookup-miss event carries scoped tenant | ✅ scoped tenant in payload |

## Friction points the testbench surfaced

The route to a clean test run wasn't straight. Two real issues turned
up along the way; both are documented here so the next person who
walks into this branch sees the shape.

### Finding 1 — Pipeline DisposeAsync doesn't reach sync IDisposable sinks

`QuickLogResult.DisposeAsync()` is currently:

```csharp
public async ValueTask DisposeAsync()
{
    if (_bootstrapResult.AsyncResource is not null)
        await _bootstrapResult.AsyncResource.DisposeAsync().ConfigureAwait(false);
}
```

`_bootstrapResult.AsyncResource` is nullable. For a sync pipeline that
doesn't call `WithAsync(...)`, the field is null and `DisposeAsync` is
a no-op. Any sink that implements `IDisposable` (sync) — including
the file sink from `Herald.Sinks.File` — is never disposed through
the registration's disposal chain.

Concretely observed:

1. Pipeline registers with `WithFileSink("alpha.ndjson")`.
2. Test logs 50 events through the pipeline.
3. `await harness.DisposeAsync()` calls
   `HeraldRegistry.RemoveAsync → entry.DisposeAsync → Result.DisposeAsync`.
4. `Result.DisposeAsync` sees `AsyncResource is null`, returns.
5. The file sink's `IDisposable.Dispose` is never called.
6. Test tries to read the .ndjson file:
   - With `FileShare.Read`: throws `"file is being used by another process"`.
   - With `FileShare.ReadWrite`: opens but reads truncated JSON
     (unflushed buffered writes).

The file-sink exhibit test (`File_sink_disposal_finding_exhibit`)
captures this behaviour as a soft assertion so the finding stays
observable across runs without blocking the suite.

#### Rosanne's recommended seam

`QuickLogResult.DisposeAsync` should reach every disposable layer in
the pipeline, not just the optional async wrapper. Two shapes that
fit the OSS "hooks present even if not used" philosophy:

**Option A — broaden AsyncResource's responsibility.** Have the
bootstrap always populate `AsyncResource`, even for sync pipelines,
with an aggregator that owns the chain of sinks/loggers and calls
`Dispose()` on each `IDisposable` plus `DisposeAsync()` on each
`IAsyncDisposable`. The disposal contract from the consumer's point
of view stays `await using var result = builder.Build()` — what
changes is what's inside the AsyncResource.

**Option B — add a sync Dispose seam.** Have `QuickLogResult`
implement `IDisposable` alongside `IAsyncDisposable`, where
`Dispose()` walks the pipeline and disposes sync resources. Consumers
that use `await using` get the async path; consumers that don't get
the sync path. The risk is a divergent code path; the win is no
behaviour change for current async-pipeline consumers.

Either way, the underlying constraint is the same: **a sync
pipeline with a sync sink must release its resources on disposal.**
Today neither shape handles that case.

### Finding 2 — One-shot naming-policy announcement leaked into user sinks — **RESOLVED in 0.2.2**

**Original observation.** Each `StructuredLogger` emitted a
one-time naming-policy announcement event on first dispatch. When a
test bridged to an in-memory capturer via `WithBridge(...)`, the
announcement landed in the capture buffer and skewed count
assertions by +1 per pipeline. The original framing called this "by
design" with `SuppressNamingPolicyAnnouncement()` as the escape
hatch.

**That framing was wrong.** A framework signal landing in the user's
sink IS the leak — it's the same wall-breach as a cross-tenant
event arriving in the wrong tenant's file. The suppression knob made
the test pass but hid the architectural problem: the framework was
forcing every consumer to remember to filter out framework lines
from their tenant-scoped sinks.

**Resolution (0.2.2).** Runtime signals now publish to a separate
process-wide channel: `MMP.Herald.Diagnostics.HeraldRuntimeMessages`.
User pipelines never see them. The announcement specifically is
emitted via `HeraldRuntimeMessages.Publish(...)` instead of
`StructuredLogger.Log(...)`. The 4th sink in the testbench
(`RuntimeNoticesCaptured` on the harness) reads from the runtime
channel and observes the announcements; the per-tenant bridges see
only application events.

The `SuppressNamingPolicyAnnouncement()` builder hook still works
as a global silencer for consumers who don't want runtime
diagnostic visibility, but the testbench no longer needs it.

#### Rosanne's note (post-resolution)

The shape that landed: parallel channel + provenance marker. The
`HeraldRuntimeMessages` static facade forwards to
`HeraldHost.Default.RuntimeMessages` — same per-host instance
pattern `HeraldRegistry` uses. Every notice carries
`GenSource = HeraldGenSource.RuntimeNotice` so a downstream consumer
who wants to re-bridge notices into their pipeline can preserve the
provenance marker and let any gate filter on it. Buffer mechanics
are factored into `BoundedNoticeBuffer<T>` which both
`HeraldRuntimeMessagesInstance` and `DiagnosticLogFailureSink`
compose — DRY win that also gave the failure sink a public
`DroppedEntryCount` accessor.

Reviews were run before merging: the-fool produced a pre-mortem
that surfaced the throwing-subscriber issue and the per-host
isolation gap; the code-reviewer agreed on both and added the
buffer-overflow loop and xmldoc-documents-the-bug findings. All
five required-before-merge items landed in the same commit.

## What this branch contains

```
tests/TestBench/
├── TestbenchHarness.cs           — bridge + file harness, MemoryNdjsonSink
├── ThreeTenantNdjsonTests.cs     — 7 tests (6 bridge + 1 file exhibit)
└── FINDINGS.md                   — this file
```

Plus a cross-repo `ProjectReference` from `Herald.OSS.Tests.csproj` to
`Herald.Sinks.File`, which is what the file-sink exhibit and the
sink-package wiring depend on.

## What this branch is NOT for

- This is an exploratory branch. Tests here are not part of the OSS
  regression suite that lands on main.
- The fixes for Finding 1 belong on a separate branch — the testbench
  is the surface for spotting friction, not patching it.
- The cross-repo `ProjectReference` to `Herald.Sinks.File` is
  pragmatic for the branch; reconciling the dependency cleanly for
  main would need its own design (probably the Herald.Sinks.File
  package referenced from a fresh test project, not the main OSS
  test project).

## Suggested next moves

1. **Patch Finding 1 on its own branch.** Either Option A or Option B
   from the seam recommendation. Add a regression test that builds a
   sync pipeline with a sync `IDisposable` sink, disposes, asserts the
   sink saw its `Dispose()` call.
2. **Repeat the bridge-mode pattern for the next scenario.** The
   harness is the reusable bit — the test class for the next
   scenario just adds new `[Fact]`s on top.
3. **Consider a testbench-only test project** if this branch grows.
   Keeping the testbench wired into `Herald.OSS.Tests.csproj` is
   convenient for one branch; if multiple test scenarios accumulate,
   a separate `Herald.OSS.TestBench.csproj` with its own
   `Herald.Sinks.File` reference is cleaner.

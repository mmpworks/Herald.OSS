# Testbench findings — 3-tenant NDJSON scenario

Branch: `testbench` (off `main` at `35817a5`)
Run date: 2026-05-16
Test suite: 7 tests, 7 passing

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

### Finding 2 — One-shot naming-policy announcement noise in bridge mode

Each `StructuredLogger` emits a one-time naming-policy announcement
event on first dispatch. When a test bridges to an in-memory capturer
via `WithBridge(...)`, that announcement lands in the capture buffer
and skews count assertions by +1 per pipeline.

This is **by design** — the announcement is a load-bearing signal for
users adopting Pascal-default naming. The escape hatch is
`builder.SuppressNamingPolicyAnnouncement()`, which the testbench
harness now calls before `.WithBridge(...)` so per-tenant count
assertions match exactly.

#### Rosanne's note

This is not a seam recommendation — the announcement should fire by
default. But the testbench harness pattern (suppress + bridge) is
worth lifting into other functional test scenarios that count routed
events. Any future test that asserts "events sent == events captured"
through a bridge needs the same suppression.

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

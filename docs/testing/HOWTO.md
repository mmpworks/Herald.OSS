# How we do tests — Herald.OSS

Operational notes for running and adding to the Herald.OSS test suite.
Tests live at `tests/Herald.OSS.Tests.csproj`; reading this doc is the
fast path to knowing which tests exist, where to put new ones, and
what the suite is for.

## Scope — workhorse-only

The Herald.OSS test suite is intentionally narrow. It pins the OSS
public surface end-to-end and covers the canonical adoption patterns:

- Build/commit a pipeline through `QuickLogBuilder`.
- Kernel fan-out dispatch across the four arity shapes.
- Minimum-level filtering rejects sub-floor events.
- Multi-tenant isolation by structural separation.
- Custom sink provider trust boundary (per-builder scoping).

This is **not** a port of Herald.Core's 256-file suite. Tests that
exercise stripped surfaces (provenance gate, edition machinery,
Pro/Enterprise extensions, fast-path companions still in flux) do not
belong here.

Phase-4 beachhead landed five test files / 17 tests covering the
patterns above. Expansion is deliberate — new tests come in only when
they pin a workhorse behavior that's not already covered.

## Default scope — multi-target

The test csproj inherits `Directory.Build.props`'s
`HeraldTargetFrameworks` setting, so it builds and runs against
net8.0, net9.0, and net10.0 by default. All three target frameworks
must pass before any commit lands.

## Running the tests

### Run everything

```bash
cd E:/dev/Herald.OSS
dotnet test tests/Herald.OSS.Tests.csproj -c Release
```

Output: three test runs (one per TFM), each reporting Passed / Failed
counts and per-test duration.

### Run a single target framework

```bash
dotnet test tests/Herald.OSS.Tests.csproj -c Release -f net10.0
```

Useful when iterating on a test — sub-second feedback per run.

### Run a filtered subset

xUnit's filter syntax targets the fully-qualified test method name:

```bash
dotnet test tests/Herald.OSS.Tests.csproj -c Release -f net10.0 \
  --filter "FullyQualifiedName~KernelFanOut"
```

Common operators:

- `~` — substring match
- `=` — exact match
- `!=` / `!~` — negation
- `&` / `|` — boolean compose

## Test directory layout

```
tests/
  Herald.OSS.Tests.csproj
  Pipeline/                  # kernel + pipeline behaviors
    BuildSmokeTests.cs
    KernelFanOutTests.cs
    LevelFilterTests.cs
  Quick/                     # QuickLogBuilder + QuickLogResult surface
    MultiTenantIsolationTests.cs
    CustomSinkProviderTrustTests.cs
```

The folder layout mirrors `src/` semantically — tests for code under
`src/Pipeline/` go in `tests/Pipeline/`, etc. The intent is one obvious
place to look for tests covering a given area.

## Adding new tests

### Class shape

```csharp
#nullable enable

using FluentAssertions;
using MMP.Herald.Events;
using MMP.Herald.Quick;
using Xunit;

namespace MMP.Herald.OSS.Tests.<Area>;

public sealed class <Behavior>Tests
{
    [Fact]
    public void <Method>_<expected_behavior>()
    {
        // arrange
        // act
        // assert
    }
}
```

Naming conventions:

- **Test class** — `<Behavior>Tests` (sealed, public, in
  `MMP.Herald.OSS.Tests.<Area>` namespace).
- **Test method** — snake-case sentence describing what the test
  pins, e.g. `Single_sink_receives_one_event` or
  `Events_below_minimum_level_do_not_reach_the_bridge_sink`. Reads
  cleanly in the xUnit output.

### Assertions

- Prefer `FluentAssertions` — `.Should().Be(...)`, `.Should().ContainSingle(...)`.
  The error messages on failure are far easier to parse than xUnit's
  built-ins.
- For collections from concurrent producers, use `ConcurrentBag<T>` or
  similar thread-safe containers. Multi-tenancy and fan-out tests
  exercise multi-threaded paths even when the test looks single-
  threaded.

### Test helpers

The suite does not currently ship a shared helpers library — each test
file declares any test-double sinks it needs as nested private types
(`CapturingKernelSink`, `CapturingLogger`, `TestSinkProvider`). This
keeps each test file self-contained.

If a helper pattern repeats across three or more files, extract it
into `tests/Helpers/` and re-use. Premature extraction (one or two
uses) clutters more than it helps.

## What the suite is for

The tests in this repo serve three purposes, in priority order:

1. **Regression gate.** A change that breaks `BuildAndCommit().Logger.Info(...)`
   end-to-end fails CI before it lands.
2. **Worked examples.** A new adopter who reads the tests sees the
   canonical OSS usage patterns. Tests double as informal API docs.
3. **Surface tripwire.** A future strip that accidentally removes a
   public method (e.g. `WithCustomSinkProvider`) fails the trust test,
   not a downstream consumer.

Tests that exist for any other reason — coverage chasing, micro-
behaviors of internal types, paranoid edge cases — belong in
Herald.Core, not Herald.OSS.

## CI expectations

A commit must keep `dotnet test tests/Herald.OSS.Tests.csproj -c Release`
green on every target framework before it lands on `main`. Local
verification before push is the same one-line command.

When CI is wired up (planned for v0.1.0 release), it will run the same
command in a clean checkout on net8/net9/net10. There is no separate
"long" or "smoke" tier — the suite is small enough that every test
runs on every commit.

## What this doc does not cover

- Benchmark methodology: see [`../benchmarks/HOWTO.md`](../benchmarks/HOWTO.md).
- API reference: see XML doc on the public types in `src/`.
- Architecture overview: see [`../guides/architecture.md`](../guides/architecture.md).

// StructuredLogger typed-args overloads — Trace / Debug / Info / Warn /
// Error each across arity 1..16 — used to live here as 80 hand-written
// public methods plus a handful of dispatchers. They now ship from
// MMP.Herald.Generators.TypedArgsOverloadGenerator and land at compile
// time as StructuredLogger.TypedArgs.Generated.cs in the obj/ tree.
//
// Why a generator owns the surface now:
//
//   - [OverloadResolutionPriority(N)] must be applied to every overload
//     to defeat C#'s "fewer omitted optional parameters wins" tiebreaker.
//     Forgetting it on a single one reintroduces the silent-2-prop-instead-
//     of-4 bug at that boundary. A generator applies the attribute by
//     construction; hand-rolling 80 methods makes drift inevitable.
//   - The arity → InlineArray-buffer mapping (1→Buffer1, 2→Buffer2,
//     3-4→Buffer4, 5-8→Buffer8, 9-16→Buffer16) lives in one place rather
//     than being repeated across 16 dispatchers.
//   - Adding arity 32 — or a new level like Critical — is now a one-line
//     change in the generator, with regression tests
//     (Modules/Core/tests/Pipeline/StructuredLoggerTypedArgsTests.cs)
//     unchanged because they assert on observable behaviour through the
//     public surface.
//
// AOT footprint: the generator emits plain C#. Each overload remains
// `[MethodImpl(AggressiveInlining)]`, so the JIT and ILC inline at the
// call site. Generic instantiations only generate native code for shapes
// the consumer actually calls; an AOT-published game using only
// `Info<string, int>` pays zero native bytes for the unused 78 overloads.
//
// Architecture: see docs/architecture.md, "Source-generated typed-args
// overloads" section.

#nullable enable

namespace MMP.Herald.Pipeline;

// Empty by design — the actual surface comes from the generator.
public sealed partial class StructuredLogger { }

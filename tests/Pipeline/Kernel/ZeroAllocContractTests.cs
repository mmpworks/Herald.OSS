#nullable enable

using System;
using FluentAssertions;
using MMP.Herald.Events;
using MMP.Herald.OSS.Tests.TestSupport;
using MMP.Herald.Quick;
using MMP.Herald.Templating;
using Xunit;

namespace MMP.Herald.OSS.Tests.Pipeline.Kernel;

/// <summary>
/// Pins the .NET 10 zero-allocation contract for the accept path so it cannot
/// silently regress. Sourced from Echo's coverage inventory (G1.1 / G1.2 /
/// G2.1 / G1.3).
///
/// <para>
/// Verified facts these tests guard (net10.0, MemoryDiagnoser):
/// </para>
/// <list type="bullet">
///   <item>Typed-args structured logging is 0 B at 1/4/8/16 properties when
///   every value is a reference type (string). Time rises with arity;
///   allocation stays 0.</item>
///   <item>On net9.0+, the array/inline-list path (<c>params
///   ReadOnlySpan&lt;LogProperty&gt;</c>) is also 0 B for reference values.
///   On net8.0 that path materializes a List and allocates, so the assertion
///   is gated to net9.0+.</item>
///   <item>The one allocating case: a value-type element routed through the
///   array-style <c>LogProperty</c> overload boxes at the <c>object? Value</c>
///   field — one heap box per such property. Typed-args carry the same
///   primitives (int/long/double/bool/DateTime) without boxing.</item>
/// </list>
///
/// <para>
/// All paths run through a <see cref="QuickLogBuilder.WithNullSink"/> kernel
/// pipeline — the same null-sink shape the competitive benchmarks use — at
/// minimum level "trace" so the level gate never short-circuits the call.
/// </para>
///
/// <para><b>Determinism.</b> Each test measures steady-state per-iteration
/// allocation via <see cref="AllocationProbe"/>, which warms the JIT and the
/// per-template name cache before measuring and reads the per-thread
/// allocation counter across a large loop. Clean paths assert exactly 0.
/// </para>
/// </summary>
public sealed class ZeroAllocContractTests : IDisposable
{
    private const string Template1 = "one {A}";
    private const string Template4 = "four {A} {B} {C} {D}";
    private const string Template8 = "eight {A} {B} {C} {D} {E} {F} {G} {H}";
    private const string Template16 =
        "sixteen {A} {B} {C} {D} {E} {F} {G} {H} {I} {J} {K} {L} {M} {N} {O} {P}";

    // Reference-type (string) property values. Passing these through the
    // typed-args dispatcher stores the reference directly in the compact
    // property's object?/ref slot — no box is materialized.
    private const string A = "alpha";
    private const string B = "bravo";
    private const string C = "charlie";
    private const string D = "delta";
    private const string E = "echo";
    private const string F = "foxtrot";
    private const string G = "golf";
    private const string H = "hotel";
    private const string I = "india";
    private const string J = "juliet";
    private const string K = "kilo";
    private const string L = "lima";
    private const string M = "mike";
    private const string N = "november";
    private const string O = "oscar";
    private const string P = "papa";

    private readonly QuickLogResult _result;

    public ZeroAllocContractTests()
    {
        _result = QuickLogBuilder.Create()
            .WithNullSink()
            .WithMinimumLevel("trace")
            .BuildAndCommit();
    }

    public void Dispose()
    {
        if (_result.AsyncResource is { } resource)
        {
            resource.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    // ── G1.1 / G2.1: Typed-args 0-alloc, all-reference values ────────────
    // One case per arity so a failure names the arity (e.g. "16 props is
    // still 0 B" — the boundary the website now claims). The category-ful
    // typed-args Info<T1..Tn>(LogCategory, string, T1..Tn) overloads route
    // through a stack-allocated LogPropertyBufferN and LogCompact(span);
    // the kernel fans the span out with no heap LogEvent.

    [Fact]
    public void TypedArgs_OneProp_AllStrings_IsZeroAlloc()
    {
        var logger = _result.Logger;
        var bytes = AllocationProbe.BytesPerIteration(
            () => logger.Info(LogCategory.App, Template1, A));

        bytes.Should().Be(0,
            "typed-args with a single reference-type property must not allocate");
    }

    [Fact]
    public void TypedArgs_FourProps_AllStrings_IsZeroAlloc()
    {
        var logger = _result.Logger;
        var bytes = AllocationProbe.BytesPerIteration(
            () => logger.Info(LogCategory.App, Template4, A, B, C, D));

        bytes.Should().Be(0,
            "typed-args with four reference-type properties must not allocate");
    }

    [Fact]
    public void TypedArgs_EightProps_AllStrings_IsZeroAlloc()
    {
        var logger = _result.Logger;
        var bytes = AllocationProbe.BytesPerIteration(
            () => logger.Info(LogCategory.App, Template8, A, B, C, D, E, F, G, H));

        bytes.Should().Be(0,
            "typed-args with eight reference-type properties must not allocate");
    }

    [Fact]
    public void TypedArgs_SixteenProps_AllStrings_IsZeroAlloc()
    {
        var logger = _result.Logger;
        var bytes = AllocationProbe.BytesPerIteration(
            () => logger.Info(LogCategory.App, Template16,
                A, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P));

        bytes.Should().Be(0,
            "typed-args with sixteen reference-type properties must not " +
            "allocate — this is the boundary the website claims");
    }

    // ── G1.2: Typed-args carry primitives without boxing ─────────────────
    // int/long/double/bool/DateTime are specialized in LogPropertyCompact.From
    // and flow into the inline scalar slot, not a box. The typed-args path
    // therefore stays 0 B even with value-type arguments. This is the
    // distinction from the array-style overload tested below.

    [Fact]
    public void TypedArgs_FourProps_PrimitiveValueTypes_IsZeroAlloc()
    {
        var logger = _result.Logger;
        // int, long, double, bool — all specialized, none box.
        var bytes = AllocationProbe.BytesPerIteration(
            () => logger.Info(LogCategory.App, Template4, 7, 9L, 3.14, true));

        bytes.Should().Be(0,
            "typed-args specialize int/long/double/bool into the inline " +
            "scalar slot, so primitive value-types must not box");
    }

#if NET9_0_OR_GREATER
    // ── G2.1: Array / inline-list path, reference values ─────────────────
    // The params ReadOnlySpan<LogProperty> overload (net9.0+). A LogProperty[]
    // converts implicitly to the span. With all-string property VALUES the
    // kernel carries the span without materializing a heap LogEvent — 0 B on
    // net9+. (On net8.0 a LogProperty[] binds the IReadOnlyList overload,
    // which materializes a List and allocates; hence this is net9+ only.)

    [Fact]
    public void ArrayPath_FourProps_AllStrings_IsZeroAlloc()
    {
        var logger = _result.Logger;
        var props = new[]
        {
            new LogProperty("A", A),
            new LogProperty("B", B),
            new LogProperty("C", C),
            new LogProperty("D", D),
        };

        var bytes = AllocationProbe.BytesPerIteration(
            () => logger.Info(LogCategory.App, Template4, props));

        bytes.Should().Be(0,
            "net9.0+: the array/span path with reference-type property " +
            "values must not allocate");
    }

    [Fact]
    public void ArrayPath_EightProps_AllStrings_IsZeroAlloc()
    {
        var logger = _result.Logger;
        var props = new[]
        {
            new LogProperty("A", A), new LogProperty("B", B),
            new LogProperty("C", C), new LogProperty("D", D),
            new LogProperty("E", E), new LogProperty("F", F),
            new LogProperty("G", G), new LogProperty("H", H),
        };

        var bytes = AllocationProbe.BytesPerIteration(
            () => logger.Info(LogCategory.App, Template8, props));

        bytes.Should().Be(0,
            "net9.0+: eight reference-type properties via the span path " +
            "must not allocate");
    }

    [Fact]
    public void ArrayPath_SixteenProps_AllStrings_IsZeroAlloc()
    {
        var logger = _result.Logger;
        var props = new[]
        {
            new LogProperty("A", A), new LogProperty("B", B),
            new LogProperty("C", C), new LogProperty("D", D),
            new LogProperty("E", E), new LogProperty("F", F),
            new LogProperty("G", G), new LogProperty("H", H),
            new LogProperty("I", I), new LogProperty("J", J),
            new LogProperty("K", K), new LogProperty("L", L),
            new LogProperty("M", M), new LogProperty("N", N),
            new LogProperty("O", O), new LogProperty("P", P),
        };

        var bytes = AllocationProbe.BytesPerIteration(
            () => logger.Info(LogCategory.App, Template16, props));

        bytes.Should().Be(0,
            "net9.0+: sixteen reference-type properties via the span path " +
            "must not allocate");
    }

    // ── G1.3: Boxing caveat ──────────────────────────────────────────────
    // A value-type element constructed into a LogProperty boxes at the
    // object? Value field — one heap box per such property. We pin the
    // documented behavior two ways:
    //   (a) the box is exactly one allocation, and
    //   (b) the delta between an all-strings shape and an identical shape with
    //       one int swapped in equals exactly one box.
    // Pinning the DELTA isolates the box from any per-call baseline and makes
    // the assertion robust to runtime box-size differences.

    [Fact]
    public void ArrayPath_ValueTypeElement_BoxesExactlyOnce()
    {
        var logger = _result.Logger;

        var allStrings = new[]
        {
            new LogProperty("A", A),
            new LogProperty("B", B),
            new LogProperty("C", C),
            new LogProperty("D", D),
        };

        // One value-type element (int) swapped into the last slot. The int
        // boxes at LogProperty construction (Value is object?); the array is
        // built fresh each iteration so the box is part of the measured call.
        var withOneInt = new Func<LogProperty[]>(() => new[]
        {
            new LogProperty("A", A),
            new LogProperty("B", B),
            new LogProperty("C", C),
            new LogProperty("D", 42),
        });

        // Baseline: build the all-strings array fresh each iteration too, so
        // the only difference between the two windows is the single box.
        var baselineFactory = new Func<LogProperty[]>(() => new[]
        {
            new LogProperty("A", A),
            new LogProperty("B", B),
            new LogProperty("C", C),
            new LogProperty("D", D),
        });

        var baselineBytes = AllocationProbe.TotalBytes(() =>
        {
            var p = baselineFactory();
            logger.Info(LogCategory.App, Template4, p);
        });

        var boxedBytes = AllocationProbe.TotalBytes(() =>
        {
            var p = withOneInt();
            logger.Info(LogCategory.App, Template4, p);
        });

        const int measured = AllocationProbe.DefaultMeasuredIterations;
        var deltaPerIteration = (double)(boxedBytes - baselineBytes) / measured;

        // One int box on x64 is 24 bytes (object header 16 + 4-byte payload,
        // 8-byte aligned). Assert the delta is exactly one box: the only
        // structural difference between the two windows is the boxed int.
        deltaPerIteration.Should().BeApproximately(24, 0.5,
            "swapping one string element for an int adds exactly one heap box " +
            "(~24 B on x64) — the documented array-path boxing caveat");

        // Sanity: also confirm a value-type element does allocate at all,
        // independent of the precise size, in case a future runtime changes
        // box layout. Build fresh array each call.
        var boxedPerIteration = AllocationProbe.BytesPerIteration(() =>
        {
            var p = withOneInt();
            logger.Info(LogCategory.App, Template4, p);
        });
        boxedPerIteration.Should().BeGreaterThan(0,
            "a value-type element through the array-style overload must box");
    }
#endif
}

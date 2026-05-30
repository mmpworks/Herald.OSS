#nullable enable
#if NET9_0_OR_GREATER

using System;
using FluentAssertions;
using MMP.Herald.Levels;
using MMP.Herald.OSS.Tests.TestSupport;
using MMP.Herald.Quick;
using MMP.Herald.Serilog;
using Xunit;

namespace MMP.Herald.OSS.Tests.Serilog;

/// <summary>
/// G-HOT.2: exact-byte allocation sweep across every arity 1..16 for the
/// Serilog-compat typed-args overloads on <see cref="SerilogLoggerAdapter"/>.
///
/// <para>
/// The generated overloads carry <c>[OverloadResolutionPriority(N)]</c> so the
/// C# compiler prefers them over the <c>params object?[]?</c> fallback. A missing
/// attribute on any arity silently routes all calls at that arity through
/// <c>params object[]</c>, boxing every value-type argument and allocating the
/// array. This sweep catches that regression for all 16 arities, including the
/// high-risk mid-band arities 3, 5–7, and 9–15 identified by Echo.
/// </para>
///
/// <para>
/// <b>Pipeline shape.</b> Every test uses a <c>WithNullSink</c> pipeline at
/// minimum level "verbose" — the same null-sink shape the competitive benchmarks
/// and <see cref="ZeroAllocContractTests"/> use. The null sink discards events
/// without storing them, so no per-event LogEvent allocation enters the measured
/// window. Only the call path itself is under test.
/// </para>
///
/// <para>
/// <b>Template interning.</b> Each entry in <see cref="Templates"/> is a
/// compile-time string literal. The CLR interns string literals at load time, so
/// <c>SerilogTemplateHoleIndex</c> always hits its cache on the second and
/// subsequent calls — first-call parse cost stays in the warmup window.
/// </para>
///
/// <para>
/// <b>Dispatch.</b> <see cref="BuildCall"/> uses a plain switch to build the
/// action; no reflection. The variable is typed as <see cref="SerilogLoggerAdapter"/>
/// (not <see cref="ILogger"/>) so the compiler resolves the typed-generic
/// overloads rather than <c>params object?[]?</c>.
/// </para>
///
/// <para>
/// Gated to net9.0+: <c>MMP.Herald.Serilog</c> does not build for net8.
/// </para>
/// </summary>
public sealed class SerilogHotPathAllocTests : IDisposable
{
    // Null-sink pipeline: same shape as ZeroAllocContractTests.
    // WithNullSink discard events — no per-event LogEvent allocation in the window.
    private readonly QuickLogResult _result;

    public SerilogHotPathAllocTests()
    {
        _result = QuickLogBuilder.Create()
            .WithNullSink()
            .WithMinimumLevel("verbose")
            .BuildAndCommit();
    }

    public void Dispose()
    {
        if (_result.AsyncResource is { } resource)
            resource.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    // ── Interned compile-time string literals, one per arity 1..16 ───────────
    // All holes are default-mode ({Name} with no format/capture specifier).
    // The CLR interns string literals, so the hole-index cache is always warm
    // after the first call; no re-parse enters the measured window.
    private static readonly string[] Templates =
    [
        "",                                                                                                // index 0 unused
        "a {A}",                                                                                          // arity 1
        "a {A} b {B}",                                                                                    // arity 2
        "a {A} b {B} c {C}",                                                                              // arity 3
        "a {A} b {B} c {C} d {D}",                                                                        // arity 4
        "a {A} b {B} c {C} d {D} e {E}",                                                                  // arity 5
        "a {A} b {B} c {C} d {D} e {E} f {F}",                                                            // arity 6
        "a {A} b {B} c {C} d {D} e {E} f {F} g {G}",                                                      // arity 7
        "a {A} b {B} c {C} d {D} e {E} f {F} g {G} h {H}",                                               // arity 8
        "a {A} b {B} c {C} d {D} e {E} f {F} g {G} h {H} i {I}",                                         // arity 9
        "a {A} b {B} c {C} d {D} e {E} f {F} g {G} h {H} i {I} j {J}",                                   // arity 10
        "a {A} b {B} c {C} d {D} e {E} f {F} g {G} h {H} i {I} j {J} k {K}",                             // arity 11
        "a {A} b {B} c {C} d {D} e {E} f {F} g {G} h {H} i {I} j {J} k {K} l {L}",                       // arity 12
        "a {A} b {B} c {C} d {D} e {E} f {F} g {G} h {H} i {I} j {J} k {K} l {L} m {M}",                 // arity 13
        "a {A} b {B} c {C} d {D} e {E} f {F} g {G} h {H} i {I} j {J} k {K} l {L} m {M} n {N}",           // arity 14
        "a {A} b {B} c {C} d {D} e {E} f {F} g {G} h {H} i {I} j {J} k {K} l {L} m {M} n {N} o {O}",     // arity 15
        "a {A} b {B} c {C} d {D} e {E} f {F} g {G} h {H} i {I} j {J} k {K} l {L} m {M} n {N} o {O} p {P}", // arity 16
    ];

    // Return the interned compile-time literal for the given arity.
    private static string TemplateFor(int arity) => Templates[arity];

    // ── BuildCall: zero-alloc arity dispatch ──────────────────────────────────
    // Typed as SerilogLoggerAdapter (not ILogger) so the C# compiler resolves
    // Information<T1>, Information<T1,T2>, … rather than params object?[]?.
    // Switch with int literals; no reflection; no allocation in the dispatcher.
    private static Action BuildCall(SerilogLoggerAdapter log, string template, int arity)
        => arity switch
        {
            1  => () => log.Information(template, 1),
            2  => () => log.Information(template, 1, 2),
            3  => () => log.Information(template, 1, 2, 3),
            4  => () => log.Information(template, 1, 2, 3, 4),
            5  => () => log.Information(template, 1, 2, 3, 4, 5),
            6  => () => log.Information(template, 1, 2, 3, 4, 5, 6),
            7  => () => log.Information(template, 1, 2, 3, 4, 5, 6, 7),
            8  => () => log.Information(template, 1, 2, 3, 4, 5, 6, 7, 8),
            9  => () => log.Information(template, 1, 2, 3, 4, 5, 6, 7, 8, 9),
            10 => () => log.Information(template, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10),
            11 => () => log.Information(template, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11),
            12 => () => log.Information(template, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12),
            13 => () => log.Information(template, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13),
            14 => () => log.Information(template, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14),
            15 => () => log.Information(template, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15),
            16 => () => log.Information(template, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16),
            _  => throw new ArgumentOutOfRangeException(nameof(arity))
        };

    // ── G-HOT.2: Full arity 1..16 sweep — int args, 0 B each ─────────────────
    // A missing [OverloadResolutionPriority] on any arity silently routes to
    // params object[], boxing every int and allocating the array. This theory
    // catches that regression for each arity individually so a failure names
    // the exact arity.
    //
    // Echo high-risk arities: 3, 5–7, 9–15 (gaps in original arity matrix).
    [Theory]
    [InlineData(1)]  [InlineData(2)]  [InlineData(3)]  [InlineData(4)]
    [InlineData(5)]  [InlineData(6)]  [InlineData(7)]  [InlineData(8)]
    [InlineData(9)]  [InlineData(10)] [InlineData(11)] [InlineData(12)]
    [InlineData(13)] [InlineData(14)] [InlineData(15)] [InlineData(16)]
    public void IntArgs_allocate_zero_bytes_at_every_arity(int arity)
    {
        var log = new SerilogLoggerAdapter(_result.Logger);
        var template = TemplateFor(arity);
        var call = BuildCall(log, template, arity);

        // AllocationProbe runs 2 000 warmup iterations before measuring,
        // so the hole-index cache and JIT tiering are settled before the window.
        var bytes = AllocationProbe.BytesPerIteration(call);

        bytes.Should().Be(0,
            $"arity {arity}: int args must route to the typed slot with zero box. " +
            "A non-zero result means [OverloadResolutionPriority] is missing at this arity " +
            "and the call silently fell through to params object[] (boxing every int).");
    }

    // ── Reject-path: below minimum level — 0 B ───────────────────────────────
    // When the level gate short-circuits (IsInformationAcceptable = false),
    // the generated overload returns before touching any buffer.
    // Even params object[] is never allocated; the early return wins.
    [Fact]
    public void RejectedCall_below_minimum_level_allocates_zero()
    {
        // Build a separate pipeline gated at Error so Information is rejected.
        var rejecting = QuickLogBuilder.Create()
            .WithNullSink()
            .WithMinimumLevel("error")
            .BuildAndCommit();

        try
        {
            var log = new SerilogLoggerAdapter(rejecting.Logger);
            const string template = "user {A}";

            var bytes = AllocationProbe.BytesPerIteration(
                () => log.Information(template, 7));

            bytes.Should().Be(0,
                "the IsInformationAcceptable guard short-circuits before any buffer or box");
        }
        finally
        {
            if (rejecting.AsyncResource is { } resource)
                resource.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    // ── Decimal: one box per call ─────────────────────────────────────────────
    // decimal is not a specialized inline scalar (int/long/double/bool are).
    // The typed-args path boxes it exactly once via LogPropertyCompact.From<T>.
    // We measure one box empirically so the assertion is arch-portable —
    // never hardcoded to 24 or 32.
    [Fact]
    public void DecimalArg_boxes_exactly_once_per_call()
    {
        // Empirical box size: measure the allocation of one object boxing on
        // this runtime. Self-referential and architecture-portable.
        long oneBox = AllocationProbe.TotalBytes(() =>
        {
            object boxed = 3.14m;
            _ = boxed; // prevent elision
        }) / AllocationProbe.DefaultMeasuredIterations;

        var log = new SerilogLoggerAdapter(_result.Logger);
        const string template = "val {V}";

        long shimBytes = AllocationProbe.TotalBytes(
            () => log.Information(template, 3.14m))
            / AllocationProbe.DefaultMeasuredIterations;

        shimBytes.Should().Be(oneBox,
            "one decimal arg must box exactly once — equivalent to Serilog's params-object boxing cost");
    }

    // ── Cache-hit delta: repeated calls stay at 0 B ───────────────────────────
    // Confirms the hole-index cache actually works: a warmed second call (which
    // AllocationProbe naturally exercises in its measured window) must not
    // re-parse the template or allocate more than a cold call.
    [Fact]
    public void SecondCall_with_same_interned_template_costs_zero_bytes()
    {
        var log = new SerilogLoggerAdapter(_result.Logger);
        const string template = "user {Id}";

        // Both the warmup and the measured window go through the cached path.
        // The key assertion: 0 B confirms no per-call re-parse or temp allocation.
        var bytes = AllocationProbe.BytesPerIteration(
            () => log.Information(template, 7));

        bytes.Should().Be(0,
            "a warmed, interned template must not re-parse or allocate on repeated calls");
    }
}

#endif

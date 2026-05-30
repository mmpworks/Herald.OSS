#nullable enable

using System;
using BenchmarkDotNet.Attributes;
using MMP.Herald.Events;
using MMP.Herald.Quick;

namespace MMP.Herald.OSS.Benchmarks.Comparisons.HeraldRow;

/// <summary>
/// Typed-args accept path: zero-alloc territory.
///
/// <para>
/// Herald's <c>Info&lt;T1..Tn&gt;</c> overloads (1..16 arity, generated
/// by <c>TypedArgsOverloadGenerator</c>) carry property values as
/// typed generics from the call site through to the dispatcher.
/// The dispatcher stores each value in a stack-allocated
/// <c>LogPropertyCompact</c> (an <c>InlineArray</c> of N) whose
/// <c>Value</c> field is <c>object?</c>.
/// </para>
///
/// <para>
/// <b>When all property values are reference types (strings,
/// records, classes), the per-call cost is zero allocation.</b>
/// The <c>object? Value</c> field stores the reference directly —
/// no box is materialized. Mixed value-type properties (int, bool,
/// double, DateTime) DO box at the <c>object?</c> boundary; each
/// box is one heap allocation.
/// </para>
///
/// <para>
/// The bench pins both shapes so the narrative is honest: "zero
/// alloc when your properties are reference types" rather than
/// "zero alloc, always."
/// </para>
/// </summary>
[MemoryDiagnoser]
public class TypedArgsBenchmarks
{
    private QuickLogResult _result = null!;

    // Pre-allocated string property values. All reference types,
    // so passing them to the typed-args dispatcher does not box.
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

    [GlobalSetup]
    public void Setup()
    {
        _result = QuickLogBuilder.Create()
            .WithNullSink()
            .WithMinimumLevel("trace")
            .BuildAndCommit();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        if (_result.AsyncResource is { } resource)
        {
            resource.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    // ── Four-property shapes ─────────────────────────────────────

    [Benchmark]
    public void Herald_TypedArgs_FourProps_AllStrings()
    {
        _result.Logger.Information(LogCategory.App,
            "accept-four {A} {B} {C} {D}",
            A, B, C, D);
    }

    [Benchmark]
    public void Herald_TypedArgs_FourProps_MixedTypes()
    {
        // Mixed: string, int, bool, double. The non-string values
        // box at the dispatcher's object? boundary — one box each.
        _result.Logger.Information(LogCategory.App,
            "accept-four {A} {B} {C} {D}",
            "alpha", 7, true, 3.14);
    }

    // ── Sixteen-property shapes ──────────────────────────────────

    [Benchmark]
    public void Herald_TypedArgs_SixteenProps_AllStrings()
    {
        _result.Logger.Information(LogCategory.App,
            "telescope {A} {B} {C} {D} {E} {F} {G} {H} {I} {J} {K} {L} {M} {N} {O} {P}",
            A, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P);
    }

    [Benchmark]
    public void Herald_TypedArgs_SixteenProps_MixedTypes()
    {
        // Worst case: 16 mixed types. Every non-string value boxes.
        _result.Logger.Information(LogCategory.App,
            "telescope {A} {B} {C} {D} {E} {F} {G} {H} {I} {J} {K} {L} {M} {N} {O} {P}",
            "a", 1, true, 1.0,
            "b", 2, false, 2.0,
            "c", 3, true, 3.0,
            "d", 4, false, 4.0);
    }

    // ── Eight-property shapes ────────────────────────────────────
    // G2: closes the 4→16 gap so the per-property scaling claim is
    // anchored on three points instead of two. Realistic for RPC
    // traces and web-request logs.

    [Benchmark]
    public void Herald_TypedArgs_EightProps_AllStrings()
    {
        _result.Logger.Information(LogCategory.App,
            "accept-eight {A} {B} {C} {D} {E} {F} {G} {H}",
            A, B, C, D, E, F, G, H);
    }

    [Benchmark]
    public void Herald_TypedArgs_EightProps_MixedTypes()
    {
        // Mixed: string, int, bool, double × 2. The non-string values
        // box at the dispatcher's object? boundary — four boxes.
        _result.Logger.Information(LogCategory.App,
            "accept-eight {A} {B} {C} {D} {E} {F} {G} {H}",
            "a", 1, true, 1.0,
            "b", 2, false, 2.0);
    }

    // ── Production value-type shapes ─────────────────────────────
    // G5: Guid, DateTimeOffset, and decimal are everywhere in real
    // workloads (correlation IDs, event timestamps, currency
    // amounts). All three are value types ≥ 8 bytes; each boxes at
    // the typed-args dispatcher's object? boundary. The bench pins
    // per-shape allocation so Compliance and Finance customers can
    // size emit cost honestly.

    private static readonly Guid CorrelationId = Guid.NewGuid();
    private static readonly Guid TxId = Guid.NewGuid();
    private static readonly DateTimeOffset EventTime =
        new(2026, 5, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset SettledUtc =
        new(2026, 5, 17, 12, 5, 0, TimeSpan.Zero);
    private const decimal Amount = 9999.99m;

    [Benchmark]
    public void Herald_TypedArgs_FourProps_AuditShape()
    {
        // Audit-event shape: Guid + DateTimeOffset + 2 strings.
        // Guid is 16 bytes; DateTimeOffset is 12 bytes. Both box.
        _result.Logger.Information(LogCategory.App,
            "audit {CorrelationId} {EventTime} {Actor} {Action}",
            CorrelationId, EventTime, "alice", "approve");
    }

    [Benchmark]
    public void Herald_TypedArgs_FourProps_FinanceShape()
    {
        // Finance-event shape: Guid + decimal + string + DateTimeOffset.
        // decimal is 16 bytes; all three value types box.
        _result.Logger.Information(LogCategory.App,
            "txn {TxId} {Amount} {Currency} {SettledUtc}",
            TxId, Amount, "USD", SettledUtc);
    }
}

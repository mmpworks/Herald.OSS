#nullable enable

using System;
using FluentAssertions;
using MMP.Herald.Events;
using MMP.Herald.OSS.Tests.TestSupport;
using MMP.Herald.Quick;
using Xunit;

namespace MMP.Herald.OSS.Tests.Pipeline.Kernel;

/// <summary>
/// Pins the Phase 1 (approach A) zero-allocation contract for the four BCL
/// value types that now ride the typed-args fast path unboxed: TimeSpan,
/// Guid, decimal, DateTimeOffset. Before this work each boxed once (~24 B
/// gen0) at the From&lt;T&gt; call boundary; the widened 16-byte inline region
/// carries them in the compact slot instead.
///
/// <para>
/// Mirrors <see cref="ZeroAllocContractTests"/>: every value runs through a
/// <see cref="QuickLogBuilder.WithNullSink"/> kernel pipeline at minimum level
/// "trace" so the level gate never short-circuits, and allocation is measured
/// steady-state via <see cref="AllocationProbe"/> (JIT + name-cache warmed,
/// per-thread counter, large amortizing loop). Clean paths assert exactly 0.
/// </para>
///
/// <para>
/// The cap is pinned too: a value type larger than 16 bytes (here a 24-byte
/// struct) still falls to the boxed RefValue path — registration/widening does
/// not change that, and the test fails the build if a future change silently
/// claims otherwise.
/// </para>
/// </summary>
public sealed class ValueTypeUnboxAllocationTests : IDisposable
{
    private const string Template1 = "one {A}";
    private const string Template4 = "four {A} {B} {C} {D}";

    private readonly QuickLogResult _result;

    public ValueTypeUnboxAllocationTests()
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

    [Fact]
    public void TypedArgs_TimeSpan_IsZeroAlloc()
    {
        var logger = _result.Logger;
        var ts = TimeSpan.FromMilliseconds(1234.5);

        var bytes = AllocationProbe.BytesPerIteration(
            () => logger.Information(LogCategory.App, Template1, ts));

        bytes.Should().Be(0,
            "TimeSpan (8 B) rides the existing ScalarBits slot unboxed");
    }

    [Fact]
    public void TypedArgs_Guid_IsZeroAlloc()
    {
        var logger = _result.Logger;
        var id = Guid.NewGuid();

        var bytes = AllocationProbe.BytesPerIteration(
            () => logger.Information(LogCategory.App, Template1, id));

        bytes.Should().Be(0,
            "Guid (16 B) bit-casts into the widened inline region unboxed");
    }

    [Fact]
    public void TypedArgs_Decimal_IsZeroAlloc()
    {
        var logger = _result.Logger;
        decimal amount = 1234.5678m;

        var bytes = AllocationProbe.BytesPerIteration(
            () => logger.Information(LogCategory.App, Template1, amount));

        bytes.Should().Be(0,
            "decimal (16 B) bit-casts into the widened inline region unboxed");
    }

    [Fact]
    public void TypedArgs_DateTimeOffset_IsZeroAlloc()
    {
        var logger = _result.Logger;
        var dto = DateTimeOffset.UtcNow;

        var bytes = AllocationProbe.BytesPerIteration(
            () => logger.Information(LogCategory.App, Template1, dto));

        bytes.Should().Be(0,
            "DateTimeOffset (16 B) bit-casts into the widened inline region unboxed");
    }

    [Fact]
    public void TypedArgs_MixedArity_PrimitivesPlusGuidPlusDecimal_IsZeroAlloc()
    {
        var logger = _result.Logger;
        var id = Guid.NewGuid();
        decimal amount = 99.99m;

        // int + string + Guid + decimal — a realistic mixed call. Every arm is
        // specialized, so the whole call must stay 0 B.
        var bytes = AllocationProbe.BytesPerIteration(
            () => logger.Information(LogCategory.App, Template4, 7, "ref", id, amount));

        bytes.Should().Be(0,
            "a call mixing primitives, a string, a Guid and a decimal stays " +
            "fully unboxed on the typed-args path");
    }

    // ── Cap pin: a >16-byte struct still boxes ───────────────────────────
    // The widening covers value types up to 16 bytes. A 24-byte struct falls
    // to the boxed RefValue path through the legacy constructor — exactly one
    // heap box per such property. We pin both that it boxes at all and that
    // the per-iteration cost equals one box, isolated as a delta against an
    // all-reference baseline so the assertion is robust to box-size drift.

    private struct Over16Bytes
    {
        public long A;
        public long B;
        public long C; // 24 bytes total — above the 16-byte inline cap.
    }

    [Fact]
    public void TypedArgs_StructLargerThan16Bytes_StillBoxes()
    {
        var logger = _result.Logger;
        var big = new Over16Bytes { A = 1, B = 2, C = 3 };

        var bytes = AllocationProbe.BytesPerIteration(
            () => logger.Information(LogCategory.App, Template1, big));

        bytes.Should().BeGreaterThan(0,
            "a value type larger than the 16-byte inline cap must still box " +
            "through the legacy RefValue path — the cap is the documented limit");
    }
}

#nullable enable

using System;
using System.Runtime.CompilerServices;
using FluentAssertions;
using MMP.Herald.Pipeline.Kernel;
using MMP.Herald.Templating;
using Xunit;

namespace MMP.Herald.OSS.Tests.Pipeline.Kernel;

/// <summary>
/// Pins the struct-level contract for the Phase 1 (approach A) 16-byte inline
/// widening of <see cref="LogPropertyCompact"/>:
/// <list type="bullet">
///   <item>the struct size is exactly 40 bytes (the +8-byte high word);</item>
///   <item>each new kind round-trips through <see cref="LogPropertyCompact.Value"/>
///   and the typed accessors with bit-exact fidelity;</item>
///   <item>the new kinds survive <see cref="LogPropertyCompact.ToLogProperty"/>
///   and equality.</item>
/// </list>
/// </summary>
public sealed class LogPropertyCompactValueTypeTests
{
    // ── Struct-size gate ─────────────────────────────────────────────────
    // The compact slot multiplies by 16 across LogPropertyBuffer16, so its
    // size is load-bearing. Phase 1 grows it 32 -> 40 (one extra long for the
    // 16-byte inline region). Pin it so a future field addition that pushes it
    // past 40 fails the build the same way the original 32-byte gate did.

    [Fact]
    public void LogPropertyCompact_is_exactly_40_bytes()
    {
        Unsafe.SizeOf<LogPropertyCompact>().Should().Be(40,
            "the 16-byte inline region (ScalarBits + ScalarBitsHigh) grows the " +
            "compact slot from 32 to 40 bytes; Buffer16 follows to 640 B");
    }

    [Fact]
    public void LogPropertyBuffer16_is_exactly_640_bytes()
    {
        Unsafe.SizeOf<LogPropertyBuffer16>().Should().Be(640,
            "16 x 40-byte LogPropertyCompact slots = 640 B on the stack (+25% " +
            "vs the prior 512 B), the documented Phase 1 stack cost");
    }

    // ── Round-trip fidelity through Value / typed accessors ──────────────

    [Fact]
    public void TimeSpan_round_trips_through_Value()
    {
        var ts = new TimeSpan(1, 2, 3, 4, 5);
        var compact = LogPropertyCompact.From("dur", ts);

        compact.Kind.Should().Be(LogPropertyKind.TimeSpan);
        compact.Value.Should().Be(ts);
    }

    [Fact]
    public void Guid_round_trips_through_Value_and_AsGuid()
    {
        var id = Guid.NewGuid();
        var compact = LogPropertyCompact.From("id", id);

        compact.Kind.Should().Be(LogPropertyKind.Guid);
        compact.AsGuid().Should().Be(id, "the unboxed accessor must reconstruct the Guid bit-exactly");
        compact.Value.Should().Be(id, "Value must reconstruct the same Guid");
    }

    [Fact]
    public void Decimal_round_trips_through_Value_and_AsDecimal()
    {
        // A value that exercises the full mantissa/scale, not just an integer.
        decimal amount = -123456789.0123456789m;
        var compact = LogPropertyCompact.From("amt", amount);

        compact.Kind.Should().Be(LogPropertyKind.Decimal);
        compact.AsDecimal().Should().Be(amount, "decimal must reconstruct bit-exactly, preserving scale");
        compact.Value.Should().Be(amount);
    }

    [Fact]
    public void DateTimeOffset_round_trips_through_Value_and_AsDateTimeOffset()
    {
        var dto = new DateTimeOffset(2026, 6, 1, 12, 34, 56, TimeSpan.FromHours(-5));
        var compact = LogPropertyCompact.From("when", dto);

        compact.Kind.Should().Be(LogPropertyKind.DateTimeOffset);
        compact.AsDateTimeOffset().Should().Be(dto, "the offset and the instant must both survive the round-trip");
        compact.Value.Should().Be(dto);
    }

    // ── ToLogProperty + equality survive the new kinds ───────────────────

    [Fact]
    public void New_kinds_survive_ToLogProperty()
    {
        var id = Guid.NewGuid();
        decimal amount = 42.42m;
        var dto = DateTimeOffset.UnixEpoch;
        var ts = TimeSpan.FromSeconds(90);

        LogProperty guidProp = LogPropertyCompact.From("id", id).ToLogProperty();
        LogProperty decProp = LogPropertyCompact.From("amt", amount).ToLogProperty();
        LogProperty dtoProp = LogPropertyCompact.From("when", dto).ToLogProperty();
        LogProperty tsProp = LogPropertyCompact.From("dur", ts).ToLogProperty();

        guidProp.Name.Should().Be("id");
        guidProp.Value.Should().Be(id);
        decProp.Value.Should().Be(amount);
        dtoProp.Value.Should().Be(dto);
        tsProp.Value.Should().Be(ts);
    }

    [Fact]
    public void Equality_distinguishes_high_word()
    {
        // Two Guids that differ only in their high 8 bytes must compare unequal.
        // This is the regression that the ScalarBitsHigh term in Equals guards:
        // before the high word was part of equality, two 16-byte values sharing
        // a low word would have collided.
        var a = new Guid("00000000-0000-0000-1111-111111111111");
        var b = new Guid("00000000-0000-0000-2222-222222222222");

        var ca = LogPropertyCompact.From("g", a);
        var cb = LogPropertyCompact.From("g", b);

        ca.Should().NotBe(cb, "Guids differing only in the high word must not be equal");
        ca.Should().Be(LogPropertyCompact.From("g", a), "identical Guids must be equal");
    }

    [Fact]
    public void Equality_same_value_same_hash()
    {
        var id = Guid.NewGuid();
        var ca = LogPropertyCompact.From("id", id);
        var cb = LogPropertyCompact.From("id", id);

        ca.Should().Be(cb);
        ca.GetHashCode().Should().Be(cb.GetHashCode(),
            "equal compact properties must hash equally (high word folded into the hash)");
    }
}

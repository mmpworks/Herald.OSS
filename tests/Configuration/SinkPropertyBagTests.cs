// Copyright (c) 2026 MMPWorks LLC
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.
#nullable enable

using System.Collections.Generic;
using FluentAssertions;
using MMP.Herald.Configuration.Sinks;
using Xunit;

// Namespace deliberately avoids `MMP.Herald.OSS.Tests.Configuration`
// because that segment shadows `MMP.Herald.Configuration` for any
// sibling test file that uses the unqualified `Configuration.X`
// form (e.g., tests/Pipeline/BuildSmokeTests.cs references
// `Configuration.PipelineStrategy`). Matches the existing convention
// in this directory: KnownSinkCapabilityTests uses
// `MMP.Herald.OSS.Tests.PipelineStrategyTests` for the same reason.
namespace MMP.Herald.OSS.Tests.SinkPropertyBagTests;

/// <summary>
/// Pins the read-side contract on <see cref="SinkPropertyBag"/>. The
/// 17 already-migrated sink-runtime-config helpers in Herald.Sinks
/// (Pass-1 + Pass-2) rely on every behaviour pinned here — empty
/// strings coerce to null, missing keys return null, wrong-type
/// values return null, parsers tolerate operator typos by silently
/// dropping bad pairs. A change that breaks any of these tests would
/// silently drop production config in every sink that depends on
/// the bag-first / legacy-fallback idiom.
/// </summary>
public sealed class SinkPropertyBagTests
{
    // ── ReadString ──────────────────────────────────────────────────

    [Fact]
    public void ReadString_returns_null_when_bag_is_null() =>
        SinkPropertyBag.ReadString(bag: null, "anything").Should().BeNull();

    [Fact]
    public void ReadString_returns_null_when_key_is_absent() =>
        SinkPropertyBag.ReadString(new Dictionary<string, object?>(), "missing").Should().BeNull();

    [Fact]
    public void ReadString_returns_null_when_value_is_null()
    {
        var bag = new Dictionary<string, object?> { ["k"] = null };
        SinkPropertyBag.ReadString(bag, "k").Should().BeNull();
    }

    [Fact]
    public void ReadString_coerces_empty_string_to_null()
    {
        // Contract behaviour, not implementation detail. A blank
        // form field arrives as "" and must fall through to the
        // legacy slot — every Pass-1/2 mapper depends on this.
        var bag = new Dictionary<string, object?> { ["k"] = "" };
        SinkPropertyBag.ReadString(bag, "k").Should().BeNull();
    }

    [Fact]
    public void ReadString_returns_the_string_when_present()
    {
        var bag = new Dictionary<string, object?> { ["k"] = "value" };
        SinkPropertyBag.ReadString(bag, "k").Should().Be("value");
    }

    [Fact]
    public void ReadString_stringifies_non_string_values()
    {
        var bag = new Dictionary<string, object?> { ["k"] = 42 };
        SinkPropertyBag.ReadString(bag, "k").Should().Be("42");
    }

    // ── ReadBool ────────────────────────────────────────────────────

    [Fact]
    public void ReadBool_returns_null_when_bag_is_null() =>
        SinkPropertyBag.ReadBool(null, "k").Should().BeNull();

    [Fact]
    public void ReadBool_returns_null_when_key_is_absent() =>
        SinkPropertyBag.ReadBool(new Dictionary<string, object?>(), "k").Should().BeNull();

    [Fact]
    public void ReadBool_returns_boxed_bool_as_is()
    {
        var bag = new Dictionary<string, object?> { ["t"] = true, ["f"] = false };
        SinkPropertyBag.ReadBool(bag, "t").Should().BeTrue();
        SinkPropertyBag.ReadBool(bag, "f").Should().BeFalse();
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("True", true)]
    [InlineData("TRUE", true)]
    [InlineData("false", false)]
    [InlineData("False", false)]
    public void ReadBool_parses_string_carriers_case_insensitively(string raw, bool expected)
    {
        var bag = new Dictionary<string, object?> { ["k"] = raw };
        SinkPropertyBag.ReadBool(bag, "k").Should().Be(expected);
    }

    [Theory]
    [InlineData("yes")]
    [InlineData("1")]
    [InlineData("on")]
    [InlineData("garbage")]
    public void ReadBool_returns_null_on_unparseable_string(string raw)
    {
        var bag = new Dictionary<string, object?> { ["k"] = raw };
        SinkPropertyBag.ReadBool(bag, "k").Should().BeNull();
    }

    // ── ReadInt ─────────────────────────────────────────────────────

    [Fact]
    public void ReadInt_returns_null_when_bag_is_null() =>
        SinkPropertyBag.ReadInt(null, "k").Should().BeNull();

    [Fact]
    public void ReadInt_returns_boxed_int_as_is()
    {
        var bag = new Dictionary<string, object?> { ["k"] = 42 };
        SinkPropertyBag.ReadInt(bag, "k").Should().Be(42);
    }

    [Fact]
    public void ReadInt_truncates_boxed_long()
    {
        var bag = new Dictionary<string, object?> { ["k"] = 30L };
        SinkPropertyBag.ReadInt(bag, "k").Should().Be(30);
    }

    [Fact]
    public void ReadInt_clamps_long_overflow_to_int_max()
    {
        var bag = new Dictionary<string, object?> { ["k"] = long.MaxValue };
        SinkPropertyBag.ReadInt(bag, "k").Should().Be(int.MaxValue);
    }

    [Fact]
    public void ReadInt_clamps_long_underflow_to_int_min()
    {
        var bag = new Dictionary<string, object?> { ["k"] = long.MinValue };
        SinkPropertyBag.ReadInt(bag, "k").Should().Be(int.MinValue);
    }

    [Fact]
    public void ReadInt_truncates_double_toward_zero()
    {
        var bag = new Dictionary<string, object?> { ["k"] = 30.7 };
        SinkPropertyBag.ReadInt(bag, "k").Should().Be(30);
    }

    [Fact]
    public void ReadInt_parses_invariant_culture_string()
    {
        var bag = new Dictionary<string, object?> { ["k"] = "42" };
        SinkPropertyBag.ReadInt(bag, "k").Should().Be(42);
    }

    [Fact]
    public void ReadInt_returns_null_on_unparseable_string()
    {
        var bag = new Dictionary<string, object?> { ["k"] = "not-a-number" };
        SinkPropertyBag.ReadInt(bag, "k").Should().BeNull();
    }

    // ── ReadLong ────────────────────────────────────────────────────

    [Fact]
    public void ReadLong_accepts_boxed_long_int_and_double()
    {
        var bag = new Dictionary<string, object?>
        {
            ["l"] = 10_000_000_000L,
            ["i"] = 30,
            ["d"] = 30.7
        };

        SinkPropertyBag.ReadLong(bag, "l").Should().Be(10_000_000_000L);
        SinkPropertyBag.ReadLong(bag, "i").Should().Be(30L);
        SinkPropertyBag.ReadLong(bag, "d").Should().Be(30L);
    }

    [Fact]
    public void ReadLong_parses_invariant_culture_string()
    {
        var bag = new Dictionary<string, object?> { ["k"] = "10000000000" };
        SinkPropertyBag.ReadLong(bag, "k").Should().Be(10_000_000_000L);
    }

    [Fact]
    public void ReadLong_returns_null_on_unsupported_type()
    {
        var bag = new Dictionary<string, object?> { ["k"] = new object() };
        SinkPropertyBag.ReadLong(bag, "k").Should().BeNull();
    }

    // ── ReadDouble ──────────────────────────────────────────────────

    [Fact]
    public void ReadDouble_accepts_boxed_double_long_int()
    {
        var bag = new Dictionary<string, object?>
        {
            ["d"] = 7.5,
            ["l"] = 30L,
            ["i"] = 14
        };

        SinkPropertyBag.ReadDouble(bag, "d").Should().Be(7.5);
        SinkPropertyBag.ReadDouble(bag, "l").Should().Be(30d);
        SinkPropertyBag.ReadDouble(bag, "i").Should().Be(14d);
    }

    [Fact]
    public void ReadDouble_parses_invariant_culture_fractional_string()
    {
        var bag = new Dictionary<string, object?> { ["k"] = "7.5" };
        SinkPropertyBag.ReadDouble(bag, "k").Should().Be(7.5);
    }

    [Fact]
    public void ReadDouble_returns_null_on_unparseable_string()
    {
        var bag = new Dictionary<string, object?> { ["k"] = "not-a-number" };
        SinkPropertyBag.ReadDouble(bag, "k").Should().BeNull();
    }

    // ── ReadEnum ────────────────────────────────────────────────────

    // Public so xUnit's [Theory] data binder can resolve the parameter
    // type. The enum lives inside this test class because nothing else
    // consumes it.
    public enum SampleFacility { User, Daemon, Local0, Local7, AuthPriv }

    [Theory]
    [InlineData("user",     SampleFacility.User)]
    [InlineData("USER",     SampleFacility.User)]
    [InlineData("daemon",   SampleFacility.Daemon)]
    [InlineData("local0",   SampleFacility.Local0)]
    [InlineData("Local7",   SampleFacility.Local7)]
    [InlineData("authpriv", SampleFacility.AuthPriv)]
    public void ReadEnum_parses_member_names_case_insensitively(string raw, SampleFacility expected)
    {
        var bag = new Dictionary<string, object?> { ["k"] = raw };
        SinkPropertyBag.ReadEnum<SampleFacility>(bag, "k").Should().Be(expected);
    }

    [Fact]
    public void ReadEnum_returns_null_on_unknown_member_name()
    {
        var bag = new Dictionary<string, object?> { ["k"] = "not-a-member" };
        SinkPropertyBag.ReadEnum<SampleFacility>(bag, "k").Should().BeNull();
    }

    [Fact]
    public void ReadEnum_returns_null_when_bag_is_null() =>
        SinkPropertyBag.ReadEnum<SampleFacility>(null, "k").Should().BeNull();

    [Fact]
    public void ReadEnum_returns_null_when_value_is_empty_string()
    {
        // Empty-string-to-null is consistent with ReadString. A blank
        // form field on an enum-shaped knob should fall through to
        // the caller's default, not parse to the first enum member.
        var bag = new Dictionary<string, object?> { ["k"] = "" };
        SinkPropertyBag.ReadEnum<SampleFacility>(bag, "k").Should().BeNull();
    }

    // ── ReadStringMap ───────────────────────────────────────────────

    [Fact]
    public void ReadStringMap_returns_null_when_value_is_not_a_nested_dictionary()
    {
        var bag = new Dictionary<string, object?> { ["k"] = "not a dict" };
        SinkPropertyBag.ReadStringMap(bag, "k").Should().BeNull();
    }

    [Fact]
    public void ReadStringMap_projects_nested_dict_to_string_string()
    {
        var nested = new Dictionary<string, object?>
        {
            ["service"] = "api",
            ["tenant"]  = "acme"
        };
        var bag = new Dictionary<string, object?> { ["k"] = nested };

        var result = SinkPropertyBag.ReadStringMap(bag, "k");

        result.Should().NotBeNull();
        result!["service"].Should().Be("api");
        result["tenant"].Should().Be("acme");
    }

    [Fact]
    public void ReadStringMap_drops_null_values()
    {
        var nested = new Dictionary<string, object?>
        {
            ["service"] = "api",
            ["null_value"] = null
        };
        var bag = new Dictionary<string, object?> { ["k"] = nested };

        var result = SinkPropertyBag.ReadStringMap(bag, "k");

        result.Should().NotBeNull();
        result.Should().ContainKey("service");
        result.Should().NotContainKey("null_value");
    }

    [Fact]
    public void ReadStringMap_returns_null_when_projection_yields_empty_dict()
    {
        var nested = new Dictionary<string, object?> { ["only_null"] = null };
        var bag = new Dictionary<string, object?> { ["k"] = nested };

        SinkPropertyBag.ReadStringMap(bag, "k").Should().BeNull();
    }

    [Fact]
    public void ReadStringMap_uses_case_insensitive_keys()
    {
        var nested = new Dictionary<string, object?> { ["service"] = "api" };
        var bag = new Dictionary<string, object?> { ["k"] = nested };

        var result = SinkPropertyBag.ReadStringMap(bag, "k");

        result.Should().NotBeNull();
        result!["SERVICE"].Should().Be("api");
        result["Service"].Should().Be("api");
    }

    // ── ReadKeyValuePairs ───────────────────────────────────────────

    [Fact]
    public void ReadKeyValuePairs_parses_canonical_comma_equals_format()
    {
        var bag = new Dictionary<string, object?>
        {
            ["k"] = "team=billing, runbook_url=https://runbooks/billing"
        };
        var result = SinkPropertyBag.ReadKeyValuePairs(bag, "k");

        result.Should().NotBeNull();
        result!["team"].Should().Be("billing");
        result["runbook_url"].Should().Be("https://runbooks/billing");
    }

    [Fact]
    public void ReadKeyValuePairs_drops_blank_pairs_and_pairs_without_equals()
    {
        // The form validator catches typos upstream; the runtime
        // parser is the last line. Silently dropping bad pairs is
        // the right call because crashing pipeline boot on a stray
        // comma is worse than ignoring it.
        var bag = new Dictionary<string, object?>
        {
            ["k"] = "team=billing, , malformed_no_equals, =value_no_key, ops=true"
        };
        var result = SinkPropertyBag.ReadKeyValuePairs(bag, "k");

        result.Should().NotBeNull();
        result.Should().HaveCount(2);
        result!["team"].Should().Be("billing");
        result["ops"].Should().Be("true");
    }

    [Fact]
    public void ReadKeyValuePairs_returns_null_when_string_is_absent()
    {
        var bag = new Dictionary<string, object?>();
        SinkPropertyBag.ReadKeyValuePairs(bag, "k").Should().BeNull();
    }

    [Fact]
    public void ReadKeyValuePairs_returns_null_when_no_valid_pairs_survive_parsing()
    {
        var bag = new Dictionary<string, object?>
        {
            ["k"] = " , , malformed_no_equals, =value_no_key"
        };
        SinkPropertyBag.ReadKeyValuePairs(bag, "k").Should().BeNull();
    }

    [Fact]
    public void ReadKeyValuePairs_trims_whitespace_around_keys_and_values()
    {
        var bag = new Dictionary<string, object?>
        {
            ["k"] = "  team  =  billing  ,  ops = true  "
        };
        var result = SinkPropertyBag.ReadKeyValuePairs(bag, "k");

        result.Should().NotBeNull();
        result!["team"].Should().Be("billing");
        result["ops"].Should().Be("true");
    }

    // ── Nullify ─────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void Nullify_returns_null_on_null_or_whitespace_input(string? input) =>
        SinkPropertyBag.Nullify(input).Should().BeNull();

    [Theory]
    [InlineData("value")]
    [InlineData(" with leading whitespace")]
    [InlineData("with trailing whitespace ")]
    public void Nullify_returns_value_unchanged_when_non_whitespace_content_present(string input) =>
        SinkPropertyBag.Nullify(input).Should().Be(input);

    // ── ParseHumanByteSize ──────────────────────────────────────────

    [Theory]
    [InlineData("10MB",  10L * 1024 * 1024)]
    [InlineData("1GB",   1L * 1024 * 1024 * 1024)]
    [InlineData("500KB", 500L * 1024)]
    [InlineData("42B",   42L)]
    [InlineData("100",   100L)]
    public void ParseHumanByteSize_handles_canonical_suffixes(string raw, long expected) =>
        SinkPropertyBag.ParseHumanByteSize(raw).Should().Be(expected);

    [Fact]
    public void ParseHumanByteSize_is_case_insensitive_on_suffix()
    {
        SinkPropertyBag.ParseHumanByteSize("10mb").Should().Be(10L * 1024 * 1024);
        SinkPropertyBag.ParseHumanByteSize("1gb").Should().Be(1L * 1024 * 1024 * 1024);
    }

    [Fact]
    public void ParseHumanByteSize_honours_fractional_values()
    {
        // 1.5 GB rounds to nearest byte.
        SinkPropertyBag.ParseHumanByteSize("1.5GB")
            .Should().Be((long)(1.5 * 1024 * 1024 * 1024));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("garbage")]
    [InlineData("0")]
    [InlineData("-10")]
    [InlineData("0MB")]
    public void ParseHumanByteSize_returns_null_on_empty_unparseable_or_non_positive(string? raw) =>
        SinkPropertyBag.ParseHumanByteSize(raw).Should().BeNull();

    // ── Compositional sanity check ──────────────────────────────────

    [Fact]
    public void Composing_bag_first_with_nullify_legacy_fallback_works_end_to_end()
    {
        // Smoke test the canonical idiom every Pass-1/2 mapper uses:
        //     ReadString(bag, KEY) ?? Nullify(definition.LegacySlot)
        // Two assertions: bag wins when present; legacy slot fills the
        // hole when the bag is missing or blank.
        var bagWithValue = new Dictionary<string, object?> { ["endpoint"] = "https://from-bag" };
        var bagBlank     = new Dictionary<string, object?> { ["endpoint"] = "" };
        IReadOnlyDictionary<string, object?>? noBag = null;

        const string legacyUri = "https://legacy";

        var withBag = SinkPropertyBag.ReadString(bagWithValue, "endpoint")
                      ?? SinkPropertyBag.Nullify(legacyUri);
        withBag.Should().Be("https://from-bag");

        var blankBagFallsThrough = SinkPropertyBag.ReadString(bagBlank, "endpoint")
                                   ?? SinkPropertyBag.Nullify(legacyUri);
        blankBagFallsThrough.Should().Be("https://legacy");

        var noBagFallsThrough = SinkPropertyBag.ReadString(noBag, "endpoint")
                                ?? SinkPropertyBag.Nullify(legacyUri);
        noBagFallsThrough.Should().Be("https://legacy");

        var noBagAndNoLegacy = SinkPropertyBag.ReadString(noBag, "endpoint")
                               ?? SinkPropertyBag.Nullify(value: null);
        noBagAndNoLegacy.Should().BeNull();
    }
}

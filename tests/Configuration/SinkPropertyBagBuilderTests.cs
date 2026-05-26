// Copyright (c) 2026 MMPWorks LLC
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.
#nullable enable

using System.Collections.Generic;
using FluentAssertions;
using MMP.Herald.Configuration.Sinks;
using Xunit;

// Namespace deliberately avoids `MMP.Herald.OSS.Tests.Configuration`
// for the same reason SinkPropertyBagTests does — that segment shadows
// `MMP.Herald.Configuration` for any sibling test file that uses the
// unqualified `Configuration.X` form. Matches the existing convention
// in this directory.
namespace MMP.Herald.OSS.Tests.SinkPropertyBagBuilderTests;

/// <summary>
/// Direct-unit coverage for <see cref="SinkPropertyBagBuilder"/>, the
/// write-side companion to <see cref="SinkPropertyBag"/>. Before this
/// suite, coverage for the builder came only through QuickLogBuilder
/// integration paths — the builder is invoked there, so any regression
/// would surface eventually, but with a long bounce path and no direct
/// signal pointing at the helper itself.
///
/// <para>
/// Mirrors the shape of <c>SinkPropertyBagTests</c>: every public
/// method, every documented behaviour, one happy-path test and one
/// edge-case test per behaviour. The two helpers are symmetric by
/// design (write-side / read-side), so the test files should read
/// symmetrically too.
/// </para>
/// </summary>
public sealed class SinkPropertyBagBuilderTests
{
    // ── Build(contract, userValues) ─────────────────────────────────

    [Fact]
    public void Build_returns_empty_dictionary_when_contract_is_empty()
    {
        var result = SinkPropertyBagBuilder.Build(
            contract: System.Array.Empty<MmpformPropertyDefinition>(),
            userValues: null);

        result.Should().BeEmpty();
    }

    [Fact]
    public void Build_emits_every_contract_key_even_when_user_values_are_absent()
    {
        // The "JSON must carry every property" invariant — operators
        // who don't override a field still get the default written
        // into the bag so the downstream reader sees the key.
        var contract = new[]
        {
            new MmpformPropertyDefinition("endpoint", "string", "https://default.example.com"),
            new MmpformPropertyDefinition("port",     "int",    514L),
            new MmpformPropertyDefinition("use_tls",  "bool",   true),
        };

        var result = SinkPropertyBagBuilder.Build(contract, userValues: null);

        result.Should().HaveCount(3);
        result["endpoint"].Should().Be("https://default.example.com");
        result["port"].Should().Be(514L);
        result["use_tls"].Should().Be(true);
    }

    [Fact]
    public void Build_lets_user_values_override_contract_defaults()
    {
        var contract = new[]
        {
            new MmpformPropertyDefinition("endpoint", "string", "https://default.example.com"),
            new MmpformPropertyDefinition("port",     "int",    514L),
        };
        var userValues = new Dictionary<string, object?>
        {
            ["endpoint"] = "https://override.example.com",
            ["port"]     = 9200L,
        };

        var result = SinkPropertyBagBuilder.Build(contract, userValues);

        result["endpoint"].Should().Be("https://override.example.com");
        result["port"].Should().Be(9200L);
    }

    [Fact]
    public void Build_falls_back_to_default_when_user_value_is_null()
    {
        // Symmetric with the read-side contract: a null user value
        // means "no override," not "operator explicitly set null."
        // The default takes over so the bag never carries a null
        // where the contract promised a value.
        var contract = new[]
        {
            new MmpformPropertyDefinition("endpoint", "string", "https://default.example.com"),
        };
        var userValues = new Dictionary<string, object?>
        {
            ["endpoint"] = null,
        };

        var result = SinkPropertyBagBuilder.Build(contract, userValues);

        result["endpoint"].Should().Be("https://default.example.com");
    }

    [Fact]
    public void Build_falls_back_to_default_when_user_key_is_absent()
    {
        var contract = new[]
        {
            new MmpformPropertyDefinition("endpoint", "string", "https://default.example.com"),
            new MmpformPropertyDefinition("token",    "string", ""),
        };
        var userValues = new Dictionary<string, object?>
        {
            ["endpoint"] = "https://override.example.com",
            // token deliberately absent
        };

        var result = SinkPropertyBagBuilder.Build(contract, userValues);

        result["endpoint"].Should().Be("https://override.example.com");
        result["token"].Should().Be("");
    }

    [Fact]
    public void Build_preserves_contract_declaration_order()
    {
        // The dashboard relies on contract order to render fields in
        // the same order the mmpform declares them. The bag flows
        // through to the JSON payload that the dashboard re-reads, so
        // a sort-by-name or hash-order ordering would shuffle the
        // operator's view between save and reload.
        var contract = new[]
        {
            new MmpformPropertyDefinition("zeta",  "string", "z"),
            new MmpformPropertyDefinition("alpha", "string", "a"),
            new MmpformPropertyDefinition("mike",  "string", "m"),
        };

        var result = SinkPropertyBagBuilder.Build(contract, userValues: null);

        result.Keys.Should().Equal("zeta", "alpha", "mike");
    }

    [Fact]
    public void Build_preserves_contract_order_when_user_values_supply_a_subset()
    {
        // User values shouldn't be able to reshuffle the bag — the
        // contract owns the order. Even when only one user value is
        // present, the keys still come out in declaration order.
        var contract = new[]
        {
            new MmpformPropertyDefinition("zeta",  "string", "z"),
            new MmpformPropertyDefinition("alpha", "string", "a"),
            new MmpformPropertyDefinition("mike",  "string", "m"),
        };
        var userValues = new Dictionary<string, object?>
        {
            ["alpha"] = "A-override",
        };

        var result = SinkPropertyBagBuilder.Build(contract, userValues);

        result.Keys.Should().Equal("zeta", "alpha", "mike");
        result["alpha"].Should().Be("A-override");
    }

    [Fact]
    public void Build_ignores_user_values_with_keys_not_in_contract()
    {
        // The contract is the gate. Stray keys in userValues do not
        // leak into the bag — that keeps the "every JSON property
        // came from the mmpform" invariant tight and prevents typos
        // from accumulating undocumented keys downstream.
        var contract = new[]
        {
            new MmpformPropertyDefinition("endpoint", "string", "https://default.example.com"),
        };
        var userValues = new Dictionary<string, object?>
        {
            ["endpoint"]   = "https://override.example.com",
            ["mystery_key"] = "should-not-appear",
        };

        var result = SinkPropertyBagBuilder.Build(contract, userValues);

        result.Should().HaveCount(1);
        result.Should().ContainKey("endpoint");
        result.Should().NotContainKey("mystery_key");
    }

    [Fact]
    public void Build_carries_typed_defaults_through_unchanged()
    {
        // The MmpformPropertyDefinition carries CLR-typed defaults
        // (long for int, double for float, bool for bool). The
        // builder is type-agnostic — it passes whatever the contract
        // declares straight through. The read-side primitives know
        // how to coerce on the way out; the builder does not coerce
        // on the way in.
        var contract = new[]
        {
            new MmpformPropertyDefinition("name",        "string", "herald"),
            new MmpformPropertyDefinition("port",        "int",    514L),
            new MmpformPropertyDefinition("retain_days", "float",  7.5d),
            new MmpformPropertyDefinition("use_tls",     "bool",   true),
            new MmpformPropertyDefinition("optional",    "string", null),
        };

        var result = SinkPropertyBagBuilder.Build(contract, userValues: null);

        result["name"].Should().BeOfType<string>().And.Be("herald");
        result["port"].Should().BeOfType<long>().And.Be(514L);
        result["retain_days"].Should().BeOfType<double>().And.Be(7.5d);
        result["use_tls"].Should().BeOfType<bool>().And.Be(true);
        result["optional"].Should().BeNull();
    }

    [Fact]
    public void Build_keys_are_ordinal_case_sensitive()
    {
        // The bag's comparer is StringComparer.Ordinal — keys are
        // case-sensitive on the write side, then case-insensitive
        // on the read side via the ReadXxx primitives' lookup
        // semantics (those routes through the consumer's choice of
        // dictionary type). Pinning the write-side comparer here
        // catches an accidental switch to OrdinalIgnoreCase that
        // would collapse "Endpoint" and "endpoint" into one slot.
        var contract = new[]
        {
            new MmpformPropertyDefinition("endpoint", "string", "lowercase-default"),
            new MmpformPropertyDefinition("Endpoint", "string", "PascalCase-default"),
        };

        var result = SinkPropertyBagBuilder.Build(contract, userValues: null);

        result.Should().HaveCount(2);
        result["endpoint"].Should().Be("lowercase-default");
        result["Endpoint"].Should().Be("PascalCase-default");
    }

    // ── Build(mmpformText, userValues) — convenience overload ───────

    [Fact]
    public void Build_string_overload_returns_empty_when_mmpform_text_is_null()
    {
        var result = SinkPropertyBagBuilder.Build(mmpformText: null, userValues: null);

        result.Should().BeEmpty();
    }

    [Fact]
    public void Build_string_overload_returns_empty_when_mmpform_has_no_properties_block()
    {
        // No __properties block means no contract, which means an
        // empty bag. The convenience overload short-circuits before
        // calling Build(contract, …) so the empty case is cheap.
        const string mmpform = """
            columns: 12
            [container("Trivial", "no properties")]
              - [label(12,"Just a label")]
            """;

        var result = SinkPropertyBagBuilder.Build(mmpform, userValues: null);

        result.Should().BeEmpty();
    }

    [Fact]
    public void Build_string_overload_parses_properties_and_merges_user_values()
    {
        // End-to-end: feed a small mmpform string, supply one user
        // value, verify the contract defaults + override both land.
        const string mmpform = """
            columns: 12
            __properties = [
                "endpoint" = { type: "string", default: "https://default.example.com" },
                "port"     = { type: "int",    default: 514 },
                "use_tls"  = { type: "bool",   default: true }
            ]
            [container("Test sink", "two-field sink")]
              - [url(12,{endpoint})] Endpoint
              - [number(12,{port})] Port
              - [checkbox(12,{use_tls})] Use TLS
            """;
        var userValues = new Dictionary<string, object?>
        {
            ["endpoint"] = "https://override.example.com",
        };

        var result = SinkPropertyBagBuilder.Build(mmpform, userValues);

        result.Should().HaveCount(3);
        result["endpoint"].Should().Be("https://override.example.com");
        result["port"].Should().Be(514L);
        result["use_tls"].Should().Be(true);
    }

    // ── Symmetry with SinkPropertyBag (read-side) ───────────────────

    [Fact]
    public void Build_output_round_trips_through_SinkPropertyBag_read_primitives()
    {
        // The two helpers are designed to be each other's mirror. A
        // bag produced by the builder must read back through the
        // SinkPropertyBag primitives without coercion or surprise.
        var contract = new[]
        {
            new MmpformPropertyDefinition("endpoint", "string", "https://default.example.com"),
            new MmpformPropertyDefinition("port",     "int",    514L),
            new MmpformPropertyDefinition("use_tls",  "bool",   true),
        };
        var userValues = new Dictionary<string, object?>
        {
            ["port"]    = 9200L,
            ["use_tls"] = false,
        };

        var bag = SinkPropertyBagBuilder.Build(contract, userValues);

        SinkPropertyBag.ReadString(bag, "endpoint").Should().Be("https://default.example.com");
        SinkPropertyBag.ReadInt(bag, "port").Should().Be(9200);
        SinkPropertyBag.ReadLong(bag, "port").Should().Be(9200L);
        SinkPropertyBag.ReadBool(bag, "use_tls").Should().BeFalse();
    }
}

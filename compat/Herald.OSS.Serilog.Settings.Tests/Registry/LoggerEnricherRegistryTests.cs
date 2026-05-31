// Copyright (c) 2026 MMPWorks LLC
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

#nullable enable

using System;
using FluentAssertions;
using Xunit;

namespace Herald.OSS.Serilog.Settings.Tests.Registry;

/// <summary>
/// Covers the <see cref="LoggerEnricherRegistry"/> public contract:
/// pre-seeded built-ins, custom registration, case-insensitive lookup,
/// collision safety, and null/blank name guards.
/// </summary>
public sealed class LoggerEnricherRegistryTests
{
    // ── Pre-seeded built-in names (all must be registered on Default) ──────────

    [Theory]
    [InlineData("FromLogContext")]
    [InlineData("WithProperty")]
    public void BuiltIn_enricher_names_are_preseeded(string name)
        => LoggerEnricherRegistry.Default.IsRegistered(name).Should().BeTrue();

    // ── Custom name resolves after registration ────────────────────────────────

    [Fact]
    public void Custom_enricher_name_resolves_after_registration()
    {
        var reg = LoggerEnricherRegistry.CreateDefault();
        reg.RegisterEnricher("MyEnricher", (builder, _) => builder);

        reg.IsRegistered("MyEnricher").Should().BeTrue();
    }

    // ── Case-insensitive resolution ────────────────────────────────────────────

    [Theory]
    [InlineData("fromlogcontext")]
    [InlineData("FROMLOGCONTEXT")]
    [InlineData("FromLogContext")]
    [InlineData("withproperty")]
    [InlineData("WITHPROPERTY")]
    public void Name_resolution_is_case_insensitive(string name)
        => LoggerEnricherRegistry.Default.IsRegistered(name).Should().BeTrue();

    // ── Unknown name is not registered ────────────────────────────────────────

    [Fact]
    public void Unknown_name_is_not_registered()
        => LoggerEnricherRegistry.Default.IsRegistered("Serilog.Enrichers.Thread").Should().BeFalse();

    // ── TryResolve returns factory for known names ─────────────────────────────

    [Fact]
    public void TryResolve_returns_true_and_factory_for_known_name()
    {
        var resolved = LoggerEnricherRegistry.Default.TryResolve("FromLogContext", out var factory);

        resolved.Should().BeTrue();
        factory.Should().NotBeNull();
    }

    [Fact]
    public void TryResolve_returns_false_for_unknown_name()
    {
        var resolved = LoggerEnricherRegistry.Default.TryResolve("Serilog.Enrichers.Thread", out var factory);

        resolved.Should().BeFalse();
        factory.Should().BeNull();
    }

    // ── Collision throws ───────────────────────────────────────────────────────

    [Fact]
    public void Registering_over_a_builtin_throws_not_silently_shadows()
    {
        var reg = LoggerEnricherRegistry.CreateDefault();

        reg.Invoking(r => r.RegisterEnricher("FromLogContext", (b, _) => b))
            .Should().Throw<InvalidOperationException>("registration collision must throw, not silently shadow");
    }

    // ── Risk 1 (pre-mortem): explicit null / blank name guard ─────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Null_or_blank_name_is_not_registered(string? name)
        => LoggerEnricherRegistry.Default.IsRegistered(name!).Should().BeFalse();

    // ── Isolated instances don't share state ──────────────────────────────────

    [Fact]
    public void CreateDefault_returns_independent_instances()
    {
        var reg1 = LoggerEnricherRegistry.CreateDefault();
        var reg2 = LoggerEnricherRegistry.CreateDefault();

        reg1.RegisterEnricher("OnlyInReg1", (b, _) => b);

        reg1.IsRegistered("OnlyInReg1").Should().BeTrue();
        reg2.IsRegistered("OnlyInReg1").Should().BeFalse();
    }

    // ── Default singleton is sealed against mutation ──────────────────────────

    [Fact]
    public void Default_singleton_is_sealed_against_mutation()
    {
        LoggerEnricherRegistry.Default
            .Invoking(r => r.RegisterEnricher("InjectedEnricher", (b, _) => b))
            .Should().Throw<InvalidOperationException>("the Default registry must be sealed");
    }
}

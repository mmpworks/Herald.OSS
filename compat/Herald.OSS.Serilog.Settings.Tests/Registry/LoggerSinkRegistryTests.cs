// Copyright (c) 2026 MMPWorks LLC
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

#nullable enable

using System;
using FluentAssertions;
using Xunit;

namespace Herald.OSS.Serilog.Settings.Tests.Registry;

/// <summary>
/// Covers the <see cref="LoggerSinkRegistry"/> public contract:
/// pre-seeded built-ins, custom registration, case-insensitive lookup,
/// collision safety, and null/blank name guards.
/// </summary>
public sealed class LoggerSinkRegistryTests
{
    // ── Pre-seeded built-in names (all must be registered on Default) ──────────

    [Theory]
    [InlineData("Console")]
    [InlineData("File")]
    [InlineData("Http")]
    [InlineData("TCPSink")]
    [InlineData("UDPSink")]
    [InlineData("Elasticsearch")]
    [InlineData("OpenTelemetry")]
    [InlineData("OpenTelemetryProtobuf")]
    [InlineData("Null")]
    public void BuiltIn_sink_names_are_preseeded(string name)
        => LoggerSinkRegistry.Default.IsRegistered(name).Should().BeTrue();

    // ── Custom name resolves after registration ────────────────────────────────

    [Fact]
    public void Custom_sink_name_resolves_after_registration()
    {
        var reg = LoggerSinkRegistry.CreateDefault();
        reg.RegisterSink("MyCompanySink", (builder, _) => builder);

        reg.IsRegistered("MyCompanySink").Should().BeTrue();
    }

    // ── Case-insensitive resolution ────────────────────────────────────────────

    [Theory]
    [InlineData("console")]
    [InlineData("CONSOLE")]
    [InlineData("Console")]
    [InlineData("file")]
    [InlineData("FILE")]
    [InlineData("null")]
    public void Name_resolution_is_case_insensitive(string name)
        => LoggerSinkRegistry.Default.IsRegistered(name).Should().BeTrue();

    // ── Unknown name is not registered ────────────────────────────────────────

    [Fact]
    public void Unknown_name_is_not_registered()
        => LoggerSinkRegistry.Default.IsRegistered("Serilog.Sinks.Seq").Should().BeFalse();

    // ── TryResolve returns factory for known names ─────────────────────────────

    [Fact]
    public void TryResolve_returns_true_and_factory_for_known_name()
    {
        var resolved = LoggerSinkRegistry.Default.TryResolve("Console", out var factory);

        resolved.Should().BeTrue();
        factory.Should().NotBeNull();
    }

    [Fact]
    public void TryResolve_returns_false_for_unknown_name()
    {
        var resolved = LoggerSinkRegistry.Default.TryResolve("Serilog.Sinks.Seq", out var factory);

        resolved.Should().BeFalse();
        factory.Should().BeNull();
    }

    // ── Collision throws — safer than last-write-wins ──────────────────────────

    [Fact]
    public void Registering_over_a_builtin_throws_not_silently_shadows()
    {
        var reg = LoggerSinkRegistry.CreateDefault();

        reg.Invoking(r => r.RegisterSink("Console", (b, _) => b))
            .Should().Throw<InvalidOperationException>("registration collision must throw, not silently shadow");
    }

    [Fact]
    public void Registering_over_a_custom_name_throws_not_silently_shadows()
    {
        var reg = LoggerSinkRegistry.CreateDefault();
        reg.RegisterSink("MySink", (b, _) => b);

        reg.Invoking(r => r.RegisterSink("MySink", (b, _) => b))
            .Should().Throw<InvalidOperationException>("registration collision must throw, not silently shadow");
    }

    // ── Risk 1 (pre-mortem): explicit null / blank name guard ─────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Null_or_blank_name_is_not_registered(string? name)
        => LoggerSinkRegistry.Default.IsRegistered(name!).Should().BeFalse();

    // ── Isolated instances don't share state ──────────────────────────────────

    [Fact]
    public void CreateDefault_returns_independent_instances()
    {
        var reg1 = LoggerSinkRegistry.CreateDefault();
        var reg2 = LoggerSinkRegistry.CreateDefault();

        reg1.RegisterSink("OnlyInReg1", (b, _) => b);

        reg1.IsRegistered("OnlyInReg1").Should().BeTrue();
        reg2.IsRegistered("OnlyInReg1").Should().BeFalse();
    }

    // ── Default singleton is immutable (no RegisterSink path reaches it) ──────

    [Fact]
    public void Default_singleton_is_sealed_against_mutation()
    {
        // Default is read-only; registration is intentionally prohibited.
        LoggerSinkRegistry.Default
            .Invoking(r => r.RegisterSink("InjectedSink", (b, _) => b))
            .Should().Throw<InvalidOperationException>("the Default registry must be sealed");
    }
}

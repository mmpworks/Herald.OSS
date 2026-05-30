// Copyright (c) 2026 MMPWorks LLC
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

#nullable enable

using FluentAssertions;
using Xunit;

namespace Herald.OSS.Serilog.Settings.Tests;

/// <summary>
/// G-SINK-WALL.1 — third-party / community sinks cannot be loaded through the Herald
/// shim because they are compiled against Serilog's strong-named assembly identity
/// (PublicKeyToken=24c2f752a8e58a10) which the unsigned shim cannot satisfy.
/// Every unresolved sink name must produce a named, audited SinkResolutionException,
/// never a silent no-op or a SelfLog record.
/// </summary>
public sealed class SinkWallTests
{
    // The hard wall: these names are NOT registered — attempting to resolve them must fail.
    [Theory]
    [InlineData("Serilog.Sinks.Seq")]
    [InlineData("Seq")]
    [InlineData("Serilog.Sinks.MSSqlServer")]
    [InlineData("MSSqlServer")]
    [InlineData("Serilog.Sinks.Datadog")]
    [InlineData("Datadog")]
    [InlineData("Serilog.Sinks.ApplicationInsights")]
    [InlineData("Elasticsearch_community")]   // distinct from Herald's built-in Elasticsearch verb
    public void Community_sink_names_are_not_registered(string name)
        => LoggerSinkRegistry.Default.IsRegistered(name).Should().BeFalse(
            $"'{name}' is a community sink that cannot be loaded through the unsigned shim");

    // TryResolve returns false and out-factory is null — never calls a factory.
    [Theory]
    [InlineData("Seq")]
    [InlineData("Serilog.Sinks.MSSqlServer")]
    public void TryResolve_returns_false_for_community_sinks(string name)
    {
        LoggerSinkRegistry.Default.TryResolve(name, out var factory).Should().BeFalse();
        factory.Should().BeNull();
    }

    // The exception must contain the sink name (auditable).
    [Fact]
    public void SinkResolutionException_contains_the_sink_name()
    {
        var ex = new SinkResolutionException("Seq", "community sink identity wall");
        ex.SinkName.Should().Be("Seq");
        ex.Message.Should().Contain("Seq");
    }

    // The exception must contain the identity-wall reason (not a generic "not found" message).
    [Fact]
    public void SinkResolutionException_message_contains_identity_wall_explanation()
    {
        var ex = new SinkResolutionException("Seq", "community sink identity wall");
        // The message should mention the assembly identity / PublicKeyToken issue
        ex.Message.Should().ContainAny(
            "identity", "strong-named", "PublicKeyToken", "not strong-named", "unsigned",
            "cannot be loaded");
    }

    // Confirm the built-in Elasticsearch IS registered (not the same as community "Elasticsearch_community")
    [Fact]
    public void Herald_builtin_Elasticsearch_IS_registered()
        => LoggerSinkRegistry.Default.IsRegistered("Elasticsearch").Should().BeTrue(
            "Herald's built-in Elasticsearch sink IS available; only the Serilog community package is blocked");
}

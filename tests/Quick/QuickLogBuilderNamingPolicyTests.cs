#nullable enable

using System;
using System.Collections.Generic;
using FluentAssertions;
using MMP.Herald.Configuration;
using MMP.Herald.Configuration.Json;
using MMP.Herald.Pipeline;
using MMP.Herald.Quick;
using MMP.Herald.Templating;
using MMP.Herald.Templating.NamingPolicies;
using Xunit;

namespace MMP.Herald.OSS.Tests.Quick;

/// <summary>
/// Phase 3: QuickLogBuilder property-naming-policy API surface +
/// JSON round-trip + Reload semantics + RebuildFrom carry-forward.
/// </summary>
[Collection(nameof(QuickLogBuilderNamingPolicyTests))]
[CollectionDefinition(nameof(QuickLogBuilderNamingPolicyTests), DisableParallelization = true)]
public sealed class QuickLogBuilderNamingPolicyTests
{
    public QuickLogBuilderNamingPolicyTests()
    {
        NameResolverCache.Reset();
    }

    // -- Builder API surface --------------------------------------------------

    [Fact]
    public void GetNamingPolicy_returns_PascalCasePolicy_when_unset()
    {
        var builder = QuickLogBuilder.Create().WithConsoleSink();

        builder.GetNamingPolicy().Should().BeSameAs(PascalCasePolicy.Instance);
    }

    [Fact]
    public void WithNamingPolicy_sets_and_GetNamingPolicy_reads()
    {
        var builder = QuickLogBuilder.Create()
            .WithConsoleSink()
            .WithNamingPolicy(PropertyNamingPolicy.Snake);

        builder.GetNamingPolicy().Should().BeSameAs(SnakeCasePolicy.Instance);
    }

    [Fact]
    public void WithNamingPolicy_null_throws_ArgumentNullException()
    {
        var builder = QuickLogBuilder.Create().WithConsoleSink();

        var act = () => builder.WithNamingPolicy(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void BuildAndCommit_installs_the_configured_policy_on_the_logger()
    {
        var result = QuickLogBuilder.Create()
            .WithConsoleSink()
            .WithNamingPolicy(PropertyNamingPolicy.Camel)
            .BuildAndCommit();

        result.Logger.NamingPolicy.Should().BeSameAs(CamelCasePolicy.Instance);
    }

    [Fact]
    public void Default_BuildAndCommit_installs_PascalCasePolicy()
    {
        var result = QuickLogBuilder.Create()
            .WithConsoleSink()
            .BuildAndCommit();

        result.Logger.NamingPolicy.Should().BeSameAs(PascalCasePolicy.Instance);
    }

    // -- JSON round-trip ------------------------------------------------------

    [Fact]
    public void BuildJsonConfig_writes_namingPolicy_id_when_set()
    {
        var builder = QuickLogBuilder.Create()
            .WithConsoleSink()
            .WithNamingPolicy(PropertyNamingPolicy.Snake);

        var buildResult = builder.Build();
        var json = buildResult.ExportConfig();

        json.Should().Contain("\"namingPolicy\"");
        json.Should().Contain("\"snake\"");
    }

    [Fact]
    public void BuildJsonConfig_emits_null_namingPolicy_when_unset()
    {
        // The serializer doesn't drop null fields (no
        // DefaultIgnoreCondition.WhenWritingNull set in the options).
        // When the builder has no explicit policy, the field round-trips
        // as null and the reader resolves null → PascalCasePolicy. Both
        // shapes are valid wire representations of the default; this
        // test pins the actual behaviour so a serializer change doesn't
        // silently flip the wire format.
        var builder = QuickLogBuilder.Create().WithConsoleSink();

        var buildResult = builder.Build();
        var json = buildResult.ExportConfig();

        json.Should().Contain("\"namingPolicy\"");
        json.Should().MatchRegex("\"namingPolicy\"\\s*:\\s*null");
    }

    [Fact]
    public void Roundtrip_BuildJsonConfig_then_FromConfigurationString_restores_policy()
    {
        // Build a configured pipeline, export the JSON, rebuild from JSON
        // → the rebuilt pipeline uses the same policy.
        var original = QuickLogBuilder.Create()
            .WithConsoleSink()
            .WithNamingPolicy(PropertyNamingPolicy.Snake)
            .Build();
        var json = original.ExportConfig();

        var rebuilt = QuickLogBuilder.FromConfigurationString(json).BuildAndCommit();

        rebuilt.Logger.NamingPolicy.Should().BeSameAs(SnakeCasePolicy.Instance);
    }

    [Fact]
    public void FromConfigurationString_with_omitted_namingPolicy_defaults_to_Pascal()
    {
        // Older JSON without the field, or new JSON that intentionally
        // omits it: reader applies the spec default.
        var builder = QuickLogBuilder.Create().WithConsoleSink();
        var json = builder.Build().ExportConfig();

        var rebuilt = QuickLogBuilder.FromConfigurationString(json).BuildAndCommit();

        rebuilt.Logger.NamingPolicy.Should().BeSameAs(PascalCasePolicy.Instance);
    }

    [Fact]
    public void FromConfiguration_unknown_namingPolicy_throws_UnknownNamingPolicyException()
    {
        // Cold-start path (FromConfiguration is the host's startup
        // entry-point): unknown id is fatal. Operators see the bad
        // configuration at startup rather than silently flipping the
        // schema downstream. We construct the bad config via the typed
        // overload so the failure surface is exercised independent of
        // JSON-string injection details.
        var goodConfig = QuickLogBuilder.Create()
            .WithConsoleSink()
            .Build()
            .ExportConfig();

        // Take a valid JSON config and mutate it to carry the bad id.
        // The serializer round-trips through LoggingJsonSerializer so we
        // get a real JsonLoggingConfig that we can hand to FromConfiguration.
        var validConfig = LoggingJsonSerializer.Deserialize(goodConfig);
        var brokenConfig = validConfig with { NamingPolicy = "never-registered-policy" };

        var act = () => QuickLogBuilder.FromConfiguration(brokenConfig);

        act.Should().Throw<UnknownNamingPolicyException>()
            .Where(ex => ex.PolicyId == "never-registered-policy")
            .WithMessage("*never-registered-policy*Register*");
    }

    // -- RebuildFrom carry-forward -------------------------------------------

    [Fact]
    public void RebuildFrom_carries_policy_forward_when_new_builder_does_not_override()
    {
        // Live pipeline configured with Snake; rebuilt with a new builder
        // that says NOTHING about naming → policy survives (no silent
        // flip back to Pascal default). RebuildFrom needs hot-reload
        // enabled on the live pipeline; without it Commit returns false.
        var live = QuickLogBuilder.Create()
            .WithConsoleSink()
            .WithMinimumLevel("info")
            .WithHotReload()
            .WithNamingPolicy(PropertyNamingPolicy.Snake)
            .BuildAndCommit();

        var rebuildBuilder = QuickLogBuilder.Create()
            .WithConsoleSink()
            .WithMinimumLevel("debug") // changed something OTHER than policy
            .WithHotReload();

        var ok = live.RebuildFrom(rebuildBuilder);

        ok.Should().BeTrue();
        live.Logger.NamingPolicy.Should().BeSameAs(SnakeCasePolicy.Instance,
            "RebuildFrom must carry the live policy forward when the rebuild " +
            "builder doesn't explicitly set one");
    }

    [Fact]
    public void RebuildFrom_honours_explicit_WithNamingPolicy_on_the_new_builder()
    {
        // Live pipeline configured with Snake; explicit override in the
        // rebuild builder wins.
        var live = QuickLogBuilder.Create()
            .WithConsoleSink()
            .WithHotReload()
            .WithNamingPolicy(PropertyNamingPolicy.Snake)
            .BuildAndCommit();

        var rebuildBuilder = QuickLogBuilder.Create()
            .WithConsoleSink()
            .WithHotReload()
            .WithNamingPolicy(PropertyNamingPolicy.Camel);

        live.RebuildFrom(rebuildBuilder);

        live.Logger.NamingPolicy.Should().BeSameAs(CamelCasePolicy.Instance,
            "explicit WithNamingPolicy on the rebuild builder must override the carry-forward");
    }
}

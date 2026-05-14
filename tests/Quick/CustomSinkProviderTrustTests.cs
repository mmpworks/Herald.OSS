#nullable enable

using System.Collections.Generic;
using MMP.Herald.Configuration.Runtime;
using MMP.Herald.Levels;
using MMP.Herald.Output.Rendering;
using MMP.Herald.Quick;
using MMP.Herald.Routing;
using FluentAssertions;
using Xunit;

namespace MMP.Herald.OSS.Tests.Quick;

/// <summary>
/// Plugin trust in Herald.OSS is structural: a custom sink provider is
/// scoped to the builder that registered it. The registration does not
/// leak into other builders unless every builder explicitly opts in.
/// These tests pin the contract.
/// </summary>
public sealed class CustomSinkProviderTrustTests
{
    [Fact]
    public void Custom_provider_is_visible_to_the_builder_that_registered_it()
    {
        var provider = new TestSinkProvider("oss-trust-test-1");

        var builder = QuickLogBuilder.Create()
            .WithCustomSinkProvider(provider);

        builder.SinkProviders.Has(provider.SinkKind).Should().BeTrue();
        builder.SinkProviders.Get(provider.SinkKind).Should().BeSameAs(provider);
    }

    [Fact]
    public void Custom_provider_is_not_visible_to_a_separate_builder()
    {
        var provider = new TestSinkProvider("oss-trust-test-2");

        // Builder A registers the custom provider.
        var builderA = QuickLogBuilder.Create()
            .WithCustomSinkProvider(provider);

        // Builder B has not. Its local set never saw it. The process-
        // wide fallback was not touched either, so Has() returns false.
        var builderB = QuickLogBuilder.Create();

        builderA.SinkProviders.Has(provider.SinkKind).Should().BeTrue();
        builderB.SinkProviders.Has(provider.SinkKind).Should().BeFalse();
        builderB.SinkProviders.Get(provider.SinkKind).Should().BeNull();
    }

    [Fact]
    public void Custom_provider_count_is_per_builder()
    {
        var pA = new TestSinkProvider("oss-trust-test-3a");
        var pB1 = new TestSinkProvider("oss-trust-test-3b-1");
        var pB2 = new TestSinkProvider("oss-trust-test-3b-2");

        var builderA = QuickLogBuilder.Create().WithCustomSinkProvider(pA);
        var builderB = QuickLogBuilder.Create()
            .WithCustomSinkProvider(pB1)
            .WithCustomSinkProvider(pB2);

        builderA.SinkProviders.Count.Should().Be(1);
        builderB.SinkProviders.Count.Should().Be(2);
    }

    /// <summary>
    /// Test double that satisfies <see cref="ILogSinkProvider"/> without
    /// touching disk or network. CreateSink is unused — the trust tests
    /// only assert registration / resolution shape, not sink behaviour.
    /// </summary>
    private sealed class TestSinkProvider : ILogSinkProvider
    {
        public TestSinkProvider(string kind) => SinkKind = kind;

        public string SinkKind { get; }

        public ILogger CreateSink(
            LoggingRuntimeSinkDefinition definition,
            ILogLevelRegistry levelRegistry,
            ILogOutputTransformerRegistry transformerRegistry) =>
            throw new System.NotSupportedException(
                "TestSinkProvider is not expected to construct a sink in trust tests.");
    }
}

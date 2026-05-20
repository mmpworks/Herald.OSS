#nullable enable

using FluentAssertions;
using MMP.Herald;
using MMP.Herald.Configuration;
using MMP.Herald.Pipeline;
using Xunit;

namespace MMP.Herald.OSS.Tests.PipelineStrategyTests;

/// <summary>
/// Pins the cap-surface additions on <see cref="KnownSink.Register"/>:
/// new <c>requiredCapabilities</c> parameter and persistence across
/// re-registration when not overridden. KnownSink does not enforce cap
/// monotonicity (sinks are leaf nodes — the host walks the cap set when
/// assembling the pipeline, not at the registry-update site).
/// </summary>
public sealed class KnownSinkCapabilityTests
{
    [Fact]
    public void Built_in_sinks_have_empty_RequiredCapabilities()
    {
        // Every built-in sink stays Community-tier and cap-free. A future
        // sink-side cap declaration would have to be additive at the
        // shipping-default level.
        KnownSink.Console.RequiredCapabilities.Should().BeEmpty();
        KnownSink.JsonFile.RequiredCapabilities.Should().BeEmpty();
        KnownSink.Elasticsearch.RequiredCapabilities.Should().BeEmpty();
    }

    [Fact]
    public void Register_with_caps_records_the_declared_set()
    {
        var sink = KnownSink.Register("known-sink-cap-test",
            displayName: "Cap Test Sink",
            vendor: VendorInfo.ThirdParty,
            requiredCapabilities: ["multi-tenant"]);

        sink.RequiredCapabilities.Should().HaveCount(1);
        sink.RequiredCapabilities[0].Name.Should().Be("multi-tenant");
    }

    [Fact]
    public void Re_register_preserves_prior_caps_when_no_caps_argument_passed()
    {
        KnownSink.Register("known-sink-preserve-caps",
            vendor: VendorInfo.ThirdParty,
            requiredCapabilities: ["multi-tenant"]);

        // Re-register with display update; caps must persist.
        var sink = KnownSink.Register("known-sink-preserve-caps",
            displayName: "Updated");

        sink.RequiredCapabilities.Should().HaveCount(1);
        sink.RequiredCapabilities[0].Name.Should().Be("multi-tenant");
    }
}

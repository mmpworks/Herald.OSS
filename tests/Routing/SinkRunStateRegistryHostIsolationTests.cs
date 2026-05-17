#nullable enable

using FluentAssertions;
using MMP.Herald.Configuration.Runtime;
using MMP.Herald.Quick;
using MMP.Herald.Routing.Loopback;
using Xunit;

namespace MMP.Herald.OSS.Tests.Routing;

/// <summary>
/// Pins the principal-review #10 host-isolation fix:
/// <see cref="SinkRunStateRegistryInstance"/> lives on
/// <see cref="HeraldHost"/>, so two hosts on the same process keep
/// independent <c>(pipelineName, sinkName) → holder</c> maps. The static
/// <see cref="SinkRunStateRegistry"/> facade keeps forwarding to
/// <see cref="HeraldHost.Default"/>, so existing single-host callers
/// compile unchanged.
/// </summary>
public sealed class SinkRunStateRegistryHostIsolationTests
{
    [Fact]
    public void Two_hosts_keep_independent_holder_maps()
    {
        var hostA = new HeraldHost();
        var hostB = new HeraldHost();
        var sharedPipeline = "shared-name";
        var sharedSink = "shared-sink";

        var holderA = new SinkRunStateHolder(SinkRunState.Live);
        var holderB = new SinkRunStateHolder(SinkRunState.Disabled);

        hostA.SinkRunState.Register(sharedPipeline, sharedSink, holderA);
        hostB.SinkRunState.Register(sharedPipeline, sharedSink, holderB);

        // Each host returns its own holder — the prior process-wide static
        // dictionary would have last-write-wins'd these into one slot.
        hostA.SinkRunState.Get(sharedPipeline, sharedSink).Should().BeSameAs(holderA);
        hostB.SinkRunState.Get(sharedPipeline, sharedSink).Should().BeSameAs(holderB);
    }

    [Fact]
    public void Static_facade_forwards_to_default_host()
    {
        // Static Register -> HeraldHost.Default.SinkRunState. A dedicated
        // host's map stays empty; the default host's map carries the entry.
        var dedicatedHost = new HeraldHost();
        var pipeline = "facade-forward-pipeline";
        var sink = "facade-forward-sink";
        var holder = new SinkRunStateHolder(SinkRunState.Live);

        try
        {
            SinkRunStateRegistry.Register(pipeline, sink, holder);

            SinkRunStateRegistry.Get(pipeline, sink).Should().BeSameAs(holder);
            HeraldHost.Default.SinkRunState.Get(pipeline, sink).Should().BeSameAs(holder);
            dedicatedHost.SinkRunState.Get(pipeline, sink).Should().BeNull(
                "the static facade must NOT touch a different host's map");
        }
        finally
        {
            SinkRunStateRegistry.ClearPipeline(pipeline);
        }
    }

    [Fact]
    public void Instance_ClearPipeline_removes_only_its_own_holders()
    {
        var hostA = new HeraldHost();
        var hostB = new HeraldHost();
        var pipeline = "clear-isolation-pipeline";
        var sink = "clear-isolation-sink";

        hostA.SinkRunState.Register(pipeline, sink, new SinkRunStateHolder(SinkRunState.Live));
        hostB.SinkRunState.Register(pipeline, sink, new SinkRunStateHolder(SinkRunState.Live));

        hostA.SinkRunState.ClearPipeline(pipeline);

        hostA.SinkRunState.Get(pipeline, sink).Should().BeNull();
        hostB.SinkRunState.Get(pipeline, sink).Should().NotBeNull(
            "ClearPipeline must affect only the host it was called on");
    }
}

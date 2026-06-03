#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Linq;
using FluentAssertions;
using MMP.Herald.Diagnostics;
using MMP.Herald.Quick;
using Xunit;

namespace MMP.Herald.OSS.Tests.Diagnostics;

/// <summary>
/// The aggregation-observability proof
/// (docs/design/announcement-channel-per-host-routing.md). Per-host buffers are
/// isolated, but the default facade OnNotice must still observe notices published
/// on every non-default host so an operator keeps one observation surface and the
/// un-silenceable injection notice stays un-silenceable.
///
/// <para>
/// This class subscribes to and reads the process-wide default facade, so it joins
/// <see cref="DefaultHostCollection"/> to serialise against every other class that
/// touches <c>HeraldHost.Default</c>. The isolation stress test
/// (<see cref="RuntimeNoticePerHostIsolationStressTests"/>) reads only per-host
/// buffers and runs fully parallel; this one cannot, because the facade event is
/// shared process-wide state.
/// </para>
/// </summary>
[Collection(MMP.Herald.OSS.Tests.Helpers.DefaultHostCollection.Name)]
public sealed class RuntimeNoticeAggregationObservabilityTests
{
    [Fact]
    public void Default_facade_OnNotice_observes_a_non_default_host_publish()
    {
        var observed = new List<RuntimeNotice>();
        Action<RuntimeNotice> handler = n => observed.Add(n);

        HeraldRuntimeMessages.OnNotice += handler;
        HeraldRuntimeMessages.ClearRecent();
        try
        {
            var host = new HeraldHost();
            host.RuntimeMessages.Publish("from.non.default", "aggregated up to the facade");

            // The facade EVENT saw it (aggregation hub re-raised it).
            observed.Should().ContainSingle(
                "the aggregation hub re-raises a non-default host notice on the default facade OnNotice")
                .Which.Source.Should().Be("from.non.default");

            // The facade BUFFER did NOT capture it (re-raise does not enqueue into
            // Default) — this is what preserves per-host buffer isolation.
            HeraldRuntimeMessages.RecentNotices.Should().BeEmpty(
                "aggregation re-raises the event only; it must not write the default host buffer");

            // The originating host buffer holds its own notice.
            host.RuntimeMessages.RecentNotices.Should().ContainSingle()
                .Which.Source.Should().Be("from.non.default");
        }
        finally
        {
            HeraldRuntimeMessages.OnNotice -= handler;
        }
    }

    [Fact]
    public void Default_host_does_not_forward_to_itself()
    {
        // The default host is the aggregation SINK, never a source. A publish on
        // the default facade must reach a facade subscriber exactly once (the
        // direct publish), never a second time via a self-forward loop.
        var hits = 0;
        Action<RuntimeNotice> handler = _ => Interlocked.Increment(ref hits);

        HeraldRuntimeMessages.OnNotice += handler;
        HeraldRuntimeMessages.ClearRecent();
        try
        {
            HeraldRuntimeMessages.Publish("default.publish", "once only");
            hits.Should().Be(1,
                "the default host does not attach to the aggregator, so there is no self-forward double-delivery");
        }
        finally
        {
            HeraldRuntimeMessages.OnNotice -= handler;
        }
    }

    [Fact]
    public void Bare_instance_does_not_aggregate_to_the_facade()
    {
        // Only hosts attach to the aggregator. A bare HeraldRuntimeMessagesInstance
        // (the per-test fixture pattern) stays fully isolated — its publishes must
        // NOT reach the default facade. This keeps the de-corralled per-test
        // channels from bleeding into facade subscribers.
        var observed = new List<RuntimeNotice>();
        Action<RuntimeNotice> handler = n => observed.Add(n);

        HeraldRuntimeMessages.OnNotice += handler;
        HeraldRuntimeMessages.ClearRecent();
        try
        {
            var bare = new HeraldRuntimeMessagesInstance();
            bare.Publish("bare.instance", "must not aggregate");

            observed.Should().BeEmpty(
                "a bare instance is not a host and does not attach to the aggregation hub");
            bare.RecentNotices.Should().ContainSingle();
        }
        finally
        {
            HeraldRuntimeMessages.OnNotice -= handler;
        }
    }
}

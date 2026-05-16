#nullable enable

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using MMP.Herald.Diagnostics;
using Xunit;

namespace MMP.Herald.OSS.Tests.Diagnostics;

/// <summary>
/// Contract tests for the runtime-notice channel. Tests use a
/// freshly-constructed <see cref="HeraldRuntimeMessagesInstance"/>
/// per case so they isolate cleanly without touching the static
/// facade's process-wide buffer.
/// </summary>
public sealed class HeraldRuntimeMessagesTests
{
    [Fact]
    public void Publish_with_throwing_subscriber_does_not_propagate_to_caller()
    {
        var channel = new HeraldRuntimeMessagesInstance();
        channel.OnNotice += _ => throw new InvalidOperationException("subscriber bug");

        // The contract: a throwing subscriber is the subscriber's
        // bug, not the framework's burden. Publish must NEVER
        // propagate the exception back to framework code — that
        // would let a buggy debug-console handler take down the
        // user's hot path.
        var act = () => channel.Publish(
            source: "test.thrower",
            message: "should be swallowed");

        act.Should().NotThrow();
    }

    [Fact]
    public void Publish_with_throwing_subscriber_still_delivers_to_other_subscribers()
    {
        var channel = new HeraldRuntimeMessagesInstance();
        var observed = new List<string>();

        channel.OnNotice += _ => throw new InvalidOperationException("first handler bombs");
        channel.OnNotice += n => observed.Add("second");
        channel.OnNotice += _ => throw new InvalidOperationException("third handler bombs");
        channel.OnNotice += n => observed.Add("fourth");

        channel.Publish("test.fan-out", "msg");

        // Subscribers two and four still see the notice even though
        // one and three threw. A naive Invoke() on the multicast
        // delegate would terminate the chain at the first throw —
        // GetInvocationList() + per-handler try/catch is what makes
        // this work.
        observed.Should().Equal(new[] { "second", "fourth" });
    }

    [Fact]
    public void Publish_with_throwing_subscriber_still_records_to_buffer()
    {
        var channel = new HeraldRuntimeMessagesInstance();
        channel.OnNotice += _ => throw new InvalidOperationException();

        channel.Publish("test.buffer-survives-throw", "msg");

        channel.RecentNotices.Should().ContainSingle()
            .Which.Source.Should().Be("test.buffer-survives-throw");
    }

    [Fact]
    public void Recent_notices_evicts_oldest_when_buffer_at_capacity()
    {
        var channel = new HeraldRuntimeMessagesInstance(capacity: 4);
        for (var i = 0; i < 10; i++)
        {
            channel.Publish($"src-{i}", $"msg-{i}");
        }

        var snapshot = channel.RecentNotices;
        snapshot.Should().HaveCount(4);
        // Oldest-first ordering, holding the last 4 of 10.
        snapshot[0].Source.Should().Be("src-6");
        snapshot[3].Source.Should().Be("src-9");
        channel.DroppedNoticeCount.Should().Be(6, "10 published minus 4 retained = 6 dropped");
    }

    [Fact]
    public void Recent_notices_returns_oldest_first_under_normal_load()
    {
        var channel = new HeraldRuntimeMessagesInstance();
        channel.Publish("a", "first");
        channel.Publish("b", "second");
        channel.Publish("c", "third");

        var snapshot = channel.RecentNotices;
        snapshot.Should().HaveCount(3);
        snapshot[0].Source.Should().Be("a");
        snapshot[2].Source.Should().Be("c");
    }

    [Fact]
    public void Clear_recent_empties_the_buffer_and_resets_dropped_count()
    {
        var channel = new HeraldRuntimeMessagesInstance(capacity: 2);
        for (var i = 0; i < 5; i++) channel.Publish($"src-{i}", "msg");

        channel.RecentNotices.Should().HaveCount(2);
        channel.DroppedNoticeCount.Should().Be(3);

        channel.ClearRecent();

        channel.RecentNotices.Should().BeEmpty();
        channel.DroppedNoticeCount.Should().Be(0);
    }

    [Fact]
    public void OnNotice_unsubscribe_stops_delivery_to_that_handler()
    {
        var channel = new HeraldRuntimeMessagesInstance();
        var observed = new List<string>();
        Action<RuntimeNotice> handler = n => observed.Add(n.Source);

        channel.OnNotice += handler;
        channel.Publish("first", "msg");

        channel.OnNotice -= handler;
        channel.Publish("second", "msg");

        observed.Should().Equal(new[] { "first" });
    }

    [Fact]
    public void Concurrent_publish_from_many_threads_produces_no_torn_records()
    {
        const int writerCount = 8;
        const int publishesPerWriter = 250;
        var channel = new HeraldRuntimeMessagesInstance(capacity: 64);

        // No subscribers; just measure that concurrent publishes
        // don't blow up and the buffer ends in a sane state.
        Parallel.For(0, writerCount, writer =>
        {
            for (var i = 0; i < publishesPerWriter; i++)
            {
                channel.Publish($"writer-{writer}", $"msg-{i}");
            }
        });

        var totalPublished = writerCount * publishesPerWriter;
        var snapshot = channel.RecentNotices;
        snapshot.Should().HaveCount(64);
        channel.DroppedNoticeCount.Should().Be(totalPublished - 64,
            "every publish past the 64-slot capacity must increment the dropped count");
        snapshot.Should().AllSatisfy(n =>
        {
            n.Source.Should().StartWith("writer-");
            n.GenSource.Should().Be(HeraldGenSource.RuntimeNotice);
        });
    }

    [Fact]
    public void Per_host_instances_have_independent_channels()
    {
        // The whole point of the host-instance refactor: two hosts
        // do not see each other's notices. A multi-tenant deployment
        // that constructs one host per tenant gets channel
        // isolation for free.
        var hostA = new HeraldRuntimeMessagesInstance();
        var hostB = new HeraldRuntimeMessagesInstance();

        var seenByA = new List<string>();
        var seenByB = new List<string>();
        hostA.OnNotice += n => seenByA.Add(n.Source);
        hostB.OnNotice += n => seenByB.Add(n.Source);

        hostA.Publish("from-A", "msg");
        hostB.Publish("from-B", "msg");

        seenByA.Should().Equal(new[] { "from-A" });
        seenByB.Should().Equal(new[] { "from-B" });
        hostA.RecentNotices.Should().ContainSingle()
            .Which.Source.Should().Be("from-A");
        hostB.RecentNotices.Should().ContainSingle()
            .Which.Source.Should().Be("from-B");
    }

    [Fact]
    public void Static_facade_forwards_to_default_host()
    {
        // HeraldRuntimeMessages is a static facade that should be
        // bit-identical to HeraldHost.Default.RuntimeMessages. This
        // test pins that contract so a future refactor doesn't
        // accidentally split the two surfaces.
        HeraldRuntimeMessages.Default.Should().BeSameAs(
            MMP.Herald.Quick.HeraldHost.Default.RuntimeMessages);
    }
}

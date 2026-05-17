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

    // ---------- 0.2.3 — severity + dropped event + fallback ----------

    [Fact]
    public void Publish_without_severity_defaults_to_Info()
    {
        var channel = new HeraldRuntimeMessagesInstance();
        channel.Publish("test.no-severity", "msg");

        channel.RecentNotices.Should().ContainSingle()
            .Which.Severity.Should().Be(NoticeSeverity.Info,
                "the severity-omitting overload must default to Info — the routine framework signal class");
    }

    [Fact]
    public void Publish_with_explicit_severity_carries_through_to_subscribers()
    {
        var channel = new HeraldRuntimeMessagesInstance();
        var observed = new List<NoticeSeverity>();
        channel.OnNotice += n => observed.Add(n.Severity);

        channel.Publish("test.info",    "info-msg",    NoticeSeverity.Info);
        channel.Publish("test.warning", "warning-msg", NoticeSeverity.Warning);
        channel.Publish("test.error",   "error-msg",   NoticeSeverity.Error);

        observed.Should().Equal(new[] {
            NoticeSeverity.Info,
            NoticeSeverity.Warning,
            NoticeSeverity.Error,
        });
    }

    [Fact]
    public void Publish_with_null_severity_throws()
    {
        var channel = new HeraldRuntimeMessagesInstance();
        Action act = () => channel.Publish("test.null", "msg", severity: null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Publish_with_null_properties_yields_empty_property_list()
    {
        var channel = new HeraldRuntimeMessagesInstance();
        channel.Publish("test.null-props", "msg", properties: null);

        // The subscriber and the buffer must both observe an
        // empty-but-non-null Properties list — iterating without
        // null-checking is the contract.
        var notice = channel.RecentNotices.Should().ContainSingle().Subject;
        notice.Properties.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void OnNoticeDropped_fires_with_evicted_notice_when_buffer_full()
    {
        var channel = new HeraldRuntimeMessagesInstance(capacity: 2);
        var dropped = new List<string>();
        channel.OnNoticeDropped += n => dropped.Add(n.Source);

        channel.Publish("first",  "msg");
        channel.Publish("second", "msg");
        // Capacity is 2; the third publish evicts "first".
        channel.Publish("third",  "msg");

        dropped.Should().Equal(new[] { "first" });
        channel.RecentNotices.Should().HaveCount(2);
        channel.DroppedNoticeCount.Should().Be(1);
    }

    [Fact]
    public void OnNoticeDropped_does_not_fire_when_buffer_has_room()
    {
        var channel = new HeraldRuntimeMessagesInstance(capacity: 4);
        var dropped = new List<RuntimeNotice>();
        channel.OnNoticeDropped += dropped.Add;

        channel.Publish("a", "msg");
        channel.Publish("b", "msg");

        dropped.Should().BeEmpty();
    }

    [Fact]
    public void Throwing_OnNoticeDropped_subscriber_does_not_propagate()
    {
        var channel = new HeraldRuntimeMessagesInstance(capacity: 1);
        channel.OnNoticeDropped += _ => throw new InvalidOperationException("subscriber bug");

        // Trigger an eviction. The throwing subscriber must not
        // propagate — same contract as OnNotice subscribers.
        channel.Publish("first", "msg");
        var act = () => channel.Publish("second", "msg");

        act.Should().NotThrow();
    }

    [Fact]
    public void FallbackSubscriber_fires_only_when_no_OnNotice_subscribers()
    {
        var channel = new HeraldRuntimeMessagesInstance();
        var fallbackHits = new List<string>();
        var liveHits = new List<string>();

        channel.FallbackSubscriber = n => fallbackHits.Add(n.Source);

        // No OnNotice subscribers — fallback runs.
        channel.Publish("a", "msg");
        fallbackHits.Should().ContainSingle();
        liveHits.Should().BeEmpty();

        // Live subscriber wired — fallback does NOT fire alongside.
        Action<RuntimeNotice> live = n => liveHits.Add(n.Source);
        channel.OnNotice += live;
        try
        {
            channel.Publish("b", "msg");
            fallbackHits.Should().ContainSingle("fallback fires when nobody else listens, not when both are wired");
            liveHits.Should().ContainSingle();
        }
        finally
        {
            channel.OnNotice -= live;
        }

        // Live subscriber removed — fallback fires again.
        channel.Publish("c", "msg");
        fallbackHits.Should().HaveCount(2);
        liveHits.Should().ContainSingle();
    }

    [Fact]
    public void Throwing_FallbackSubscriber_does_not_propagate()
    {
        var channel = new HeraldRuntimeMessagesInstance();
        channel.FallbackSubscriber = _ => throw new InvalidOperationException("fallback bug");

        var act = () => channel.Publish("test.thrower", "msg");
        act.Should().NotThrow();
    }

    [Fact]
    public void Static_facade_does_not_observe_non_default_host_publishes()
    {
        // G1.1 — the per-host isolation invariant must hold ON THE STATIC
        // FACADE, not just between two new HeraldRuntimeMessagesInstance
        // objects. Subscribe on the facade, publish to a different host,
        // assert the facade subscriber saw nothing.
        var facadeHits = new List<string>();
        Action<RuntimeNotice> handler = n => facadeHits.Add(n.Source);
        HeraldRuntimeMessages.OnNotice += handler;
        HeraldRuntimeMessages.ClearRecent();
        try
        {
            var otherHost = new MMP.Herald.Quick.HeraldHost();
            otherHost.RuntimeMessages.Publish("from-other-host", "msg");
            otherHost.RuntimeMessages.Publish("from-other-host", "msg-2");

            facadeHits.Should().BeEmpty(
                "publishes on a non-default host MUST NOT reach the default-host facade's subscribers");
            HeraldRuntimeMessages.RecentNotices.Should().BeEmpty(
                "the default host's recent-notices buffer MUST NOT capture another host's publishes");
            otherHost.RuntimeMessages.RecentNotices.Should().HaveCount(2);
        }
        finally
        {
            HeraldRuntimeMessages.OnNotice -= handler;
        }
    }

    // ── Tenant attribution (principal-review queue #7) ───────────────

    [Fact]
    public void Tenant_round_trips_through_publish_and_recent_notices()
    {
        // Tenant attribution lets a multi-tenant host route notices to
        // per-tenant subscribers. The field must round-trip from the
        // tenant-aware Publish overload all the way through to the
        // RuntimeNotice that subscribers and the recent-notices buffer
        // observe.
        var channel = new HeraldRuntimeMessagesInstance();
        RuntimeNotice? observed = null;
        channel.OnNotice += n => observed = n;

        channel.Publish(
            source: "tenant.scoped",
            message: "scoped to tenant-A",
            severity: NoticeSeverity.Warning,
            tenant: "tenant-A");

        observed.Should().NotBeNull();
        observed!.Tenant.Should().Be("tenant-A");
        channel.RecentNotices.Should().ContainSingle()
            .Which.Tenant.Should().Be("tenant-A");
    }

    [Fact]
    public void Tenant_defaults_to_null_for_legacy_publish_overloads()
    {
        // Pre-tenant Publish overloads — and any caller that doesn't
        // pass a tenant — must surface null on the notice. Default-null
        // is the additive contract that makes Tenant safe to ship into
        // a sealed record without breaking existing pattern-matchers.
        var channel = new HeraldRuntimeMessagesInstance();
        RuntimeNotice? observed = null;
        channel.OnNotice += n => observed = n;

        channel.Publish("legacy.caller", "no tenant supplied");

        observed.Should().NotBeNull();
        observed!.Tenant.Should().BeNull();
    }

    [Fact]
    public void Tenant_null_explicit_is_equivalent_to_omitting_the_argument()
    {
        // The tenant-aware overload with tenant: null must behave
        // exactly like the legacy overload. Operators migrating between
        // the two surfaces should see no observable difference when
        // the tenant value is unknown.
        var channel = new HeraldRuntimeMessagesInstance();
        RuntimeNotice? observed = null;
        channel.OnNotice += n => observed = n;

        channel.Publish(
            source: "explicit.null",
            message: "null tenant",
            severity: NoticeSeverity.Info,
            tenant: null);

        observed.Should().NotBeNull();
        observed!.Tenant.Should().BeNull();
    }
}

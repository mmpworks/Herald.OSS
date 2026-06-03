#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MMP.Herald.Diagnostics;
using MMP.Herald.Events;
using MMP.Herald.Levels;
using MMP.Herald.OSS.Tests.Helpers;
using MMP.Herald.Quick;
using Xunit;

namespace MMP.Herald.OSS.Tests.Diagnostics;

/// <summary>
/// The proof for per-host runtime-notice isolation
/// (docs/design/announcement-channel-per-host-routing.md). This is the test that
/// would have caught the original announcement-buffer bleed: N hosts built and
/// exercised in parallel, each asserting its OWN host buffer holds ONLY its own
/// notices and nothing from a sibling.
///
/// <para>
/// Parallelism is ON by design — there is NO DisableParallelization collection on
/// this class. If per-host buffer isolation holds, this is green every run with no
/// serialisation. A regression that re-routes a logger notice to the wrong host
/// buffer (a missed ForContext propagation, a fallback to the default channel)
/// turns the cross-host assertions red. The class reads only per-host BUFFERS, not
/// the default facade event, so the aggregation hub cannot pollute it.
/// </para>
/// </summary>
public sealed class RuntimeNoticePerHostIsolationStressTests
{
    private const string NamingNoticeSource = "@herald.runtime.naming-policy";
    private const string InjectionNoticeSource = "@herald.runtime.external-event-injection";

    [Fact]
    public void N_hosts_in_parallel_each_buffer_holds_only_its_own_notices()
    {
        const int hostCount = 32;

        // Each host gets a unique policy id token via a distinct minimum level is
        // not enough; instead we tag each host through its own bridge + assert the
        // announcement PolicyId and the injection callSite land only on that host.
        var hosts = new HeraldHost[hostCount];
        for (var i = 0; i < hostCount; i++)
        {
            hosts[i] = new HeraldHost();
        }

        // Build + exercise every host in parallel. Each host:
        //   1. builds a logger routed to its own RuntimeMessages channel,
        //   2. fires its naming announcement (first dispatch),
        //   3. fires an injection-refusal notice (Log a hand-built event, consent off).
        Parallel.For(0, hostCount, i =>
        {
            var host = hosts[i];
            var sink = new CapturingSink();
            var result = QuickLogBuilder.Create()
                .WithRuntimeMessageChannel(host.RuntimeMessages)
                .WithBridge(sink)
                .WithMinimumLevel("verbose")
                .BuildAndCommit();

            // First dispatch arms + queues the deferred naming announcement.
            result.Logger.Information("host {Index} online", i);

            // Off-path injection: consent is OFF (default), so this drops the event
            // and fires the one-shot un-silenceable refusal notice on THIS host.
            result.Logger.Log(BuildHandCraftedEvent("injected on host " + i));
        });

        // For every host, wait for its deferred announcement, then assert its
        // buffer holds exactly its own naming announcement + its own injection
        // refusal, and NOTHING attributable to a sibling host.
        for (var i = 0; i < hostCount; i++)
        {
            var channel = hosts[i].RuntimeMessages;

            AnnouncementSpinHelpers.WaitForAnnouncement(channel);

            var notices = channel.RecentNotices;

            var naming = notices
                .Where(n => string.Equals(n.Source, NamingNoticeSource, StringComparison.Ordinal))
                .ToList();
            naming.Should().ContainSingle(
                "each host fires exactly one naming announcement on its own channel");

            var injection = notices
                .Where(n => string.Equals(n.Source, InjectionNoticeSource, StringComparison.Ordinal))
                .ToList();
            injection.Should().ContainSingle(
                "each host fires exactly one injection refusal on its own channel");

            // Cross-host leak check: the injection notice names the call site that
            // built the hand-crafted event for THIS host. A sibling host id in this
            // buffer would mean a publish bled across host channels.
            var injectionMessage = injection[0].Message;
            for (var j = 0; j < hostCount; j++)
            {
                if (j == i) continue;
                injectionMessage.Should().NotContain("injected on host " + j,
                    "host " + i + " buffer must not carry host " + j + " notices");
            }

            // And only those two framework notices, nothing else.
            notices.Should().HaveCount(2,
                "a host buffer holds exactly its own naming + injection notices");
        }
    }

    [Fact]
    public void Many_short_lived_hosts_keep_their_buffers_disjoint()
    {
        // A tighter, faster variant focused purely on disjointness: build M hosts,
        // publish a host-unique marker straight onto each host channel in parallel,
        // then assert every host buffer contains ONLY its own marker.
        const int hostCount = 64;
        var hosts = Enumerable.Range(0, hostCount).Select(_ => new HeraldHost()).ToArray();

        Parallel.For(0, hostCount, i =>
            hosts[i].RuntimeMessages.Publish("marker", "host-" + i));

        for (var i = 0; i < hostCount; i++)
        {
            var notices = hosts[i].RuntimeMessages.RecentNotices;
            notices.Should().ContainSingle();
            notices[0].Message.Should().Be("host-" + i,
                "each host channel buffer holds only its own publish, never a sibling host marker");
        }
    }

    private static LogEvent BuildHandCraftedEvent(string message) =>
        new(
            TimeUtc: DateTimeOffset.UtcNow,
            Level: KnownLogLevels.Information,
            Category: LogCategory.App,
            MessageTemplate: message,
            Message: message,
            Properties: LogEvent.EmptyProperties,
            Context: LogEvent.EmptyContext);

    private sealed class CapturingSink : MMP.Herald.ILogger
    {
        private readonly List<LogEvent> _events = new();
        private readonly object _sync = new();

        public IReadOnlyList<LogEvent> Events
        {
            get { lock (_sync) { return _events.ToArray(); } }
        }

        public void Log(LogEvent logEvent)
        {
            lock (_sync) { _events.Add(logEvent); }
        }

        public ValueTask LogAsync(LogEvent logEvent, CancellationToken cancellationToken = default)
        {
            Log(logEvent);
            return ValueTask.CompletedTask;
        }
    }
}

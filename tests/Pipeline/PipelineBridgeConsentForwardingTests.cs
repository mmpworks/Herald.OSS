#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MMP.Herald.Diagnostics;
using MMP.Herald.Events;
using MMP.Herald.Levels;
using MMP.Herald.OSS.Tests.Helpers;
using MMP.Herald.Pipeline;
using MMP.Herald.Quick;
using Xunit;

namespace MMP.Herald.OSS.Tests.Pipeline;

// Regression: PipelineBridge consent-gate forwarding.
//
// A PipelineBridge whose target is another Herald pipeline (the documented
// hub-and-spoke case: Bridges.Add(result.Logger), where result.Logger is a
// StructuredLogger) must FORWARD the bridged event into the target even when the
// target pipeline has external-event-injection consent OFF — because a
// Herald-to-Herald forward is NOT external injection. Before the fix the bridge
// called _target.Log(logEvent), which on a StructuredLogger binds to the explicit
// ILogger.Log member (the public consent boundary); with consent OFF the hub
// silently dropped the event and fired a spurious injection notice.
//
// The fix routes Herald-to-Herald forwarding through the internal
// IInternalLogForwarder.ForwardPrebuilt path, which bypasses the gate. A
// non-Herald ILogger target does not implement the interface and keeps normal
// Log(LogEvent) dispatch.
//
// These tests observe the process-wide HeraldRuntimeMessages buffer, so they join
// DefaultHostCollection (serialised) and filter the shared buffer for the
// injection notice source.
[Collection(DefaultHostCollection.Name)]
public sealed class PipelineBridgeConsentForwardingTests : IDisposable
{
    private const string InjectionNoticeSource = "@herald.runtime.external-event-injection";

    public PipelineBridgeConsentForwardingTests()
    {
        HeraldRuntimeMessages.ClearRecent();
    }

    // Each test here builds real QuickLogBuilder pipelines whose first dispatch
    // queues a deferred naming-policy announcement onto the thread pool. Those land
    // asynchronously and would otherwise straggle into a sibling test that asserts a
    // count on the shared DefaultHostCollection buffer. Settle the deferred publishes,
    // then clear, so this class leaves the shared buffer clean.
    public void Dispose()
    {
        AnnouncementSpinHelpers.SpinUntil(
            () => HeraldRuntimeMessages.RecentNotices.Any(n =>
                n.Message.StartsWith("Herald active naming policy:", StringComparison.Ordinal)),
            timeoutMs: 200);
        HeraldRuntimeMessages.ClearRecent();
    }

    [Fact]
    public void Herald_to_Herald_bridge_forwards_into_hub_with_consent_off_and_fires_no_injection_notice()
    {
        // Hub pipeline (the spoke target). Consent is OFF — the default. Its sink
        // captures whatever reaches it.
        var hubSink = new CapturingSink();
        var hub = QuickLogBuilder.Create()
            .WithBridge(hubSink)
            .WithMinimumLevel("verbose")
            .BuildAndCommit();

        // Spoke pipeline: bridges into the hub via the hub's StructuredLogger,
        // exactly as Bridges.Add(result.Logger) does.
        var spoke = QuickLogBuilder.Create()
            .WithBridge(hub.Logger)
            .WithMinimumLevel("verbose")
            .BuildAndCommit();

        // Emit through the spoke's typed surface — a normal internal event.
        spoke.Logger.Information("cross-pipeline {Hop}", "spoke->hub");

        hubSink.Events.Should().ContainSingle(
            "a Herald-to-Herald bridge must forward the event into the hub even with the hub's consent off");
        hubSink.Events[0].Message.Should().Contain("spoke->hub");

        InjectionNotices().Should().BeEmpty(
            "Herald-to-Herald forwarding is not external injection, so no refusal notice fires");
    }

    [Fact]
    public void Direct_bridge_over_StructuredLogger_with_consent_off_forwards_and_fires_no_notice()
    {
        // Same trap at the PipelineBridge unit level: construct a bridge straight
        // over a consent-off pipeline's StructuredLogger and push a pre-built event.
        var hubSink = new CapturingSink();
        var hub = QuickLogBuilder.Create()
            .WithBridge(hubSink)
            .WithMinimumLevel("verbose")
            .BuildAndCommit();

        var bridge = new PipelineBridge(hub.Logger);
        bridge.Log(BuildHandCraftedEvent("direct bridge over StructuredLogger"));

        hubSink.Events.Should().ContainSingle(
            "the bridge detects the StructuredLogger target and forwards via the internal path");
        InjectionNotices().Should().BeEmpty();
    }

    [Fact]
    public void Bridge_over_non_Herald_ILogger_uses_normal_dispatch()
    {
        // A genuinely external, non-Herald ILogger target does not implement
        // IInternalLogForwarder, so the bridge falls back to normal Log(LogEvent)
        // dispatch and the target's own contract applies.
        var external = new CapturingSink();
        var bridge = new PipelineBridge(external);

        var injected = BuildHandCraftedEvent("non-Herald target");
        bridge.Log(injected);

        var seen = external.Events.Should().ContainSingle(
            "a non-Herald ILogger target receives the event through normal dispatch").Subject;
        seen.Message.Should().Be(injected.Message);

        // No StructuredLogger consent gate is in play on this path, so there is
        // nothing to refuse.
        InjectionNotices().Should().BeEmpty();
    }

    [Fact]
    public void Filtered_Herald_to_Herald_bridge_still_forwards_matching_events_with_consent_off()
    {
        var hubSink = new CapturingSink();
        var hub = QuickLogBuilder.Create()
            .WithBridge(hubSink)
            .WithMinimumLevel("verbose")
            .BuildAndCommit();

        // Filter that only forwards events whose message mentions "keep".
        var bridge = new PipelineBridge(
            hub.Logger,
            e => e.Message.Contains("keep", StringComparison.Ordinal));

        bridge.Log(BuildHandCraftedEvent("drop this one"));
        bridge.Log(BuildHandCraftedEvent("keep this one"));

        hubSink.Events.Should().ContainSingle("only the matching event crosses the filter");
        hubSink.Events[0].Message.Should().Contain("keep");
        InjectionNotices().Should().BeEmpty("the forwarded event is Herald-to-Herald, not injection");
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

    private static IReadOnlyList<RuntimeNotice> InjectionNotices() =>
        HeraldRuntimeMessages.RecentNotices
            .Where(n => string.Equals(n.Source, InjectionNoticeSource, StringComparison.Ordinal))
            .ToList();

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

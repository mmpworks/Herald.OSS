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
using MMP.Herald.Quick;
using MMP.Herald.Templating;
using Xunit;

namespace MMP.Herald.OSS.Tests.Pipeline;

// External-event-injection consent switch (ADR:
// docs/design/external-event-injection-switch.md).
//
// The public Log(LogEvent) port is the only injection boundary and the only
// place the consent flag is read. With consent OFF an injected event is dropped
// and a one-shot, un-silenceable runtime notice fires; with consent ON it flows.
// The internal typed surface (Information/Warning/...) builds through the factory
// and never reaches the consent gate (section 7.1 entry-point guarantee).
//
// Per-host channel routing (docs/design/announcement-channel-per-host-routing.md):
// each test builds its logger on its OWN runtime-message channel and asserts on
// that channel's buffer, so a parallel sibling's notice can never inflate this
// class's count. That isolation retired the DefaultHostCollection membership and
// the shared-buffer ClearRecent — the class now runs fully parallel.
public sealed class ExternalEventInjectionTests
{
    private const string InjectionNoticeSource = "@herald.runtime.external-event-injection";

    // One channel per test instance. xUnit constructs a fresh test instance per
    // [Fact], so this is per-test isolation by construction.
    private readonly MMP.Herald.Diagnostics.HeraldRuntimeMessagesInstance _channel = new();

    // Builder factory that routes the build's framework notices to THIS test's
    // channel. Every test uses it instead of Create() so the
    // injection-refusal notice lands on _channel, not the process-wide buffer.
    private QuickLogBuilder Create() =>
        QuickLogBuilder.Create().WithRuntimeMessageChannel(_channel);

    [Fact]
    public void Off_injection_via_Log_LogEvent_is_dropped_and_fires_exactly_one_notice_naming_the_call_site()
    {
        var sink = new CapturingBridge();
        var result = Create()
            .WithBridge(sink)
            .WithMinimumLevel("verbose")
            .BuildAndCommit();

        var injected = BuildHandCraftedEvent("injected via Log(LogEvent), consent off");

        result.Logger.Log(injected);

        sink.Events.Should().BeEmpty("consent is off, so the injected event is dropped at the boundary");

        var notices = InjectionNotices();
        notices.Should().ContainSingle("the OFF-path notice fires once on the first off-path injection");

        var callSite = PropertyValue(notices[0], "callSite");
        callSite.Should().NotBeNull();
        callSite!.ToString().Should().Contain("ExternalEventInjectionTests.cs",
            "a direct Log(LogEvent) call captures the caller file via the caller-info overload");
    }

    [Fact]
    public void Off_path_notice_is_severity_Warning_and_points_to_the_opt_in()
    {
        var result = Create()
            .WithBridge(new CapturingBridge())
            .WithMinimumLevel("info")
            .BuildAndCommit();

        result.Logger.Log(BuildHandCraftedEvent("off-path"));

        var notice = InjectionNotices().Should().ContainSingle().Subject;
        notice.Severity.Should().Be(NoticeSeverity.Warning);
        notice.Message.Should().Contain("AllowExternalEventInjection()",
            "the notice must point the developer at the opt-in");
        PropertyValue(notice, "optIn").Should().Be("AllowExternalEventInjection()");
    }

    [Fact]
    public void Off_path_notice_names_the_bypassed_protections_including_redaction()
    {
        var result = Create()
            .WithBridge(new CapturingBridge())
            .WithMinimumLevel("info")
            .BuildAndCommit();

        result.Logger.Log(BuildHandCraftedEvent("off-path protections"));

        var notice = InjectionNotices().Should().ContainSingle().Subject;

        notice.Message.Should().Contain("redaction",
            "redaction is the protection whose bypass carries the security weight");

        var bypassed = PropertyValue(notice, "bypassedProtections")?.ToString();
        bypassed.Should().NotBeNull();
        bypassed!.Should().Contain("redaction");
        bypassed.Should().Contain("enrichment");
        bypassed.Should().Contain("templateRendering");
    }

    [Fact]
    public void Off_path_notice_is_one_shot_across_many_injections()
    {
        var sink = new CapturingBridge();
        var result = Create()
            .WithBridge(sink)
            .WithMinimumLevel("verbose")
            .BuildAndCommit();

        for (var i = 0; i < 100; i++)
        {
            result.Logger.Log(BuildHandCraftedEvent("loop " + i));
        }

        sink.Events.Should().BeEmpty();

        InjectionNotices().Should().ContainSingle(
            "the refusal notice fires on the first off-path injection only");
    }

    [Fact]
    public void On_injection_flows_through_and_fires_no_refusal_notice()
    {
        var sink = new CapturingBridge();
        var result = Create()
            .WithBridge(sink)
            .WithMinimumLevel("verbose")
            .AllowExternalEventInjection()
            .BuildAndCommit();

        var injected = BuildHandCraftedEvent("injected via Log(LogEvent), consent on");
        result.Logger.Log(injected);

        var seen = sink.Events.Should().ContainSingle().Subject;
        seen.MessageTemplate.Should().Be(injected.MessageTemplate);
        seen.Message.Should().Be(injected.Message);

        InjectionNotices().Should().BeEmpty("consent is on, so there is nothing to refuse");
    }

    [Fact]
    public void Internal_typed_path_with_consent_off_fires_no_injection_notice()
    {
        var sink = new CapturingBridge();
        var result = Create()
            .WithBridge(sink)
            .WithMinimumLevel("verbose")
            .BuildAndCommit();

        result.Logger.Information("user {UserId} signed in", "alice");
        result.Logger.Warning("disk {Percent} full", 91);
        result.Logger.Error("op {Op} failed", "sync");

        sink.Events.Should().HaveCount(3, "the typed surface is unaffected by the consent gate");

        InjectionNotices().Should().BeEmpty(
            "internal typed events build via the factory and never hit the consent port");
    }

    [Fact]
    public void Internal_typed_path_with_consent_on_also_fires_no_injection_notice()
    {
        var sink = new CapturingBridge();
        var result = Create()
            .WithBridge(sink)
            .WithMinimumLevel("verbose")
            .AllowExternalEventInjection()
            .BuildAndCommit();

        result.Logger.Information("hello {Who}", "world");

        sink.Events.Should().ContainSingle();
        InjectionNotices().Should().BeEmpty();
    }

    [Fact]
    public void ForContext_child_inherits_parent_consent_off_and_refuses_injection()
    {
        var sink = new CapturingBridge();
        var result = Create()
            .WithBridge(sink)
            .WithMinimumLevel("verbose")
            .BuildAndCommit();

        var child = result.Logger.ForContext(new Dictionary<string, object?> { ["scope"] = "child" });
        child.Log(BuildHandCraftedEvent("via child logger"));

        sink.Events.Should().BeEmpty("a child of an unconsented logger inherits the refusal");
        InjectionNotices().Should().ContainSingle();
    }

    [Fact]
    public void ForContext_child_inherits_parent_consent_on_and_allows_injection()
    {
        var sink = new CapturingBridge();
        var result = Create()
            .WithBridge(sink)
            .WithMinimumLevel("verbose")
            .AllowExternalEventInjection()
            .BuildAndCommit();

        var child = result.Logger.ForContext(new Dictionary<string, object?> { ["scope"] = "child" });
        child.Log(BuildHandCraftedEvent("via child logger, consent on"));

        sink.Events.Should().ContainSingle("a child of a consented logger inherits the consent");
        InjectionNotices().Should().BeEmpty();
    }

    [Fact]
    public void Consent_round_trips_through_exported_json()
    {
        var json = Create()
            .WithConsoleSink()
            .WithMinimumLevel("info")
            .AllowExternalEventInjection()
            .ExportConfigJson();

        json.Should().Contain("allowExternalEventInjection",
            "the consent flag must serialize so it survives a restore-from-JSON");

        var rebuilt = QuickLogBuilder.FromConfigurationString(json)
            .WithRuntimeMessageChannel(_channel)
            .BuildAndCommit();

        rebuilt.Logger.Log(BuildHandCraftedEvent("after json round-trip"));

        // Consent survived the round-trip: injection is allowed, so NO refusal
        // notice fires. (Absence-of-notice is the consent signal here; we avoid
        // re-routing a fresh bridge over a preloaded config, which is a separate
        // builder concern unrelated to this switch.)
        InjectionNotices().Should().BeEmpty("consent restored from JSON allows the injection without a refusal notice");
    }

    [Fact]
    public void Off_path_refusal_notice_lives_on_runtime_channel_and_is_absent_from_the_user_log_feed()
    {
        // Channel-separation guarantee (Richard's gap, pinned by Echo 2026-06-02):
        // a refused off-path injection produces a notice on the runtime-messages
        // channel ONLY. The user log feed (the bridge the consumer built) must see
        // NEITHER the injected event NOR the framework refusal notice. This is the
        // wall the injection gate is built to hold: framework signals never flow
        // through the consumer's pipeline.

        var userFeed = new CapturingBridge();
        var result = Create()
            .WithBridge(userFeed)
            .WithMinimumLevel("verbose")
            .BuildAndCommit();

        result.Logger.Log(BuildHandCraftedEvent("channel-separation probe"));

        // Runtime channel HAS the refusal notice (one, this class's source).
        var notice = InjectionNotices().Should().ContainSingle(
            "the refusal notice fires on the runtime-messages channel").Subject;
        notice.Source.Should().Be(InjectionNoticeSource);

        // The user log feed is EMPTY: not the injected event (dropped at the
        // consent gate), and crucially not the refusal notice (it must never
        // cross into the consumer's pipeline).
        userFeed.Events.Should().BeEmpty(
            "the user feed sees neither the dropped injection nor the framework " +
            "refusal notice — the refusal lives only on the runtime-messages channel");

        // Belt-and-braces: even by message-text match, no framework refusal
        // notice leaked into the user feed.
        userFeed.Events.Should().NotContain(
            e => e.Message.Contains("AllowExternalEventInjection()", StringComparison.Ordinal),
            "the opt-in refusal text must never appear in the user log feed");
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

    private IReadOnlyList<RuntimeNotice> InjectionNotices() =>
        _channel.RecentNotices
            .Where(n => string.Equals(n.Source, InjectionNoticeSource, StringComparison.Ordinal))
            .ToList();

    private static object? PropertyValue(RuntimeNotice notice, string name) =>
        notice.Properties.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.Ordinal)).Value;

    private sealed class CapturingBridge : MMP.Herald.ILogger
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

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
using MMP.Herald.Failures;
using MMP.Herald.OSS.Tests.Helpers;
using MMP.Herald.Quick;
using MMP.Herald.Templating;
using MMP.Herald.Templating.NamingPolicies;
using Xunit;

namespace MMP.Herald.OSS.Tests.Pipeline;

/// <summary>
/// Phase 5: first-dispatch naming-policy announcement + ReloadDegraded
/// failure-sink event. Announcement fires once per <c>StructuredLogger</c>
/// on the first dispatch through it and publishes to the runtime-message
/// channel (<see cref="HeraldRuntimeMessages"/>) — NOT through the user
/// pipeline. Suppression covers <c>SuppressNamingPolicyAnnouncement()</c>
/// on the builder.
/// </summary>
[Collection(nameof(NamingPolicyAnnouncementTests))]
[CollectionDefinition(nameof(NamingPolicyAnnouncementTests), DisableParallelization = true)]
public sealed class NamingPolicyAnnouncementTests
{
    public NamingPolicyAnnouncementTests()
    {
        NameResolverCache.Reset();
        // Clear the runtime-message buffer so each test observes only
        // notices it generated. The channel is process-wide static
        // state; the surrounding [Collection] disables parallelism for
        // this file, but the buffer can still carry residue from a
        // prior class's tests.
        HeraldRuntimeMessages.ClearRecent();
    }

    // -- Announcement firing ------------------------------------------------

    [Fact]
    public void First_dispatch_publishes_announcement_to_runtime_channel_not_user_pipeline()
    {
        var sink = new CapturingBridge();
        var result = QuickLogBuilder.Create()
            .WithBridge(sink)
            .WithMinimumLevel("info")
            .BuildAndCommit();

        var userId = "alice";
        result.Logger.Info("user {UserId} signed in", userId);

        // Wall holds: the bridge has only the user event. The
        // announcement landed on the runtime-message channel instead.
        sink.Events.Should().ContainSingle()
            .Which.MessageTemplate.Should().Be("user {UserId} signed in");

        // Announcement publish is deferred to the thread pool; wait for it
        // to land before asserting on RecentNotices.
        AnnouncementSpinHelpers.WaitForAnnouncement();

        var notices = HeraldRuntimeMessages.RecentNotices;
        notices.Should().ContainSingle();
        var announcement = notices[0];
        announcement.Message.Should().StartWith("Herald active naming policy:");
        announcement.Source.Should().Be("@herald.runtime.naming-policy");
        announcement.GenSource.Should().Be(HeraldGenSource.RuntimeNotice);
        announcement.Properties.Should().ContainSingle()
            .Which.Name.Should().Be("PolicyId");
        announcement.Properties[0].Value.Should().Be("pascal");
    }

    [Fact]
    public void Announcement_fires_at_most_once_per_logger_across_many_dispatches()
    {
        var sink = new CapturingBridge();
        var result = QuickLogBuilder.Create()
            .WithBridge(sink)
            .WithMinimumLevel("info")
            .BuildAndCommit();

        var v = 1;
        for (var i = 0; i < 100; i++)
        {
            result.Logger.Info("loop {V}", v);
        }

        // The bridge sees only user events.
        sink.Events.Should().HaveCount(100);
        sink.Events.Should().AllSatisfy(e =>
            e.MessageTemplate.Should().Be("loop {V}"));

        // Wait for the deferred announcement publish to land.
        AnnouncementSpinHelpers.WaitForAnnouncement();

        // The runtime-message channel saw exactly one announcement.
        var announcements = HeraldRuntimeMessages.RecentNotices
            .Where(n => n.Message.StartsWith("Herald active naming policy:", StringComparison.Ordinal))
            .ToList();
        announcements.Should().ContainSingle("the announcement is one-shot per StructuredLogger instance");
    }

    [Fact]
    public void Announcement_carries_active_policy_id_when_Camel_is_configured()
    {
        var sink = new CapturingBridge();
        var result = QuickLogBuilder.Create()
            .WithBridge(sink)
            .WithMinimumLevel("info")
            .WithNamingPolicy(PropertyNamingPolicy.Camel)
            .BuildAndCommit();

        var v = 1;
        result.Logger.Info("seed {V}", v);

        AnnouncementSpinHelpers.WaitForAnnouncement();

        HeraldRuntimeMessages.RecentNotices.Should().ContainSingle()
            .Which.Properties[0].Value.Should().Be("camel");
    }

    // -- Suppression --------------------------------------------------------

    [Fact]
    public void SuppressNamingPolicyAnnouncement_silences_the_event()
    {
        var sink = new CapturingBridge();
        var result = QuickLogBuilder.Create()
            .WithBridge(sink)
            .WithMinimumLevel("info")
            .SuppressNamingPolicyAnnouncement()
            .BuildAndCommit();

        var v = 1;
        result.Logger.Info("seed {V}", v);

        sink.Events.Should().HaveCount(1, "only the user event survives; the announcement is suppressed");
        sink.Events[0].MessageTemplate.Should().Be("seed {V}");

        // Suppression silences the runtime channel too — the operator
        // is opting out of the diagnostic, not just out of the
        // pipeline-level emission.
        HeraldRuntimeMessages.RecentNotices.Should().BeEmpty();
    }

    [Fact]
    public void Suppression_holds_across_many_dispatches()
    {
        var sink = new CapturingBridge();
        var result = QuickLogBuilder.Create()
            .WithBridge(sink)
            .WithMinimumLevel("info")
            .SuppressNamingPolicyAnnouncement()
            .BuildAndCommit();

        var v = 1;
        for (var i = 0; i < 50; i++)
        {
            result.Logger.Info("loop {V}", v);
        }

        sink.Events.Should().AllSatisfy(e =>
            e.MessageTemplate.Should().NotStartWith("Herald active naming policy:"));
    }

    // -- Below-info minimum --------------------------------------------------

    [Fact]
    public void Announcement_fires_on_runtime_channel_regardless_of_pipeline_minimum_level()
    {
        // Spec invariant (post channel-split): pipeline minimum-level
        // filters USER events. Framework-emitted notices live on a
        // separate channel and are not subject to user-level rules.
        // Setting MinimumLevel=Warn quiets Info-level user logs but
        // does NOT silence the runtime-channel announcement — an
        // operator who upgrades from a noisier policy still gets the
        // diagnostic visibility.
        var sink = new CapturingBridge();
        var result = QuickLogBuilder.Create()
            .WithBridge(sink)
            .WithMinimumLevel("warn")
            .BuildAndCommit();

        var v = 1;
        result.Logger.Warn("seed {V}", v);

        // Bridge sees only the Warn-level user event (and never the
        // announcement, regardless of level).
        sink.Events.Should().ContainSingle()
            .Which.MessageTemplate.Should().Be("seed {V}");

        AnnouncementSpinHelpers.WaitForAnnouncement();

        // Runtime channel sees the announcement.
        HeraldRuntimeMessages.RecentNotices.Should().ContainSingle()
            .Which.Message.Should().StartWith("Herald active naming policy:");
    }

    // -- Multi-pipeline -----------------------------------------------------

    [Fact]
    public void Multi_pipeline_host_emits_one_announcement_per_StructuredLogger()
    {
        // Spec contract: announcement is per-logger, not process-wide. A
        // process with N pipelines emits N announcements, one each on
        // first dispatch through that pipeline — all to the runtime
        // channel.
        var sinkA = new CapturingBridge();
        var sinkB = new CapturingBridge();
        var pipelineA = QuickLogBuilder.Create()
            .WithBridge(sinkA)
            .WithMinimumLevel("info")
            .BuildAndCommit();
        var pipelineB = QuickLogBuilder.Create()
            .WithBridge(sinkB)
            .WithMinimumLevel("info")
            .WithNamingPolicy(PropertyNamingPolicy.Snake)
            .BuildAndCommit();

        var v = 1;
        pipelineA.Logger.Info("from {Tenant}", v);
        pipelineB.Logger.Info("from {Tenant}", v);

        // Each bridge has its tenant's user event only — no
        // cross-contamination, no announcement noise.
        sinkA.Events.Should().ContainSingle()
            .Which.MessageTemplate.Should().Be("from {Tenant}");
        sinkB.Events.Should().ContainSingle()
            .Which.MessageTemplate.Should().Be("from {Tenant}");

        // Wait for both deferred publishes.
        AnnouncementSpinHelpers.WaitForAnnouncements(2);

        // Runtime channel has both announcements. PolicyIds are
        // distinct (pascal vs snake) so they're uniquely identifiable.
        var notices = HeraldRuntimeMessages.RecentNotices.ToList();
        notices.Should().HaveCount(2);
        notices.Select(n => n.Properties.Single().Value)
            .Should().BeEquivalentTo(new[] { "pascal", "snake" });
    }

    // -- Source-gen path ----------------------------------------------------

    [Fact]
    public void RecordCompileTimeResolution_also_arms_the_announcement_gate()
    {
        // Source-gen [HeraldLog] dispatches call RecordCompileTimeResolution,
        // which also routes through EnsureAnnouncementFired. So a service
        // that uses ONLY source-gen calls still sees the announcement on
        // the runtime channel on its first dispatch.
        var sink = new CapturingBridge();
        var result = QuickLogBuilder.Create()
            .WithBridge(sink)
            .WithMinimumLevel("info")
            .BuildAndCommit();

        result.Logger.RecordCompileTimeResolution();

        // The bridge stays empty — RecordCompileTimeResolution
        // doesn't emit a user event, and the announcement lives on
        // the runtime channel.
        sink.Events.Should().BeEmpty();

        AnnouncementSpinHelpers.WaitForAnnouncement();

        // The runtime channel saw the announcement.
        HeraldRuntimeMessages.RecentNotices.Should().ContainSingle()
            .Which.Message.Should().StartWith("Herald active naming policy:");
    }

    // -- ReloadDegraded surface (Phase 3 hot-reload path) -------------------

    [Fact]
    public void Hot_reload_with_unknown_policy_id_records_failure_and_keeps_prior_policy()
    {
        // Spec invariant: hot-reload that names an unregistered policy id
        // must NOT crash the live pipeline. The prior policy stays active
        // and a Warn record flows through the default DiagnosticLogFailureSink
        // so operators see the misconfigured reload without losing the
        // service. Cold-start FromConfiguration throws — different path,
        // covered in Phase 3 tests.
        var live = QuickLogBuilder.Create()
            .WithConsoleSink()
            .WithMinimumLevel("info")
            .WithHotReload()
            .WithNamingPolicy(PropertyNamingPolicy.Snake)
            .BuildAndCommit();

        // Build a fresh JSON config with the same shape, then swap in a
        // bogus policy id and reload.
        var freshConfig = QuickLogBuilder.Create()
            .WithConsoleSink()
            .WithMinimumLevel("info")
            .WithHotReload()
            .ExportConfig();
        // JSON is serialised with JsonKnownNamingPolicy.CamelCase, so the
        // field lands as "namingPolicy" on the wire even though the C# record
        // declares it as "NamingPolicy".
        var brokenConfig = freshConfig.Replace(
            "\"namingPolicy\": null",
            "\"namingPolicy\": \"phantom-policy\"",
            StringComparison.Ordinal);
        brokenConfig.Should().Contain("phantom-policy",
            "the substitution must land — if the field shape changes, this test needs an update");

        live.HotReloadBootstrap.Should().NotBeNull("WithHotReload() should yield a hot-reload bootstrap");
        live.HotReloadBootstrap!.Reload(brokenConfig);

        // Live policy survives — Snake remains active, not flipped to
        // anything else.
        live.Logger.NamingPolicy.Should().BeSameAs(SnakeCasePolicy.Instance,
            "unknown policy id during hot reload must keep the prior policy active");

        // Default failure sink captured the degraded-reload event.
        var diag = live.FailureSink as DiagnosticLogFailureSink;
        diag.Should().NotBeNull("the default bootstrap installs a DiagnosticLogFailureSink");
        var entries = diag!.GetEntries();
        entries.Should().ContainSingle(e =>
            e.Source == nameof(MMP.Herald.Bootstrap.HotReloadableLoggingBootstrap)
            && e.Message.Contains("Hot reload kept naming policy")
            && e.ExceptionType == typeof(UnknownNamingPolicyException).FullName);
    }

    // -- Plumbing -----------------------------------------------------------

    /// <summary>
    /// Minimal capturing bridge — sequenced single-threaded list so test
    /// assertions can rely on insertion order. Tests in this collection are
    /// non-parallel so we don't need a thread-safe ordered sink.
    /// </summary>
    private sealed class CapturingBridge : MMP.Herald.ILogger
    {
        private readonly List<LogEvent> _events = new();
        private readonly object _sync = new();

        public IReadOnlyList<LogEvent> Events
        {
            get
            {
                lock (_sync) { return _events.ToArray(); }
            }
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

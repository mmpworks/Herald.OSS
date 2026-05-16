#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MMP.Herald.Events;
using MMP.Herald.Failures;
using MMP.Herald.Quick;
using MMP.Herald.Templating;
using MMP.Herald.Templating.NamingPolicies;
using Xunit;

namespace MMP.Herald.OSS.Tests.Pipeline;

/// <summary>
/// Phase 5: first-dispatch naming-policy announcement + ReloadDegraded
/// failure-sink event. Announcement fires once per <c>StructuredLogger</c>
/// on the first dispatch through it. Suppression covers
/// <c>SuppressNamingPolicyAnnouncement()</c> on the builder.
/// </summary>
[Collection(nameof(NamingPolicyAnnouncementTests))]
[CollectionDefinition(nameof(NamingPolicyAnnouncementTests), DisableParallelization = true)]
public sealed class NamingPolicyAnnouncementTests
{
    public NamingPolicyAnnouncementTests()
    {
        NameResolverCache.Reset();
    }

    // -- Announcement firing ------------------------------------------------

    [Fact]
    public void First_dispatch_emits_one_announcement_event_with_active_policy_id()
    {
        var sink = new CapturingBridge();
        var result = QuickLogBuilder.Create()
            .WithBridge(sink)
            .WithMinimumLevel("info")
            .BuildAndCommit();

        var userId = "alice";
        result.Logger.Info("user {UserId} signed in", userId);

        // Two events should land at the bridge: the announcement first,
        // then the user-emitted event.
        sink.Events.Should().HaveCount(2);
        var announcement = sink.Events[0];
        announcement.MessageTemplate.Should().StartWith("Herald active naming policy:");
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

        var announcements = sink.Events
            .Where(e => e.MessageTemplate.StartsWith("Herald active naming policy:", StringComparison.Ordinal))
            .ToList();
        announcements.Should().HaveCount(1, "the announcement is one-shot per StructuredLogger instance");
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

        sink.Events[0].Properties[0].Value.Should().Be("camel");
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
    public void Announcement_is_silent_when_minimum_level_rejects_Info()
    {
        // Spec invariant: the announcement is emitted at Info; if the
        // pipeline's minimum level filters Info out, the message is
        // dropped silently. No retry, no escalation — operators who set
        // Warn-only will not see the announcement, which is correct
        // behaviour for any user-level event at Info.
        var sink = new CapturingBridge();
        var result = QuickLogBuilder.Create()
            .WithBridge(sink)
            .WithMinimumLevel("warn")
            .BuildAndCommit();

        var v = 1;
        result.Logger.Warn("seed {V}", v);

        sink.Events.Should().AllSatisfy(e =>
            e.MessageTemplate.Should().NotStartWith("Herald active naming policy:"));
    }

    // -- Multi-pipeline -----------------------------------------------------

    [Fact]
    public void Multi_pipeline_host_emits_one_announcement_per_StructuredLogger()
    {
        // Spec contract: announcement is per-logger, not process-wide. A
        // process with N pipelines emits N announcements, one each on
        // first dispatch through that pipeline.
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

        sinkA.Events[0].MessageTemplate.Should().StartWith("Herald active naming policy:");
        sinkA.Events[0].Properties[0].Value.Should().Be("pascal");

        sinkB.Events[0].MessageTemplate.Should().StartWith("Herald active naming policy:");
        sinkB.Events[0].Properties[0].Value.Should().Be("snake");
    }

    // -- Source-gen path ----------------------------------------------------

    [Fact]
    public void RecordCompileTimeResolution_also_arms_the_announcement_gate()
    {
        // Source-gen [HeraldLog] dispatches call RecordCompileTimeResolution,
        // which also routes through EnsureAnnouncementFired. So a service
        // that uses ONLY source-gen calls still sees the announcement on
        // the first one.
        var sink = new CapturingBridge();
        var result = QuickLogBuilder.Create()
            .WithBridge(sink)
            .WithMinimumLevel("info")
            .BuildAndCommit();

        result.Logger.RecordCompileTimeResolution();

        sink.Events.Should().ContainSingle()
            .Which.MessageTemplate.Should().StartWith("Herald active naming policy:");
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

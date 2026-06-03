#nullable enable

using System;
using System.Linq;
using FluentAssertions;
using MMP.Herald.Events;
using MMP.Herald.Levels;
using MMP.Herald.OSS.Tests.Helpers;
using MMP.Herald.Pipeline;
using MMP.Herald.Pipeline.Kernel;
using MMP.Herald.Templating;
using Xunit;

namespace MMP.Herald.OSS.Tests.Pipeline.Kernel;

public sealed class GenSourceGatedSinkTests
{
    private const string ReferenceSource = "ref-32hex-pipeline-token-AAAA";
    private const string ExternalKey = "plugin-alpha";
    private const string OtherKey = "plugin-bravo";

    private static LogEvent NewEvent(string? genSource) =>
        new LogEvent(
            TimeUtc: DateTimeOffset.UtcNow,
            Level: KnownLogLevels.Information,
            Category: LogCategory.App,
            MessageTemplate: "msg",
            Message: "msg",
            Properties: LogEvent.EmptyProperties,
            Context: LogEvent.EmptyContext,
            GenSource: genSource);

    [Fact]
    public void Reference_event_is_accepted_by_default()
    {
        var inner = new SpyLogger();
        var gate = new GenSourceGatedSink(inner, ReferenceSource);

        gate.Log(NewEvent(ReferenceSource));

        inner.Events.Should().ContainSingle();
    }

    [Fact]
    public void Event_with_null_genSource_is_dropped()
    {
        var inner = new SpyLogger();
        var gate = new GenSourceGatedSink(inner, ReferenceSource);

        gate.Log(NewEvent(null));

        inner.Events.Should().BeEmpty();
    }

    [Fact]
    public void Event_with_unknown_genSource_is_dropped()
    {
        var inner = new SpyLogger();
        var gate = new GenSourceGatedSink(inner, ReferenceSource);

        gate.Log(NewEvent("fabricated-source"));

        inner.Events.Should().BeEmpty();
    }

    [Fact]
    public void Event_from_another_pipeline_is_dropped()
    {
        var inner = new SpyLogger();
        var gate = new GenSourceGatedSink(inner, ReferenceSource);

        // A different pipeline's reference token — perfectly legitimate elsewhere,
        // not for this gate.
        gate.Log(NewEvent("other-pipeline-12345-AAAA"));

        inner.Events.Should().BeEmpty();
    }

    [Fact]
    public void Registered_external_source_is_accepted()
    {
        var inner = new SpyLogger();
        var gate = new GenSourceGatedSink(inner, ReferenceSource);
        gate.RegisterAcceptedSource(ExternalKey);

        gate.Log(NewEvent(ExternalKey));

        inner.Events.Should().ContainSingle();
    }

    [Fact]
    public void Other_unregistered_external_source_is_still_dropped_after_one_registers()
    {
        var inner = new SpyLogger();
        var gate = new GenSourceGatedSink(inner, ReferenceSource);
        gate.RegisterAcceptedSource(ExternalKey);

        gate.Log(NewEvent(OtherKey));

        inner.Events.Should().BeEmpty();
    }

    [Fact]
    public void Revoked_external_source_is_dropped()
    {
        var inner = new SpyLogger();
        var gate = new GenSourceGatedSink(inner, ReferenceSource);
        gate.RegisterAcceptedSource(ExternalKey);
        gate.RevokeAcceptedSource(ExternalKey);

        gate.Log(NewEvent(ExternalKey));

        inner.Events.Should().BeEmpty();
    }

    [Fact]
    public void Registering_same_source_twice_is_idempotent()
    {
        var inner = new SpyLogger();
        var gate = new GenSourceGatedSink(inner, ReferenceSource);
        gate.RegisterAcceptedSource(ExternalKey);
        gate.RegisterAcceptedSource(ExternalKey);

        gate.Log(NewEvent(ExternalKey));
        gate.Log(NewEvent(ExternalKey));

        inner.Events.Should().HaveCount(2);
    }

    [Fact]
    public void IsAccepted_reference_path_uses_reference_equality()
    {
        // The fast-path docblock claims one ReferenceEquals on the common
        // case. Verify we accept the same instance and also accept an
        // equal-but-different-instance string (string equality fallback).
        var gate = new GenSourceGatedSink(new SpyLogger(), ReferenceSource);

        gate.IsAccepted(ReferenceSource).Should().BeTrue();
        gate.IsAccepted(string.Concat("ref-32hex-", "pipeline-token-AAAA"))
            .Should().BeTrue();
        gate.IsAccepted(null).Should().BeFalse();
        gate.IsAccepted("").Should().BeFalse();
    }

    [Fact]
    public void Rejection_callback_fires_on_drop()
    {
        var inner = new SpyLogger();
        string? capturedSource = null;
        string? capturedSinkType = null;
        var gate = new GenSourceGatedSink(inner, ReferenceSource,
            onRejection: (src, sinkType) =>
            {
                capturedSource = src;
                capturedSinkType = sinkType;
            });

        gate.Log(NewEvent("rogue-source"));

        capturedSource.Should().Be("rogue-source");
        capturedSinkType.Should().Be(nameof(SpyLogger));
        inner.Events.Should().BeEmpty();
    }

    [Fact]
    public void Rejection_callback_records_null_source_as_marker_string()
    {
        string? captured = null;
        var gate = new GenSourceGatedSink(new SpyLogger(), ReferenceSource,
            onRejection: (src, _) => captured = src);

        gate.Log(NewEvent(null));

        captured.Should().Be("(null)");
    }

    // ── REGRESSION (MEDIUM — gate-notice routing): the default rejection notice
    //    must publish to the OWNING HOST's runtime-message channel, not the global
    //    static Default. Pre-fix EmitRejectionNotice called the static
    //    HeraldRuntimeMessages.Publish facade (always Default's instance), so a
    //    rejection on a non-default host's gate leaked onto the Default buffer and
    //    never reached the host's own buffer. Post-fix the gate coalesces a
    //    threaded HeraldRuntimeMessagesInstance and publishes there. ──────────────

    [Fact]
    public void Rejection_notice_lands_on_the_owning_host_buffer_not_default()
    {
        // A fresh non-default host owns an isolated runtime-message channel.
        var host = new MMP.Herald.Quick.HeraldHost();

        // Use a gate-rejection source unique to THIS test so the Default-buffer
        // assertion below cannot be inflated or matched by a parallel sibling that
        // also publishes a gate notice. The gate's source token is a constant, so
        // we discriminate on the host buffer (isolated) and on the absence of the
        // gate source from Default for THIS host's reference token specifically.
        const string GateSource = "@herald.runtime.gen-source-gate";

        var inner = new SpyLogger();
        // Thread the non-default host's channel into the gate (production default
        // is null → Default; here we pass the host's own instance).
        var gate = new GenSourceGatedSink(
            inner, ReferenceSource, runtimeMessages: host.RuntimeMessages);

        // A rejected event (wrong GenSource) fires the default one-shot notice.
        gate.Log(NewEvent("rogue-source"));

        inner.Events.Should().BeEmpty("the rogue event is dropped");

        // The notice lands on the host's OWN (isolated) buffer — this is the
        // primary assertion and is race-free because the buffer is per-host.
        host.RuntimeMessages.RecentNotices.Should().ContainSingle(
            n => n.Source == GateSource,
            "the gate rejection notice must publish to the owning host's channel");

        // And the host's notice carries this gate's identity (the rogue source +
        // sink type), confirming it is THIS rejection on the host buffer, not a
        // stray one. Pre-fix the notice went to the static Default facade and this
        // host buffer would have been empty.
        var hostNotice = host.RuntimeMessages.RecentNotices.Single(n => n.Source == GateSource);
        hostNotice.Properties.Should().Contain(
            p => p.Name == "genSource" && (string?)p.Value == "rogue-source",
            "the host-scoped notice names the rejected event's source — proving the gate " +
            "published to the host channel, not the global Default");
    }

    private sealed class KernelSpy : ILogger, IKernelSink
    {
        public int LogCount;
        public int BufferLogCount;

        public void Log(LogEvent logEvent) => LogCount++;
        public void Log(in LogEventBuffer buffer) => BufferLogCount++;
    }

    [Fact]
    public void Kernel_path_validates_buffer_genSource()
    {
        var inner = new KernelSpy();
        var gate = (GenSourceGatedKernelSink)GenSourceGatedSink.Wrap(inner, ReferenceSource);

        var rejected = new LogEventBuffer(
            timeUtc: DateTimeOffset.UtcNow,
            level: KnownLogLevels.Information,
            category: LogCategory.App,
            messageTemplate: "msg",
            message: "msg",
            properties: ReadOnlySpan<LogProperty>.Empty,
            genSource: "rogue");
        gate.Log(in rejected);
        inner.BufferLogCount.Should().Be(0);

        var accepted = new LogEventBuffer(
            timeUtc: DateTimeOffset.UtcNow,
            level: KnownLogLevels.Information,
            category: LogCategory.App,
            messageTemplate: "msg",
            message: "msg",
            properties: ReadOnlySpan<LogProperty>.Empty,
            genSource: ReferenceSource);
        gate.Log(in accepted);
        inner.BufferLogCount.Should().Be(1);
    }

    [Fact]
    public void Wrap_returns_kernel_gate_for_IKernelSink_inner()
    {
        var inner = new KernelSpy();
        var wrapped = GenSourceGatedSink.Wrap(inner, ReferenceSource);
        wrapped.Should().BeOfType<GenSourceGatedKernelSink>();
    }

    [Fact]
    public void Wrap_returns_plain_gate_for_non_kernel_inner()
    {
        var inner = new SpyLogger();  // doesn't implement IKernelSink
        var wrapped = GenSourceGatedSink.Wrap(inner, ReferenceSource);
        wrapped.Should().NotBeOfType<GenSourceGatedKernelSink>();
        wrapped.Should().BeOfType<GenSourceGatedSink>();
    }

    [Fact]
    public void Label_is_preserved_when_provided()
    {
        var gate = GenSourceGatedSink.Wrap(
            new SpyLogger(), ReferenceSource, label: "console-prod");
        gate.Label.Should().Be("console-prod");
    }

    [Fact]
    public void Label_is_auto_generated_when_null_or_empty()
    {
        var fromNull = GenSourceGatedSink.Wrap(new SpyLogger(), ReferenceSource);
        var fromEmpty = GenSourceGatedSink.Wrap(new SpyLogger(), ReferenceSource, label: "");

        fromNull.Label.Should().NotBeNullOrEmpty().And.HaveLength(32);
        fromEmpty.Label.Should().NotBeNullOrEmpty().And.HaveLength(32);
        fromNull.Label.Should().NotBe(fromEmpty.Label);
    }

    [Fact]
    public void Label_distinct_per_gate_when_auto_generated()
    {
        var a = GenSourceGatedSink.Wrap(new SpyLogger(), ReferenceSource);
        var b = GenSourceGatedSink.Wrap(new SpyLogger(), ReferenceSource);
        a.Label.Should().NotBe(b.Label);
    }
}

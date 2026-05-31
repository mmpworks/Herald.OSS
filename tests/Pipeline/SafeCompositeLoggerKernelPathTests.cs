#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using MMP.Herald;
using MMP.Herald.Events;
using MMP.Herald.Failures;
using MMP.Herald.Levels;
using MMP.Herald.Pipeline;
using MMP.Herald.Pipeline.Kernel;
using MMP.Herald.Templating;
using Xunit;

namespace MMP.Herald.OSS.Tests.Pipeline;

/// <summary>
/// Regression coverage for SafeCompositeLogger's kernel-path fan-out
/// (<see cref="IKernelSink.Log(in LogEventBuffer)"/>), added 2026-05-30 to
/// close the async-soak allocation: the composite previously implemented only
/// <see cref="ILogger"/>, so <see cref="FastPathAsyncSink"/> wrapped a
/// non-kernel inner and materialised a heap <see cref="LogEvent"/> +
/// <c>LogProperty[]</c> per event on the drain thread (~162-356 B/event).
///
/// The suite pins the class of behaviour, not just the single repro:
/// 1. The composite IS an IKernelSink and fans buffers to kernel children
///    without forcing a heap LogEvent.
/// 2. Legacy (non-kernel) children still receive a faithful heap LogEvent.
/// 3. Mixed children: kernel children get the buffer, legacy get the event,
///    in one pass.
/// 4. Canonical equivalence: the buffer the kernel child receives carries the
///    same field values a synchronous Log(LogEvent) would have delivered —
///    across default axes, Silent visibility, non-default CaptureMode,
///    non-null EventId, and non-null GenSource. The typed soak workload is
///    all-default-axes and would NOT catch an axes-loss; these vectors do.
/// 5. Failure isolation is preserved: one throwing child does not stop the
///    others, and AuditLogFailureException still propagates.
/// </summary>
public sealed class SafeCompositeLoggerKernelPathTests
{
    private static LogEventBuffer BuildBuffer(
        ReadOnlySpan<LogProperty> props,
        LogEventId? eventId = null,
        string? genSource = null) =>
        new(
            timeUtc: new DateTimeOffset(2026, 5, 30, 1, 2, 3, TimeSpan.Zero),
            level: KnownLogLevels.Information,
            category: LogCategory.App,
            messageTemplate: "User {user} did {action}",
            message: "User alice did purchase",
            properties: props,
            eventId: eventId,
            genSource: genSource);

    [Fact]
    public void Composite_is_a_kernel_sink()
    {
        var composite = new SafeCompositeLogger(new ILogger[] { new CollectingKernelSink() });
        composite.Should().BeAssignableTo<IKernelSink>(
            "the async drain dispatches the buffer through IKernelSink to stay 0-alloc");
    }

    [Fact]
    public void Kernel_child_receives_buffer_without_heap_event()
    {
        var kernel = new CollectingKernelSink();
        var composite = new SafeCompositeLogger(new ILogger[] { kernel });

        var props = new[] { new LogProperty("user", "alice"), new LogProperty("action", "purchase") };
        composite.Log(BuildBuffer(props));

        kernel.BufferCalls.Should().Be(1, "kernel children take the in-buffer path");
        kernel.EventCalls.Should().Be(0, "the composite must not materialise a LogEvent for a kernel child");
        var snap = kernel.Snapshots.Single();
        snap.Properties.Select(p => p.Name).Should().Equal("user", "action");
        snap.Properties.Select(p => p.Value).Should().Equal("alice", "purchase");
    }

    [Fact]
    public void Legacy_child_receives_faithful_heap_event()
    {
        var legacy = new CollectingLegacyLogger();
        var composite = new SafeCompositeLogger(new ILogger[] { legacy });

        var props = new[] { new LogProperty("user", "alice") };
        composite.Log(BuildBuffer(props, genSource: "tenant-7"));

        var evt = legacy.Events.Single();
        evt.Level.Key.Should().Be("information");
        evt.Category.Should().Be(LogCategory.App);
        evt.MessageTemplate.Should().Be("User {user} did {action}");
        evt.GenSource.Should().Be("tenant-7", "provenance must survive the buffer->LogEvent rebuild");
        evt.Properties.Single().Name.Should().Be("user");
        evt.Properties.Single().Value.Should().Be("alice");
    }

    [Fact]
    public void Mixed_children_each_take_their_own_path_in_one_pass()
    {
        var kernel = new CollectingKernelSink();
        var legacy = new CollectingLegacyLogger();
        var composite = new SafeCompositeLogger(new ILogger[] { kernel, legacy });

        composite.Log(BuildBuffer(new[] { new LogProperty("k", "v") }));

        kernel.BufferCalls.Should().Be(1);
        legacy.Events.Should().HaveCount(1);
        legacy.Events.Single().Properties.Single().Value.Should().Be("v");
    }

    // ── Canonical-equivalence vectors (Attack 2) ────────────────────────
    // The kernel child must see field-for-field what a sync Log(LogEvent)
    // would have delivered. The soak workload is all-default-axes, so these
    // non-default vectors are the only guard against an axes-loss regression.

    [Fact]
    public void Silent_visibility_survives_to_kernel_child()
    {
        var kernel = new CollectingKernelSink();
        var composite = new SafeCompositeLogger(new ILogger[] { kernel });

        var props = new[] { LogProperty.Silent("secret", "xyz") };
        composite.Log(BuildBuffer(props));

        kernel.Snapshots.Single().Properties.Single().IsSilent.Should().BeTrue(
            "Visibility=Silent must reach the kernel child unchanged");
    }

    [Fact]
    public void NonDefault_capture_mode_survives_to_kernel_child()
    {
        var kernel = new CollectingKernelSink();
        var composite = new SafeCompositeLogger(new ILogger[] { kernel });

        var props = new[]
        {
            new LogProperty("obj", "v", CaptureMode: LogPropertyCaptureMode.Destructure),
        };
        composite.Log(BuildBuffer(props));

        kernel.Snapshots.Single().Properties.Single().CaptureModeOrDefault
            .Should().Be(LogPropertyCaptureMode.Destructure);
    }

    [Fact]
    public void EventId_and_GenSource_round_trip_to_legacy_child()
    {
        var legacy = new CollectingLegacyLogger();
        var composite = new SafeCompositeLogger(new ILogger[] { legacy });

        var eventId = new LogEventId(99, "checkout");
        composite.Log(BuildBuffer(
            new[] { new LogProperty("user", "alice") },
            eventId: eventId,
            genSource: "tenant-3"));

        var evt = legacy.Events.Single();
        evt.EventId.Should().Be(eventId);
        evt.GenSource.Should().Be("tenant-3");
    }

    // ── Failure isolation (Attack 1) ────────────────────────────────────

    [Fact]
    public void One_throwing_child_does_not_stop_the_others()
    {
        var failures = new RecordingFailureSink();
        var thrower = new ThrowingKernelSink();
        var healthy = new CollectingKernelSink();
        var composite = new SafeCompositeLogger(new ILogger[] { thrower, healthy }, failures);

        composite.Log(BuildBuffer(new[] { new LogProperty("k", "v") }));

        healthy.BufferCalls.Should().Be(1, "a sibling failure must not block a healthy sink");
        failures.Count.Should().Be(1, "the failing child is reported through the failure sink");
    }

    [Fact]
    public void Audit_failure_propagates_from_kernel_path()
    {
        var auditThrower = new AuditThrowingKernelSink();
        var healthy = new CollectingKernelSink();
        var composite = new SafeCompositeLogger(new ILogger[] { healthy, auditThrower });

        var act = () => composite.Log(BuildBuffer(new[] { new LogProperty("k", "v") }));

        act.Should().Throw<AuditLogFailureException>(
            "an audit failure must propagate even on the kernel fan-out path");
        healthy.BufferCalls.Should().Be(1, "the healthy child still ran before the audit throw surfaced");
    }

    // ── Test fakes ──────────────────────────────────────────────────────

    private sealed record Snapshot(LogProperty[] Properties);

    private sealed class CollectingKernelSink : ILogger, IKernelSink
    {
        private readonly List<Snapshot> _snaps = new();
        private readonly object _gate = new();
        public int BufferCalls { get; private set; }
        public int EventCalls { get; private set; }
        public IReadOnlyList<Snapshot> Snapshots { get { lock (_gate) return _snaps.ToArray(); } }

        public void Log(LogEvent logEvent)
        {
            lock (_gate) { EventCalls++; _snaps.Add(new Snapshot(logEvent.Properties.ToArray())); }
        }

        public void Log(in LogEventBuffer buffer)
        {
            var props = new LogProperty[buffer.Properties.Length];
            for (var i = 0; i < props.Length; i++) props[i] = buffer.Properties[i];
            lock (_gate) { BufferCalls++; _snaps.Add(new Snapshot(props)); }
        }
    }

    private sealed class CollectingLegacyLogger : ILogger
    {
        private readonly List<LogEvent> _events = new();
        private readonly object _gate = new();
        public IReadOnlyList<LogEvent> Events { get { lock (_gate) return _events.ToArray(); } }
        public void Log(LogEvent logEvent) { lock (_gate) _events.Add(logEvent); }
    }

    private sealed class ThrowingKernelSink : ILogger, IKernelSink
    {
        public void Log(LogEvent logEvent) => throw new InvalidOperationException("boom");
        public void Log(in LogEventBuffer buffer) => throw new InvalidOperationException("boom");
    }

    private sealed class AuditThrowingKernelSink : ILogger, IKernelSink
    {
        private static AuditLogFailureException Boom() =>
            new("audit-sink", BuildFailedEvent(), new InvalidOperationException("audit boom"));

        private static LogEvent BuildFailedEvent() => new(
            TimeUtc: DateTimeOffset.UtcNow,
            Level: KnownLogLevels.Information,
            Category: LogCategory.App,
            MessageTemplate: "x",
            Message: "x",
            Properties: LogEvent.EmptyProperties,
            Context: LogEvent.EmptyContext);

        public void Log(LogEvent logEvent) => throw Boom();
        public void Log(in LogEventBuffer buffer) => throw Boom();
    }

    private sealed class RecordingFailureSink : ILogFailureSink
    {
        private int _count;
        public int Count => _count;
        public void ReportFailure(LogEvent logEvent, Exception exception, string source) => _count++;
    }
}

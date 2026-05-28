#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MMP.Herald;
using MMP.Herald.Events;
using MMP.Herald.Levels;
using MMP.Herald.Pipeline.Kernel;
using MMP.Herald.Templating;
using Xunit;

namespace MMP.Herald.OSS.Tests.Pipeline.Kernel;

/// <summary>
/// Regression coverage for FastPathAsyncSink after the Lever A promotion
/// (inline AsyncEnvelope on Channel&lt;AsyncEnvelope&gt; replaces the prior
/// heap-LogEvent on Channel&lt;LogEvent&gt; shape).
///
/// Verifies:
/// 1. Round-trip integrity — what the producer hands to FastPathAsyncSink
///    is what the inner sink receives, post-envelope-pack-and-unpack.
/// 2. Lazy-resolution contract — LogProperty.Lazy factories resolve on the
///    producer thread, not the drain thread. (L1 of the async-sink
///    cross-tenant PII fix; see #90.)
/// 3. Inner-sink dispatch — IKernelSink inners take the buffer path,
///    legacy ILogger inners take the heap LogEvent path.
/// 4. Drain semantics — DrainAsync flushes pending events; DisposeAsync
///    is idempotent.
/// </summary>
public sealed class FastPathAsyncSinkLeverATests
{
    [Fact]
    public async Task LegacyInner_receives_heap_LogEvents_with_round_tripped_properties()
    {
        var inner = new CollectingLegacyLogger();
        var sink = new FastPathAsyncSink(inner, boundedCapacity: 32);

        var category = LogCategory.App;
        var props = new[]
        {
            new LogProperty("user", "alice"),
            new LogProperty("session", 42),
            LogProperty.Silent("trace", "abc-123"),
        };
        var evt = new LogEvent(
            TimeUtc: DateTimeOffset.UtcNow,
            Level: KnownLogLevels.Info,
            Category: category,
            MessageTemplate: "User {user} on session {session}",
            Message: "User alice on session 42",
            Properties: props,
            Context: LogEvent.EmptyContext);

        sink.Log(evt);
        await sink.DrainAsync(TimeSpan.FromSeconds(2));

        inner.Events.Should().HaveCount(1);
        var received = inner.Events[0];
        received.Level.Key.Should().Be("info");
        received.MessageTemplate.Should().Be("User {user} on session {session}");
        received.Properties.Should().HaveCount(3);
        received.Properties[0].Name.Should().Be("user");
        received.Properties[0].Value.Should().Be("alice");
        received.Properties[1].Name.Should().Be("session");
        received.Properties[1].Value.Should().Be(42);
        received.Properties[2].Name.Should().Be("trace");
        // Silent visibility must round-trip through the envelope's PackAxes.
        received.Properties[2].IsSilent.Should().BeTrue(
            "Visibility=Silent must survive the envelope round-trip");

        await sink.DisposeAsync();
    }

    [Fact]
    public async Task KernelInner_receives_buffers_with_round_tripped_properties()
    {
        var inner = new CollectingKernelSink();
        var sink = new FastPathAsyncSink(inner, boundedCapacity: 32);

        var props = new[]
        {
            new LogProperty("user", "alice"),
            new LogProperty("count", 7L),
        };
        var evt = new LogEvent(
            TimeUtc: DateTimeOffset.UtcNow,
            Level: KnownLogLevels.Warn,
            Category: LogCategory.App,
            MessageTemplate: "User {user} count {count}",
            Message: "User alice count 7",
            Properties: props,
            Context: LogEvent.EmptyContext);

        sink.Log(evt);
        await sink.DrainAsync(TimeSpan.FromSeconds(2));

        inner.Snapshots.Should().HaveCount(1);
        var snap = inner.Snapshots[0];
        snap.Level.Key.Should().Be("warn");
        snap.MessageTemplate.Should().Be("User {user} count {count}");
        snap.Properties.Should().HaveCount(2);
        snap.Properties[0].Name.Should().Be("user");
        snap.Properties[0].Value.Should().Be("alice");
        snap.Properties[1].Name.Should().Be("count");
        snap.Properties[1].Value.Should().Be(7L);

        await sink.DisposeAsync();
    }

    [Fact]
    public async Task LazyProperty_resolves_on_producer_thread_not_drain_thread()
    {
        // The factory captures the calling thread's managed id. If the
        // envelope deferred resolution to the drain thread, the captured
        // id would be the drain thread's. The Lever A contract resolves
        // eagerly on the producer thread.
        var producerThreadId = Thread.CurrentThread.ManagedThreadId;
        var inner = new CollectingLegacyLogger();
        var sink = new FastPathAsyncSink(inner, boundedCapacity: 32);

        int? resolutionThreadId = null;
        Func<object?> factory = () =>
        {
            resolutionThreadId = Thread.CurrentThread.ManagedThreadId;
            return "resolved";
        };

        var evt = new LogEvent(
            TimeUtc: DateTimeOffset.UtcNow,
            Level: KnownLogLevels.Info,
            Category: LogCategory.App,
            MessageTemplate: "Trace {trace}",
            Message: "Trace lazy",
            Properties: new[] { LogProperty.Lazy("trace", factory) },
            Context: LogEvent.EmptyContext);

        sink.Log(evt);
        await sink.DrainAsync(TimeSpan.FromSeconds(2));

        resolutionThreadId.Should().NotBeNull(
            "the factory must have been invoked");
        resolutionThreadId.Should().Be(producerThreadId,
            "Lever A's eager-resolution contract requires producer-thread invocation, not drain-thread");

        inner.Events.Should().HaveCount(1);
        inner.Events[0].Properties[0].Value.Should().Be("resolved",
            "the resolved value must reach the inner sink, not the Func reference");

        await sink.DisposeAsync();
    }

    [Fact]
    public async Task LazyProperty_factory_throw_yields_descriptive_fallback_string()
    {
        // Eager resolution preserves the "logging never crashes" contract:
        // a throwing factory produces a descriptive fallback in the slot,
        // not an unhandled exception in the producer's enqueue path.
        var inner = new CollectingLegacyLogger();
        var sink = new FastPathAsyncSink(inner, boundedCapacity: 32);

        Func<object?> throwingFactory = () => throw new InvalidOperationException("boom");

        var evt = new LogEvent(
            TimeUtc: DateTimeOffset.UtcNow,
            Level: KnownLogLevels.Info,
            Category: LogCategory.App,
            MessageTemplate: "Trace {trace}",
            Message: "Trace lazy",
            Properties: new[] { LogProperty.Lazy("trace", throwingFactory) },
            Context: LogEvent.EmptyContext);

        // Must not throw out of Log; the factory throw is caught inside
        // EnvelopeSlot.FromLogProperty.
        var act = () => sink.Log(evt);
        act.Should().NotThrow();

        await sink.DrainAsync(TimeSpan.FromSeconds(2));

        inner.Events.Should().HaveCount(1);
        var receivedValue = inner.Events[0].Properties[0].Value as string;
        receivedValue.Should().NotBeNull();
        receivedValue.Should().Contain("Lazy property 'trace' threw InvalidOperationException");
        receivedValue.Should().Contain("boom");

        await sink.DisposeAsync();
    }

    [Fact]
    public async Task Empty_properties_round_trip_without_allocation_drama()
    {
        var inner = new CollectingLegacyLogger();
        var sink = new FastPathAsyncSink(inner, boundedCapacity: 32);

        var evt = new LogEvent(
            TimeUtc: DateTimeOffset.UtcNow,
            Level: KnownLogLevels.Info,
            Category: LogCategory.App,
            MessageTemplate: "no props",
            Message: "no props",
            Properties: LogEvent.EmptyProperties,
            Context: LogEvent.EmptyContext);

        sink.Log(evt);
        await sink.DrainAsync(TimeSpan.FromSeconds(2));

        inner.Events.Should().HaveCount(1);
        inner.Events[0].Properties.Should().BeEmpty();

        await sink.DisposeAsync();
    }

    [Fact]
    public async Task Overflow_arity_above_8_round_trips_through_overflow_array()
    {
        // Arity > 8 triggers the overflow heap-array path in AsyncEnvelope.
        // All 12 properties must survive the round trip.
        var inner = new CollectingLegacyLogger();
        var sink = new FastPathAsyncSink(inner, boundedCapacity: 32);

        var props = new LogProperty[12];
        for (var i = 0; i < 12; i++)
        {
            props[i] = new LogProperty($"p{i}", i);
        }
        var evt = new LogEvent(
            TimeUtc: DateTimeOffset.UtcNow,
            Level: KnownLogLevels.Info,
            Category: LogCategory.App,
            MessageTemplate: "12 props",
            Message: "12 props",
            Properties: props,
            Context: LogEvent.EmptyContext);

        sink.Log(evt);
        await sink.DrainAsync(TimeSpan.FromSeconds(2));

        inner.Events.Should().HaveCount(1);
        inner.Events[0].Properties.Should().HaveCount(12);
        for (var i = 0; i < 12; i++)
        {
            inner.Events[0].Properties[i].Name.Should().Be($"p{i}");
            inner.Events[0].Properties[i].Value.Should().Be(i);
        }

        await sink.DisposeAsync();
    }

    [Fact]
    public async Task DrainAsync_is_idempotent()
    {
        var inner = new CollectingLegacyLogger();
        var sink = new FastPathAsyncSink(inner, boundedCapacity: 32);

        var first = await sink.DrainAsync(TimeSpan.FromSeconds(2));
        var second = await sink.DrainAsync(TimeSpan.FromSeconds(2));
        first.Should().BeTrue();
        second.Should().BeTrue("DrainAsync must be safe to call repeatedly");

        await sink.DisposeAsync();
    }

    [Fact]
    public async Task DroppedCount_and_WrittenCount_sum_equals_input()
    {
        // Behaviour regression: under any inner sink + capacity combination,
        // every Log call accounts for exactly one written-or-dropped tick.
        // The pre-Lever-A counter discipline lives on the same code path.
        var inner = new CollectingLegacyLogger();
        var sink = new FastPathAsyncSink(inner, boundedCapacity: 4);

        const int total = 32;
        for (var i = 0; i < total; i++)
        {
            sink.Log(new LogEvent(
                TimeUtc: DateTimeOffset.UtcNow,
                Level: KnownLogLevels.Info,
                Category: LogCategory.App,
                MessageTemplate: "x",
                Message: "x",
                Properties: LogEvent.EmptyProperties,
                Context: LogEvent.EmptyContext));
        }

        await sink.DrainAsync(TimeSpan.FromSeconds(5));

        (sink.WrittenCount + sink.DroppedCount).Should().Be(total,
            "every Log call must account for exactly one written-or-dropped tick");

        await sink.DisposeAsync();
    }

    // ── Test fakes ──────────────────────────────────────────────────────

    // A snapshot taken from the IKernelSink Log(in LogEventBuffer) entry —
    // captures whatever the caller can verify post-drain. We must NOT keep
    // the buffer reference; it lives on the drain's stack.
    private sealed record BufferSnapshot(
        DateTimeOffset TimeUtc,
        LogLevel Level,
        LogCategory Category,
        string MessageTemplate,
        string Message,
        LogProperty[] Properties);

    private sealed class CollectingLegacyLogger : ILogger
    {
        private readonly List<LogEvent> _events = new();
        private readonly object _gate = new();

        public IReadOnlyList<LogEvent> Events
        {
            get { lock (_gate) return _events.ToArray(); }
        }

        public void Log(LogEvent logEvent)
        {
            lock (_gate) _events.Add(logEvent);
        }
    }

    private sealed class CollectingKernelSink : ILogger, IKernelSink
    {
        private readonly List<BufferSnapshot> _snapshots = new();
        private readonly object _gate = new();

        public IReadOnlyList<BufferSnapshot> Snapshots
        {
            get { lock (_gate) return _snapshots.ToArray(); }
        }

        public void Log(LogEvent logEvent)
        {
            // Should not be hit when FastPathAsyncSink routes through the
            // IKernelSink interface — but implement defensively.
            lock (_gate)
            {
                _snapshots.Add(new BufferSnapshot(
                    logEvent.TimeUtc, logEvent.Level, logEvent.Category,
                    logEvent.MessageTemplate, logEvent.Message,
                    System.Linq.Enumerable.ToArray(logEvent.Properties)));
            }
        }

        public void Log(in LogEventBuffer buffer)
        {
            var props = new LogProperty[buffer.Properties.Length];
            for (var i = 0; i < buffer.Properties.Length; i++)
            {
                props[i] = buffer.Properties[i];
            }
            lock (_gate)
            {
                _snapshots.Add(new BufferSnapshot(
                    buffer.TimeUtc, buffer.Level, buffer.Category,
                    buffer.MessageTemplate, buffer.Message, props));
            }
        }
    }

}

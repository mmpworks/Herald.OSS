#nullable enable

using System;
using System.Collections.Generic;
using FluentAssertions;
using MMP.Herald.Events;
using MMP.Herald.Levels;
using MMP.Herald.OSS.Tests.Helpers;
using MMP.Herald.OSS.Tests.TestSupport;
using MMP.Herald.Pipeline.Kernel;
using MMP.Herald.Templating;
using Xunit;

namespace MMP.Herald.OSS.Tests.Pipeline.Kernel;

/// <summary>
/// W6 — the property filter step (<c>Filter.ByExcluding</c> equivalent).
/// Pins the two contracts that make it safe: the predicate forwards exactly
/// the events it should, and the buffer hot path and the heap twin reach the
/// <b>same</b> verdict (the silent-divergence bug class).
/// </summary>
public sealed class PredicateFilteredKernelSinkTests
{
    private const string Template = "msg {Timing} {TenantId}";

    private static LogEventBuffer BufferWith(params LogProperty[] props) =>
        new(
            timeUtc: DateTimeOffset.UtcNow,
            level: KnownLogLevels.Information,
            category: LogCategory.App,
            messageTemplate: Template,
            message: "msg",
            properties: props);

    private static LogEvent EventWith(params LogProperty[] props) =>
        new(
            TimeUtc: DateTimeOffset.UtcNow,
            Level: KnownLogLevels.Information,
            Category: LogCategory.App,
            MessageTemplate: Template,
            Message: "msg",
            Properties: props,
            Context: LogEvent.EmptyContext);

    // Static lambda: captures nothing, so the steady-state buffer path is 0 B.
    private static readonly LogEventBufferPredicate ExcludeTiming =
        static (in LogEventBuffer b) => b.HasProperty("Timing");

    [Fact]
    public void Buffer_event_matching_predicate_is_excluded()
    {
        var inner = new KernelSpySink();
        var sink = new PredicateFilteredKernelSink(inner, ExcludeTiming);

        var buffer = BufferWith(new LogProperty("Timing", 42));
        sink.Log(in buffer);

        inner.BufferLogCount.Should().Be(0, "an event carrying Timing must be excluded");
    }

    [Fact]
    public void Buffer_event_not_matching_predicate_passes_through()
    {
        var inner = new KernelSpySink();
        var sink = new PredicateFilteredKernelSink(inner, ExcludeTiming);

        var buffer = BufferWith(new LogProperty("TenantId", "acme"));
        sink.Log(in buffer);

        inner.BufferLogCount.Should().Be(1, "an event without Timing must pass through");
    }

    // ── The load-bearing regression: ONE predicate, TWO entry points, EQUAL ──
    //
    // For every event shape, the buffer hot path and the heap twin must make
    // the identical admit/drop decision. A divergence here is the silent bug
    // the whole W6 design exists to prevent — a filter that drops on one entry
    // point and keeps on the other. We drive both entry points with the same
    // event content and assert their forwarding counts match exactly.

    public static IEnumerable<object[]> DecisionEqualityCases()
    {
        yield return new object[] { new LogProperty("Timing", 1), false };       // excluded
        yield return new object[] { new LogProperty("TenantId", "acme"), true }; // kept
        yield return new object[] { new LogProperty("Other", "x"), true };       // kept
    }

    [Theory]
    [MemberData(nameof(DecisionEqualityCases))]
    public void Buffer_and_heap_entry_points_reach_the_same_verdict(
        LogProperty property, bool shouldForward)
    {
        var bufferInner = new KernelSpySink();
        var heapInner = new KernelSpySink();
        var bufferSink = new PredicateFilteredKernelSink(bufferInner, ExcludeTiming);
        var heapSink = new PredicateFilteredKernelSink(heapInner, ExcludeTiming);

        var buffer = BufferWith(property);
        bufferSink.Log(in buffer);
        heapSink.Log(EventWith(property));

        var expected = shouldForward ? 1 : 0;
        bufferInner.BufferLogCount.Should().Be(expected);
        heapInner.HeapLogCount.Should().Be(expected);
        bufferInner.BufferLogCount.Should().Be(heapInner.HeapLogCount,
            "the buffer hot path and the heap twin must never diverge on the admit/drop decision");
    }

    [Fact]
    public void Compact_property_buffer_is_filtered_identically()
    {
        // The helper scans CompactProperties too. Same predicate, compact path.
        var inner = new KernelSpySink();
        var sink = new PredicateFilteredKernelSink(inner, ExcludeTiming);

        // LogPropertyCompact carries a reference (RefValue), so it is a managed
        // type and cannot be stackalloc'd; a heap array is fine here — this test
        // pins the compact-path filter DECISION, not allocation.
        var props = new[] { LogPropertyCompact.From("Timing", 42) };
        var buffer = new LogEventBuffer(
            timeUtc: DateTimeOffset.UtcNow,
            level: KnownLogLevels.Information,
            category: LogCategory.App,
            messageTemplate: Template,
            message: "msg",
            compactProperties: props);

        sink.Log(in buffer);

        inner.BufferLogCount.Should().Be(0, "compact-path Timing must be excluded too");
    }

    [Fact]
    public void Constructor_rejects_non_kernel_inner()
    {
        var act = () => new PredicateFilteredKernelSink(new SpyLogger(), ExcludeTiming);
        act.Should().Throw<ArgumentException>();
    }

    // ── 0 B/op steady state ─────────────────────────────────────────────
    // The buffer is built fresh inside the measured action (a ref struct
    // cannot be captured by the probe's closure). Property values are
    // reference types, the predicate is a static lambda, and the helper scans
    // a stack span — so the filter decision itself must not allocate.

    [Fact]
    public void Buffer_filter_decision_is_zero_alloc()
    {
        var inner = new KernelSpySink();
        var sink = new PredicateFilteredKernelSink(inner, ExcludeTiming);
        var keep = new LogProperty("TenantId", "acme");

        var bytes = AllocationProbe.BytesPerIteration(() =>
        {
            var buffer = new LogEventBuffer(
                timeUtc: default,
                level: KnownLogLevels.Information,
                category: LogCategory.App,
                messageTemplate: Template,
                message: "msg",
                properties: new ReadOnlySpan<LogProperty>(in keep));
            sink.Log(in buffer);
        });

        bytes.Should().Be(0,
            "a span predicate with a static lambda must filter with no per-event allocation");
    }
}

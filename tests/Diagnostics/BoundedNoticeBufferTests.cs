#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using MMP.Herald.Diagnostics;
using Xunit;

namespace MMP.Herald.OSS.Tests.Diagnostics;

/// <summary>
/// Contract tests for the bounded buffer that backs both the
/// runtime-notice channel and the diagnostic failure sink.
/// </summary>
public sealed class BoundedNoticeBufferTests
{
    [Fact]
    public void Constructor_rejects_zero_and_negative_capacity()
    {
        Action zero = () => new BoundedNoticeBuffer<int>(0);
        Action negative = () => new BoundedNoticeBuffer<int>(-1);

        zero.Should().Throw<ArgumentOutOfRangeException>();
        negative.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Enqueue_below_capacity_appends_and_does_not_evict()
    {
        var buffer = new BoundedNoticeBuffer<int>(capacity: 4);
        for (var i = 1; i <= 3; i++) buffer.Enqueue(i);

        buffer.Snapshot().Should().Equal(new[] { 1, 2, 3 });
        buffer.DroppedCount.Should().Be(0);
    }

    [Fact]
    public void Enqueue_at_capacity_evicts_oldest_and_increments_dropped_count()
    {
        var buffer = new BoundedNoticeBuffer<int>(capacity: 3);
        for (var i = 1; i <= 5; i++) buffer.Enqueue(i);

        buffer.Snapshot().Should().Equal(new[] { 3, 4, 5 });
        buffer.DroppedCount.Should().Be(2, "5 enqueued minus 3 retained = 2 dropped");
    }

    [Fact]
    public void Snapshot_returns_fresh_array_each_call()
    {
        var buffer = new BoundedNoticeBuffer<int>(capacity: 4);
        buffer.Enqueue(1);

        var first = buffer.Snapshot();
        var second = buffer.Snapshot();

        first.Should().NotBeSameAs(second,
            "each snapshot must be independent so callers can iterate without affecting the buffer or each other");
        first.Should().Equal(second);
    }

    [Fact]
    public void Clear_empties_buffer_and_resets_dropped_count()
    {
        var buffer = new BoundedNoticeBuffer<int>(capacity: 2);
        for (var i = 0; i < 10; i++) buffer.Enqueue(i);
        buffer.DroppedCount.Should().Be(8);

        buffer.Clear();

        buffer.Snapshot().Should().BeEmpty();
        buffer.DroppedCount.Should().Be(0);
    }

    [Fact]
    public void Capacity_exposes_constructor_argument()
    {
        new BoundedNoticeBuffer<int>(capacity: 42).Capacity.Should().Be(42);
    }

    [Fact]
    public void Concurrent_writers_produce_correct_final_state()
    {
        const int writerCount = 8;
        const int writesPerWriter = 1000;
        var buffer = new BoundedNoticeBuffer<int>(capacity: 100);

        Parallel.For(0, writerCount, writer =>
        {
            for (var i = 0; i < writesPerWriter; i++)
            {
                buffer.Enqueue(writer * writesPerWriter + i);
            }
        });

        var total = writerCount * writesPerWriter;
        buffer.Snapshot().Count.Should().Be(100);
        buffer.DroppedCount.Should().Be(total - 100,
            "every write past the 100-slot capacity must increment dropped count");
    }

    // ---------- 0.2.3 additions ----------

    [Fact]
    public void OnEvicted_fires_with_evicted_item_when_capacity_exceeded()
    {
        var buffer = new BoundedNoticeBuffer<int>(capacity: 2);
        var evicted = new List<int>();
        buffer.OnEvicted += evicted.Add;

        buffer.Enqueue(1);
        buffer.Enqueue(2);
        buffer.Enqueue(3); // evicts 1
        buffer.Enqueue(4); // evicts 2

        evicted.Should().Equal(new[] { 1, 2 });
    }

    [Fact]
    public void OnEvicted_does_not_fire_when_buffer_has_room()
    {
        var buffer = new BoundedNoticeBuffer<int>(capacity: 4);
        var evicted = new List<int>();
        buffer.OnEvicted += evicted.Add;

        buffer.Enqueue(1);
        buffer.Enqueue(2);
        buffer.Enqueue(3);

        evicted.Should().BeEmpty();
    }

    [Fact]
    public void Throwing_OnEvicted_subscriber_is_swallowed_and_buffer_stays_consistent()
    {
        var buffer = new BoundedNoticeBuffer<int>(capacity: 1);
        buffer.OnEvicted += _ => throw new InvalidOperationException("subscriber bug");

        // The eviction triggers the throwing handler. Caller must
        // not observe the throw, and the buffer must reflect the
        // post-eviction state regardless.
        buffer.Enqueue(1);
        var act = () => buffer.Enqueue(2);

        act.Should().NotThrow();
        buffer.Snapshot().Should().Equal(new[] { 2 });
        buffer.DroppedCount.Should().Be(1);
    }

    [Fact]
    public void Capacity_one_evicts_on_every_subsequent_enqueue()
    {
        // G3.5 — boundary case: a single-slot buffer is the most
        // degenerate valid configuration. Every enqueue past the
        // first evicts the prior entry.
        var buffer = new BoundedNoticeBuffer<int>(capacity: 1);
        var evicted = new List<int>();
        buffer.OnEvicted += evicted.Add;

        buffer.Enqueue(1);
        buffer.Enqueue(2);
        buffer.Enqueue(3);

        buffer.Snapshot().Should().Equal(new[] { 3 });
        evicted.Should().Equal(new[] { 1, 2 });
        buffer.DroppedCount.Should().Be(2);
    }

    [Fact]
    public void Large_capacity_buffer_behaves_like_small_one_until_threshold()
    {
        // G3.5 — boundary case: a large buffer mustn't allocate
        // eagerly or behave specially at construction time.
        var buffer = new BoundedNoticeBuffer<int>(capacity: 10_000);
        for (var i = 0; i < 5_000; i++) buffer.Enqueue(i);

        buffer.Snapshot().Count.Should().Be(5_000);
        buffer.DroppedCount.Should().Be(0);
    }
}

#nullable enable

using System;
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
}

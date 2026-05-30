#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MMP.Herald.Addons.ManagementApi;
using MMP.Herald.Events;
using MMP.Herald.Failures;
using MMP.Herald.Levels;
using MMP.Herald.Templating;
using Xunit;

namespace MMP.Herald.OSS.Tests.Addons.ManagementApi;

/// <summary>
/// Pins the principal-review #13 triple fix for
/// <see cref="LiveLogCapture"/>:
///
/// (a) <c>_buffer</c> over-drain race: replaced with
///     <see cref="MMP.Herald.Diagnostics.BoundedNoticeBuffer{T}"/> so two
///     concurrent producers cannot overshoot the cap.
/// (b) <see cref="RejectedEventBroadcaster.OnRejected"/> leak: the capture
///     implements <see cref="IDisposable"/> and unsubscribes the handler
///     it registered.
/// (c) <c>_channel</c> reassignment race: <see cref="LiveLogSubscriber.Drain"/>
///     uses <see cref="Interlocked.Exchange{T}"/> and completes the OLD
///     channel after the swap.
/// </summary>
public sealed class LiveLogCaptureTripleFixTests
{
    [Fact]
    public void Buffer_caps_at_max_under_concurrent_producers()
    {
        // Fix (a): the pre-fix ConcurrentQueue + manual over-drain loop
        // raced. Bounded buffer keeps the invariant under contention.
        const int maxBuffer = 100;
        using var capture = new LiveLogCapture(maxBufferSize: maxBuffer);

        const int threads = 8;
        const int perThread = 500;
        var barrier = new Barrier(threads);

        Parallel.For(0, threads, _ =>
        {
            barrier.SignalAndWait();
            for (var i = 0; i < perThread; i++)
            {
                capture.Log(MakeEvent($"event-{i}"));
            }
        });

        capture.GetRecent(maxBuffer * 2).Count.Should().BeLessThanOrEqualTo(maxBuffer,
            "the bounded buffer must never carry more than its configured cap, even under racing producers");
    }

    [Fact]
    public void Dispose_unsubscribes_from_rejected_broadcaster()
    {
        // Fix (b): the pre-fix code subscribed on construction and never
        // unsubscribed. Each capture instance leaked one handler entry. The
        // fix captures the delegate and removes it in Dispose. Verify by
        // creating a capture, disposing it, then publishing a rejection —
        // the capture's buffer must NOT receive the entry.
        var capture = new LiveLogCapture();
        capture.Dispose();

        RejectedEventBroadcaster.Publish(MakeEvent("post-dispose"), reason: "level");

        // Snapshot must be empty: the rejection went out to broadcaster
        // subscribers, but the disposed capture is no longer one.
        capture.GetRecent(50).Should().BeEmpty(
            "Dispose must unsubscribe from RejectedEventBroadcaster.OnRejected");
    }

    [Fact]
    public async Task Subscriber_Drain_completes_old_channel_and_swaps_new()
    {
        // Fix (c): Drain must atomically swap channels and complete the OLD
        // one, so a reader on the old channel sees ChannelClosedException
        // and a writer post-swap lands on the new channel.
        using var capture = new LiveLogCapture();
        using var sub = capture.Subscribe();

        var readerBeforeDrain = sub.Reader;

        // Push one entry through the live channel so the subscriber has
        // something to drain. Drain the entry off the reader first so the
        // post-drain Completion task can resolve — channel Completion fires
        // only after the channel is both completed AND fully read.
        capture.Log(MakeEvent("pre-drain"));
        readerBeforeDrain.TryRead(out _);

        sub.Drain();

        // Reader reference is a NEW reader after swap — same subscriber
        // instance, different underlying channel.
        var readerAfterDrain = sub.Reader;
        readerAfterDrain.Should().NotBeSameAs(readerBeforeDrain,
            "Drain must replace the channel so post-drain reads land on the fresh one");

        // The OLD channel's reader observes completion (ReadAsync returns
        // ChannelClosedException OR the read loop ends cleanly).
        var completionTask = readerBeforeDrain.Completion;
        var completedInTime = await Task.WhenAny(completionTask, Task.Delay(TimeSpan.FromSeconds(2)));
        completedInTime.Should().Be(completionTask,
            "the OLD channel must complete after Drain so SSE consumers exit their read loop");

        // Post-drain log must reach the fresh channel.
        capture.Log(MakeEvent("post-drain"));
        var hasItem = readerAfterDrain.TryRead(out var _);
        hasItem.Should().BeTrue("post-drain emits must land on the fresh channel");
    }

    private static LogEvent MakeEvent(string message)
    {
        return new LogEvent(
            DateTimeOffset.UtcNow,
            KnownLogLevels.Information,
            LogCategory.App,
            message,
            message,
            LogEvent.EmptyProperties,
            LogEvent.EmptyContext);
    }
}

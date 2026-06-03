// Copyright (c) 2026 MMPWorks LLC
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.
#nullable enable

using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using MMP.Herald.Diagnostics;

namespace MMP.Herald.OSS.Tests.Helpers;

/// <summary>
/// Test-only spin helpers for the deferred naming-policy announcement publish.
/// The first-dispatch announcement queues itself on the thread pool so the
/// emitting hot path does not pay the runtime-message cost; the publish lands
/// "soon after" but not synchronously. Tests poll for the publish to arrive
/// instead of racing on the read.
///
/// <para>
/// The helpers take the <see cref="HeraldRuntimeMessagesInstance"/> to poll as a
/// parameter rather than reading the process-wide static
/// <c>HeraldRuntimeMessages</c> buffer. With per-host channel routing each test
/// builds its logger on its own channel and asserts on that channel's buffer, so
/// a sibling test's deferred announcement cannot land in the buffer under
/// assertion — the isolation that retired the shared-buffer corral. See
/// docs/design/announcement-channel-per-host-routing.md.
/// </para>
/// </summary>
internal static class AnnouncementSpinHelpers
{
    /// <summary>
    /// Default poll budget. 2 seconds is generous for a thread-pool work
    /// item; any test that exceeds this is observing a real defect, not
    /// a slow CI machine.
    /// </summary>
    public const int DefaultTimeoutMs = 2000;

    /// <summary>
    /// Spin until <paramref name="predicate"/> returns true or the budget
    /// elapses. Returns true on success, false on timeout.
    /// </summary>
    public static bool SpinUntil(Func<bool> predicate, int timeoutMs = DefaultTimeoutMs)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (predicate()) return true;
            Thread.Sleep(1);
        }
        return predicate();
    }

    /// <summary>
    /// Block the test thread until at least one naming-policy announcement
    /// has landed on <paramref name="channel"/>. The announcement message
    /// always starts with "Herald active naming policy:" — that's the stable
    /// signature used to identify it among unrelated notices.
    /// </summary>
    public static void WaitForAnnouncement(
        HeraldRuntimeMessagesInstance channel, int timeoutMs = DefaultTimeoutMs)
    {
        SpinUntil(
            () => channel.RecentNotices.Any(n =>
                n.Message.StartsWith("Herald active naming policy:", StringComparison.Ordinal)),
            timeoutMs);
    }

    /// <summary>
    /// Block the test thread until at least <paramref name="count"/>
    /// announcements have landed on <paramref name="channel"/>.
    /// </summary>
    public static void WaitForAnnouncements(
        HeraldRuntimeMessagesInstance channel, int count, int timeoutMs = DefaultTimeoutMs)
    {
        SpinUntil(
            () => channel.RecentNotices
                .Count(n => n.Message.StartsWith("Herald active naming policy:", StringComparison.Ordinal)) >= count,
            timeoutMs);
    }
}

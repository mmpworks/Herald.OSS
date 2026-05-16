#nullable enable

using System;
using System.Collections.Generic;
using MMP.Herald.Templating;

namespace MMP.Herald.Diagnostics;

/// <summary>
/// Instance-scoped runtime-notice channel. Each
/// <see cref="MMP.Herald.Quick.HeraldHost"/> owns one, and the static
/// <see cref="HeraldRuntimeMessages"/> facade forwards to
/// <see cref="MMP.Herald.Quick.HeraldHost.Default"/>'s instance.
///
/// <para>
/// <b>Why per-host.</b> Two parallel test classes that each subscribe
/// to a process-wide static event would observe each other's notices
/// — the same trap the named-pipeline registry refactor closed by
/// moving the map onto a per-host instance. Multi-tenant deployments
/// that construct one host per tenant get tenant-scoped channels for
/// free.
/// </para>
///
/// <para>
/// <b>Subscriber isolation.</b> A throwing subscriber MUST NOT take
/// down the framework code that published the notice. The publish
/// path snapshots the invocation list, dispatches each subscriber
/// inside its own try/catch, and swallows caught exceptions. A
/// subscriber that throws stops only itself — every other subscriber
/// still receives the notice, and the user's hot-path code that
/// triggered the publish never sees the exception.
/// </para>
///
/// <para>
/// <b>Buffer semantics.</b> Recent notices are retained on a
/// bounded <see cref="BoundedNoticeBuffer{T}"/> so a diagnostic
/// dashboard polling after the fact can show the last N entries
/// without subscribing in real time. The buffer's
/// <see cref="BoundedNoticeBuffer{T}.DroppedCount"/> is exposed so a
/// viewer can tell when older notices were evicted.
/// </para>
/// </summary>
public sealed class HeraldRuntimeMessagesInstance
{
    /// <summary>
    /// Default capacity for the recent-notices buffer when the
    /// caller doesn't supply one. Sized for diagnostic dashboard
    /// polling: a per-second poll on a 64-slot ring sees roughly
    /// the last minute of notice traffic at one-notice-per-second
    /// publishing.
    /// </summary>
    public const int DefaultBufferCapacity = 64;

    private readonly BoundedNoticeBuffer<RuntimeNotice> _buffer;

    public HeraldRuntimeMessagesInstance(int capacity = DefaultBufferCapacity)
    {
        _buffer = new BoundedNoticeBuffer<RuntimeNotice>(capacity);
    }

    /// <summary>
    /// Fires for every runtime notice published through this
    /// instance. Subscribers are dispatched on the publishing thread.
    /// A throwing subscriber is caught and swallowed — its exception
    /// never propagates back into framework code that fired the
    /// notice, and every other subscriber on the invocation list
    /// still receives the notice.
    /// </summary>
    public event Action<RuntimeNotice>? OnNotice;

    /// <summary>
    /// Oldest-first snapshot of buffered notices. Useful for
    /// diagnostic surfaces that poll after the fact (admin
    /// dashboards, support tooling). Each call returns a fresh
    /// array so the buffer can keep mutating without affecting the
    /// returned view.
    /// </summary>
    public IReadOnlyList<RuntimeNotice> RecentNotices => _buffer.Snapshot();

    /// <summary>
    /// Number of notices evicted from <see cref="RecentNotices"/>
    /// because the buffer was full at publish time. Use this to
    /// tell a viewer they're not seeing all-of-history.
    /// </summary>
    public long DroppedNoticeCount => _buffer.DroppedCount;

    /// <summary>Empty the recent-notices buffer. Used by tests for isolation.</summary>
    public void ClearRecent() => _buffer.Clear();

    /// <summary>
    /// Publish a runtime notice. Called from inside framework code
    /// when a notice-worthy event occurs (naming-policy
    /// announcement, hot-reload status, etc.). Public so a
    /// downstream commercial wrapper can publish its own framework-
    /// tier notices through the same channel without re-implementing
    /// the buffer + subscriber pattern.
    /// </summary>
    public void Publish(string source, string message, IReadOnlyList<LogProperty>? properties = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        var notice = new RuntimeNotice(
            TimeUtc: DateTimeOffset.UtcNow,
            Source: source,
            Message: message,
            Properties: properties ?? Array.Empty<LogProperty>(),
            GenSource: HeraldGenSource.RuntimeNotice);

        _buffer.Enqueue(notice);

        // Snapshot the invocation list so a throwing or unsubscribing
        // handler cannot terminate the dispatch chain. Each subscriber
        // runs inside its own try/catch — framework code never
        // propagates a third-party exception back into the user's hot
        // path. A subscriber that throws stops only itself.
        var handler = OnNotice;
        if (handler is null) return;
        foreach (var sub in handler.GetInvocationList())
        {
            try { ((Action<RuntimeNotice>)sub).Invoke(notice); }
            catch
            {
                // Swallowed by contract. Documented on OnNotice: a
                // throwing subscriber is the subscriber's bug, not
                // the framework's problem to bubble up.
            }
        }
    }
}

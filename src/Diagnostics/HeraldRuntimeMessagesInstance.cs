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
        // Forward buffer-level evictions onto the instance-level event so
        // subscribers can observe both routes (buffer-level for any T,
        // instance-level for RuntimeNotice specifically) without
        // depending on the buffer type.
        _buffer.OnEvicted += FireNoticeDropped;
    }

    private void FireNoticeDropped(RuntimeNotice evicted)
    {
        var handler = OnNoticeDropped;
        if (handler is null) return;
        foreach (var sub in handler.GetInvocationList())
        {
            try { ((Action<RuntimeNotice>)sub).Invoke(evicted); }
            catch { /* swallowed — eviction notification is not subscriber-dependent */ }
        }
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
    /// Fires when a notice is evicted from <see cref="RecentNotices"/>
    /// because the buffer was full. Mirrors <see cref="OnNotice"/>'s
    /// shape: synchronous dispatch on the publishing thread, throwing
    /// handlers swallowed. A subscriber that wants to diagnose a
    /// chatty publisher uses this to see which notices were lost.
    /// </summary>
    public event Action<RuntimeNotice>? OnNoticeDropped;

    /// <summary>
    /// Optional fallback subscriber. Invoked when <see cref="Publish"/>
    /// finds no subscribers on <see cref="OnNotice"/> — gives a host
    /// that hasn't wired live observation a place for unwatched
    /// notices to land (typically <c>Trace.WriteLine</c> or stderr).
    /// Default <c>null</c> = current silent behavior. Mirrors the
    /// kernel-failure-sink fallback pattern: when no subscriber is
    /// listening, the framework still has somewhere to put the
    /// signal so it isn't lost.
    /// </summary>
    public Action<RuntimeNotice>? FallbackSubscriber { get; set; }

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
    /// Re-raise an already-published notice on this instance's
    /// <see cref="OnNotice"/> WITHOUT enqueuing it into this instance's buffer.
    /// The aggregation hub
    /// (<see cref="RuntimeNoticeAggregator"/>) uses this on the default host's
    /// channel so an operator subscribed to the static
    /// <see cref="HeraldRuntimeMessages.OnNotice"/> facade observes notices
    /// published on every non-default host — while the default host's BUFFER
    /// (<see cref="RecentNotices"/>) stays free of other hosts' notices. That
    /// buffer isolation is what kills the parallel-test bleed: tests assert on
    /// the buffer, not on a live <see cref="OnNotice"/> subscription.
    ///
    /// <para>
    /// Same loud-non-throwing contract as <see cref="Publish"/>: the invocation
    /// list is snapshotted so a subscriber that unsubscribes mid-dispatch cannot
    /// break the chain, and each subscriber runs in its own try/catch so a
    /// throwing handler stops only itself. Does NOT touch the buffer, does NOT
    /// re-stamp the notice, and does NOT consult <see cref="FallbackSubscriber"/>
    /// — a re-raise with no live subscriber is simply a no-op (the originating
    /// host already buffered and fell back as needed).
    /// </para>
    /// </summary>
    internal void RaiseOnNoticeWithoutBuffering(RuntimeNotice notice)
    {
        var handler = OnNotice;
        if (handler is null) return;

        foreach (var sub in handler.GetInvocationList())
        {
            try { ((Action<RuntimeNotice>)sub).Invoke(notice); }
            catch { /* same swallow-throwing-subscriber contract as Publish */ }
        }
    }

    /// <summary>
    /// Publish a runtime notice at <see cref="NoticeSeverity.Info"/>.
    /// Forwards to the severity-explicit overload — kept for source
    /// compatibility with pre-0.2.3 callers.
    /// </summary>
    public void Publish(string source, string message, IReadOnlyList<LogProperty>? properties = null) =>
        Publish(source, message, NoticeSeverity.Info, properties);

    /// <summary>
    /// Publish a runtime notice with an explicit severity. Called
    /// from inside framework code when a notice-worthy event occurs
    /// (naming-policy announcement, hot-reload status, etc.). Public
    /// so a downstream commercial wrapper can publish its own
    /// framework-tier notices through the same channel without
    /// re-implementing the buffer + subscriber pattern.
    /// </summary>
    public void Publish(
        string source,
        string message,
        NoticeSeverity severity,
        IReadOnlyList<LogProperty>? properties = null) =>
        Publish(source, message, severity, tenant: null, properties);

    /// <summary>
    /// Publish a runtime notice carrying an explicit tenant
    /// attribution. Multi-tenant hosts use this so a per-tenant
    /// dashboard subscriber can filter notices to the tenant whose
    /// framework code emitted them. <paramref name="tenant"/> may
    /// be <c>null</c>, which is equivalent to the legacy overloads.
    /// </summary>
    public void Publish(
        string source,
        string message,
        NoticeSeverity severity,
        string? tenant,
        IReadOnlyList<LogProperty>? properties = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        // No null-guard on severity: NoticeSeverity is a non-null sealed record and
        // every caller passes a static instance (Info/Warning/Error). A guard here
        // would be dead code.

        var notice = new RuntimeNotice(
            TimeUtc: DateTimeOffset.UtcNow,
            Source: source,
            Message: message,
            Properties: properties ?? Array.Empty<LogProperty>(),
            GenSource: HeraldGenSource.RuntimeNotice,
            Severity: severity,
            Tenant: tenant);

        _buffer.Enqueue(notice);

        // Snapshot the invocation list so a throwing or unsubscribing
        // handler cannot terminate the dispatch chain. Each subscriber
        // runs inside its own try/catch — framework code never
        // propagates a third-party exception back into the user's hot
        // path. A subscriber that throws stops only itself.
        var handler = OnNotice;
        if (handler is null)
        {
            // No live subscribers — fall back if the host wired one,
            // otherwise the notice lives in RecentNotices only.
            var fallback = FallbackSubscriber;
            if (fallback is not null)
            {
                try { fallback(notice); }
                catch { /* same contract as OnNotice */ }
            }
            return;
        }

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

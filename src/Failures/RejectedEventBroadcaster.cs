#nullable enable

using System;
using MMP.Herald.Events;

namespace MMP.Herald.Failures;

/// <summary>
/// Process-wide pub/sub for events the pipeline rejected before they
/// reached a sink. Filters and other gatekeepers publish here when
/// they drop an event; the dashboard's <c>LiveLogCapture</c>
/// subscribes so its SSE stream can surface rejected entries
/// alongside the accepted ones.
///
/// <para>The shape mirrors the existing <c>LoopbackEventBus</c>: a
/// single static event, no per-pipeline scoping. That keeps the
/// publisher's call site allocation-free (one delegate invoke when a
/// subscriber is wired, a null-check no-op otherwise) and avoids
/// threading a new sink reference through every filter constructor.
/// Per-pipeline routing, when needed, can layer on top by including
/// the pipeline name in the published payload.</para>
///
/// <para>Reason values come from <see cref="DropReasons"/> — the same
/// vocabulary the metrics path uses, so a "rejected: level" badge in
/// the dashboard reads identically to the
/// <c>herald_sink_drops_total{reason="level"}</c> Prometheus row.</para>
/// </summary>
public static class RejectedEventBroadcaster
{
    /// <summary>
    /// Subscribe to rejection notifications. Each subscriber receives
    /// the original <see cref="LogEvent"/> plus the reason string.
    /// </summary>
    public static event Action<LogEvent, string>? OnRejected;

    /// <summary>Fire all subscribers. Null-check is a single branch when nobody listens.</summary>
    public static void Publish(LogEvent logEvent, string reason)
    {
        OnRejected?.Invoke(logEvent, reason);
    }
}

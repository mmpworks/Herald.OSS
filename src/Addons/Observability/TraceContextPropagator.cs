#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace MMP.Herald.Addons.Observability;

/// <summary>
/// W3C TraceContext propagator for distributed trace correlation.
/// Injects and extracts W3C traceparent/tracestate headers so trace context
/// flows across service boundaries (HTTP, message queues, RPC).
///
/// Herald's ActivityEnricher captures TraceId/SpanId from the current Activity,
/// but that only works within one process. This propagator bridges the gap:
/// the sending service calls Inject() to serialize trace context into headers,
/// and the receiving service calls Extract() to restore it.
///
/// Usage — sending side (HTTP client):
///   var headers = new Dictionary&lt;string, string&gt;();
///   TraceContextPropagator.Inject(headers);
///   httpRequest.Headers.Add("traceparent", headers["traceparent"]);
///
/// Usage — receiving side (HTTP handler):
///   var traceparent = request.Headers["traceparent"];
///   using var activity = TraceContextPropagator.Extract(traceparent);
///   // Activity.Current now has the upstream TraceId — ActivityEnricher
///   // will attach it to all log events in this scope.
///
/// W3C traceparent format: "00-{traceId}-{spanId}-{flags}"
///   Version: 00 (fixed)
///   TraceId: 32 hex chars (128-bit)
///   SpanId:  16 hex chars (64-bit)
///   Flags:   02 hex chars (01 = sampled)
/// </summary>
public static class TraceContextPropagator
{
    private static readonly ActivitySource _source = new("MMP.Herald");

    /// <summary>
    /// Inject the current Activity's trace context into a dictionary as W3C headers.
    /// Call this before sending an HTTP request, message, or RPC call.
    /// No-ops if no Activity is current.
    /// </summary>
    public static void Inject(IDictionary<string, string> headers)
    {
        ArgumentNullException.ThrowIfNull(headers);

        var activity = Activity.Current;
        if (activity is null)
            return;

        // W3C traceparent: 00-{traceId}-{spanId}-{flags}
        var flags = activity.Recorded ? "01" : "00";
        headers["traceparent"] = $"00-{activity.TraceId}-{activity.SpanId}-{flags}";

        if (!string.IsNullOrEmpty(activity.TraceStateString))
            headers["tracestate"] = activity.TraceStateString;
    }

    /// <summary>
    /// Extract a W3C traceparent header and create a new Activity that continues
    /// the upstream trace. The returned Activity becomes Activity.Current, so
    /// ActivityEnricher will automatically attach the upstream TraceId/SpanId
    /// to all log events in this scope.
    ///
    /// Dispose the returned Activity when the request/operation is complete.
    /// Returns null if the traceparent is null, empty, or malformed.
    /// </summary>
    public static Activity? Extract(string? traceparent, string? tracestate = null)
    {
        if (string.IsNullOrWhiteSpace(traceparent))
            return null;

        // Parse: "00-{traceId}-{spanId}-{flags}"
        var parts = traceparent.Split('-');
        if (parts.Length < 4)
            return null;

        var traceId = parts[1];
        var parentSpanId = parts[2];
        var flags = parts[3];

        if (traceId.Length != 32 || parentSpanId.Length != 16)
            return null;

        var activity = _source.StartActivity(
            "Herald.IncomingRequest",
            ActivityKind.Server,
            new ActivityContext(
                ActivityTraceId.CreateFromString(traceId),
                ActivitySpanId.CreateFromString(parentSpanId),
                flags == "01" ? ActivityTraceFlags.Recorded : ActivityTraceFlags.None));

        if (activity is not null && !string.IsNullOrEmpty(tracestate))
            activity.TraceStateString = tracestate;

        return activity;
    }

    /// <summary>
    /// Start a new root trace. Use when your service is the entry point and
    /// no upstream traceparent exists.
    /// </summary>
    public static Activity? StartTrace(string operationName = "Herald.Operation")
    {
        return _source.StartActivity(operationName, ActivityKind.Internal);
    }

    /// <summary>
    /// Format the current Activity as a W3C traceparent string.
    /// Returns null if no Activity is current.
    /// </summary>
    public static string? CurrentTraceparent()
    {
        var activity = Activity.Current;
        if (activity is null) return null;
        var flags = activity.Recorded ? "01" : "00";
        return $"00-{activity.TraceId}-{activity.SpanId}-{flags}";
    }
}

#nullable enable

using System;
using System.Diagnostics;
using MMP.Herald.Events;

namespace MMP.Herald.Enrichers;

/// <summary>
/// Enriches log events with the current Activity's trace and span IDs.
/// No-ops when no Activity is present on the calling thread.
/// Requires opt-in via includeActivityContext in configuration.
/// </summary>
public sealed class ActivityEnricher : ILogEnricher
{
    public void Enrich(LogEventEnrichmentContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var activity = Activity.Current;

        if (activity is null)
        {
            return;
        }

        context.SetContextValue(Services.LogContextKeys.TraceId, activity.TraceId.ToString());
        context.SetContextValue(Services.LogContextKeys.SpanId, activity.SpanId.ToString());
    }
}

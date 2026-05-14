#nullable enable

using System;
using System.Threading;
using MMP.Herald.Events;
using MMP.Herald.Enrichers;

namespace MMP.Herald.Addons.Compliance;

/// <summary>
/// Assigns a monotonically increasing sequence number to every log event.
/// Thread-safe via Interlocked.Increment. Useful for:
/// - Compliance (PCI-DSS event ordering guarantees)
/// - Debugging (detect lost or reordered events)
/// - Correlation (match events across sinks)
/// </summary>
public sealed class SequenceNumberEnricher : ILogEnricher
{
    private long _counter;

    public void Enrich(LogEventEnrichmentContext context) {
        ArgumentNullException.ThrowIfNull(context);
        context.SetContextValue(Services.LogContextKeys.SequenceNumber, Interlocked.Increment(ref _counter));
    }
}

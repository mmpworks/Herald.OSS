#nullable enable

using MMP.Herald.Events;

namespace MMP.Herald.Enrichers;

/// <summary>
/// No-op enricher.
/// </summary>
public sealed class NullLogEnricher : ILogEnricher
{
    public static NullLogEnricher Instance { get; } = new();

    public void Enrich(LogEventEnrichmentContext context)
    {
    }
}
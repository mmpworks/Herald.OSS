#nullable enable

using System;
using System.Collections.Generic;
using MMP.Herald.Events;

namespace MMP.Herald.Enrichers;

/// <summary>
/// Runs registered enrichers in order.
/// </summary>
public sealed class CompositeLogEnricher : ILogEnricher
{
    private readonly IReadOnlyList<ILogEnricher> _enrichers;

    public CompositeLogEnricher(params ILogEnricher[] enrichers)
    {
        ArgumentNullException.ThrowIfNull(enrichers);
        _enrichers = enrichers;
    }

    public void Enrich(LogEventEnrichmentContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        foreach (var enricher in _enrichers)
        {
            enricher.Enrich(context);
        }
    }

    /// <summary>Number of inner enrichers. Zero means this composite is a no-op.</summary>
    public int Count => _enrichers.Count;

    /// <summary>The inner enricher list. Exposed for kernel-pipeline
    /// introspection — kernel eligibility inspects each inner enricher to
    /// check for <c>IKernelEnricher</c>.</summary>
    public IReadOnlyList<ILogEnricher> Inner => _enrichers;
}
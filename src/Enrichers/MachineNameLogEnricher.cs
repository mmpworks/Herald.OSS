#nullable enable

using System;
using MMP.Herald.Events;

namespace MMP.Herald.Enrichers;

/// <summary>
/// Adds the current machine name to log context.
/// </summary>
public sealed class MachineNameLogEnricher : ILogEnricher
{
    public void Enrich(LogEventEnrichmentContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.SetContextValue(Services.LogContextKeys.MachineName, Environment.MachineName);
    }
}
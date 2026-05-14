#nullable enable

using System;
using System.Diagnostics;
using MMP.Herald.Events;

namespace MMP.Herald.Enrichers;

/// <summary>
/// Adds the current process id to log context.
/// </summary>
public sealed class ProcessIdLogEnricher : ILogEnricher
{
    // Lazy so the expensive Process.GetCurrentProcess().ProcessName call only happens
    // when the enricher is actually used, not when the type is loaded.
    private static readonly Lazy<(int Id, string Name)> CachedProcessInfo = new(() =>
        (Environment.ProcessId, Process.GetCurrentProcess().ProcessName));

    public void Enrich(LogEventEnrichmentContext context) {
        ArgumentNullException.ThrowIfNull(context);
        var info = CachedProcessInfo.Value;
        context.SetContextValue(Services.LogContextKeys.ProcessId, info.Id);
        context.SetContextValue(Services.LogContextKeys.ProcessName, info.Name);
    }
}
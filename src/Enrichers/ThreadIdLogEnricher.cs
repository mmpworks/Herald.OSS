#nullable enable

using System;
using System.Threading;
using MMP.Herald.Events;

namespace MMP.Herald.Enrichers;

/// <summary>
/// Adds the managed thread id to log context.
/// </summary>
public sealed class ThreadIdLogEnricher : ILogEnricher
{
    public void Enrich(LogEventEnrichmentContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.SetContextValue(Services.LogContextKeys.ThreadId, Environment.CurrentManagedThreadId);
        context.SetContextValue(Services.LogContextKeys.IsThreadPoolThread, Thread.CurrentThread.IsThreadPoolThread);
    }
}
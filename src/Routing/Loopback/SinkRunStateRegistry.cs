#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using MMP.Herald.Configuration.Runtime;

namespace MMP.Herald.Routing.Loopback;

/// <summary>
/// Process-wide registry of <see cref="SinkRunStateHolder"/>s keyed by
/// (pipeline, sink) so the management-API PATCH endpoint can find the
/// holder for a given sink and flip its state without holding any
/// pipeline-internal references.
///
/// <para>The router factory registers a holder for every sink it wraps
/// during pipeline construction. When a pipeline rebuilds (e.g. a
/// commit), the previous holders for that pipeline are removed via
/// <see cref="ClearPipeline"/> before the new ones land — that keeps
/// stale entries from accumulating across iterations.</para>
///
/// <para>Lookups are lock-free; writes happen on pipeline construction
/// and on PATCH calls, both of which are infrequent compared to the
/// per-event read on the holder itself.</para>
/// </summary>
public static class SinkRunStateRegistry
{
    private static readonly ConcurrentDictionary<string, SinkRunStateHolder> _holders =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Register (or replace) the holder for one sink. The router
    /// factory calls this once per sink per pipeline build.
    /// </summary>
    public static void Register(string pipelineName, string sinkName, SinkRunStateHolder holder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipelineName);
        ArgumentException.ThrowIfNullOrWhiteSpace(sinkName);
        ArgumentNullException.ThrowIfNull(holder);
        _holders[Key(pipelineName, sinkName)] = holder;
    }

    /// <summary>Look up a holder. Returns null when no match.</summary>
    public static SinkRunStateHolder? Get(string pipelineName, string sinkName) =>
        _holders.TryGetValue(Key(pipelineName, sinkName), out var holder) ? holder : null;

    /// <summary>
    /// Remove every holder for a pipeline. Called by the router
    /// factory at the start of a pipeline build so leftover holders
    /// from a previous build do not point at disposed sinks.
    /// </summary>
    public static void ClearPipeline(string pipelineName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipelineName);
        var prefix = pipelineName + "/";
        var toRemove = new List<string>();
        foreach (var key in _holders.Keys)
        {
            if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                toRemove.Add(key);
        }
        foreach (var key in toRemove)
            _holders.TryRemove(key, out _);
    }

    private static string Key(string pipelineName, string sinkName) =>
        pipelineName + "/" + sinkName;
}

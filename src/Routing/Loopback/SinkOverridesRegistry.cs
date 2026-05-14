#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace MMP.Herald.Routing.Loopback;

/// <summary>
/// Process-wide registry of <see cref="SinkOverridesHolder"/>s keyed
/// by (pipeline, sink). Same shape as
/// <see cref="SinkRunStateRegistry"/>: the router factory registers a
/// holder for every sink it wraps; the management API's PATCH
/// endpoints look up the holder and mutate its fields without
/// touching the pipeline.
///
/// <para>Lookups are lock-free; writes happen on pipeline construction
/// and on PATCH calls, both infrequent compared to the per-event
/// reads on the holder itself.</para>
/// </summary>
public static class SinkOverridesRegistry
{
    private static readonly ConcurrentDictionary<string, SinkOverridesHolder> _holders =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Register (or replace) the holder for one sink. The router
    /// factory calls this once per sink per pipeline build.
    /// </summary>
    public static void Register(string pipelineName, string sinkName, SinkOverridesHolder holder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipelineName);
        ArgumentException.ThrowIfNullOrWhiteSpace(sinkName);
        ArgumentNullException.ThrowIfNull(holder);
        _holders[Key(pipelineName, sinkName)] = holder;
    }

    /// <summary>Look up a holder. Returns null when no match.</summary>
    public static SinkOverridesHolder? Get(string pipelineName, string sinkName) =>
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

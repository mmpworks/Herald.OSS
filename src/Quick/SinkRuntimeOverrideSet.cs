// Copyright (c) 2026 MMP LLC
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.
#nullable enable

using System;
using System.Collections.Generic;
using MMP.Herald.Configuration.Runtime;

namespace MMP.Herald.Quick;

/// <summary>
/// Per-builder map of sink-id → <see cref="SinkRuntimeOverride"/>.
/// The dashboard's per-sink strip (run state + tees + sink-level
/// minimum) writes into this map through the management API; the
/// JSON serializer reads from it when persisting the pipeline so
/// every operator choice survives a reboot.
///
/// <para><b>Single source of truth.</b> The runtime holders
/// (<c>SinkOverridesHolder</c>, <c>SinkRunStateHolder</c>) keep the
/// per-event hot-path lock-free, but they reset on every pipeline
/// rebuild. This map is the durable mirror — writes funnel through
/// the management API, which updates both the holder (for instant
/// effect) and the map (for persistence).</para>
/// </summary>
public sealed class SinkRuntimeOverrideSet
{
    private readonly Dictionary<string, SinkRuntimeOverride> _bySinkId =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Replace the override snapshot for one sink. Used by the JSON
    /// restore path: when <c>RestoreBuilderFromConfig</c> reads a
    /// saved sink that carries <c>runState</c> / <c>teeLiveToFile</c>
    /// / <c>teeLiveToUrl</c>, it lands here so the next serializer
    /// pass writes the same values out again.
    /// </summary>
    public void Set(string sinkId, SinkRuntimeOverride snapshot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sinkId);
        ArgumentNullException.ThrowIfNull(snapshot);
        _bySinkId[sinkId] = snapshot;
    }

    /// <summary>
    /// Merge incoming non-null fields onto the existing snapshot for
    /// <paramref name="sinkId"/>. Used by the management API's PATCH
    /// funnel: a single-field PATCH leaves untouched fields alone.
    /// Returns the updated snapshot.
    /// </summary>
    public SinkRuntimeOverride Merge(string sinkId, SinkRuntimeOverride incoming)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sinkId);
        ArgumentNullException.ThrowIfNull(incoming);

        var current = _bySinkId.TryGetValue(sinkId, out var existing)
            ? existing
            : new SinkRuntimeOverride();
        var merged = current.MergeWith(incoming);
        _bySinkId[sinkId] = merged;
        return merged;
    }

    /// <summary>
    /// Look up the recorded snapshot for one sink, or <c>null</c>
    /// when nothing has been written for it yet (defaults apply).
    /// </summary>
    public SinkRuntimeOverride? Get(string sinkId) =>
        _bySinkId.TryGetValue(sinkId, out var snapshot) ? snapshot : null;

    /// <summary>
    /// Sink-id keys with at least one override recorded. The
    /// serializer iterates this only as a hint — it walks the
    /// emitted sinks itself and asks <see cref="Get"/> per sink.
    /// </summary>
    public IReadOnlyCollection<string> SinkIds => _bySinkId.Keys;

    /// <summary>Discard every recorded snapshot.</summary>
    public void Clear() => _bySinkId.Clear();
}

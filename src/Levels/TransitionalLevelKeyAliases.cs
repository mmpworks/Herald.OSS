#nullable enable

// TRANSITIONAL — scaffolding for the Serilog rename wave. REMOVED in Task 9.
// Do not build new behaviour on this; it exists only so old keys resolve mid-sweep.
using System;
using System.Collections.Generic;

namespace MMP.Herald.Levels;

/// <summary>
/// Maps the pre-rename level keys to their Serilog-aligned successors so that
/// inbound config-load / wire-ingest keys resolve correctly while the rename
/// sweep is in flight. This is throwaway scaffolding removed in Task 9 — do not
/// build new behaviour on it.
/// </summary>
internal static class TransitionalLevelKeyAliases
{
    private static readonly IReadOnlyDictionary<string, string> OldToNew =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["info"] = "information",
            ["warn"] = "warning",
            ["critical"] = "fatal",
            ["trace"] = "verbose",
        };

    /// <summary>
    /// Returns the canonical (post-rename) key for <paramref name="key"/>.
    /// Old keys map to their successor; everything else is lowercased and passed
    /// through unchanged so new keys and extras (notice/success/...) are stable.
    /// </summary>
    public static string Canonicalize(string key)
        => OldToNew.TryGetValue(key, out var canonical) ? canonical : key.ToLowerInvariant();
}

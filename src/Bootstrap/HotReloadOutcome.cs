#nullable enable

namespace MMP.Herald.Bootstrap;

/// <summary>
/// Result of <see cref="HotReloadableLoggingBootstrap.Reload(string)"/>.
///
/// <para>
/// Pre-fix the method returned <c>void</c> and silently dropped the JSON
/// when a reload was already in flight. Two file-watcher events landing
/// inside one slow rebuild caused the second edit to disappear without a
/// trace. The outcome surface lets callers tell the three states apart so
/// the file-watcher path can record-and-retry instead of guessing.
/// </para>
/// </summary>
public enum HotReloadOutcome
{
    /// <summary>
    /// The JSON was applied to the live pipeline. Returned for both the
    /// fast level-only path and the slow full-rebuild path.
    /// </summary>
    Applied,

    /// <summary>
    /// A reload was already in flight; this JSON has been recorded as the
    /// next-to-apply and the in-flight reload will pick it up when it
    /// finishes. Most-recent-wins: a later <see cref="Deferred"/> overwrites
    /// an earlier one, so operators see the latest config and not a stale
    /// queue replay.
    /// </summary>
    Deferred,

    /// <summary>
    /// The bootstrap is disposed; no reload was attempted. Callers in a
    /// shutdown path can use this to short-circuit cleanup work.
    /// </summary>
    Skipped,
}

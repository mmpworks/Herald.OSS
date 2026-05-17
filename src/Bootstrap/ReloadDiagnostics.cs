#nullable enable

using System;

namespace MMP.Herald.Bootstrap;

/// <summary>
/// Payload for the <see cref="HotReloadableLoggingBootstrap.OnReloadCompleted"/>
/// and <see cref="HotReloadableLoggingBootstrap.OnReloadFailed"/> events.
///
/// <para>
/// Operators inspecting "did my edit take effect" no longer need to poll
/// <see cref="HotReloadableLoggingBootstrap.CurrentMinimumLevel"/> and
/// infer. Subscribing once at startup gives a deterministic per-reload
/// callback with outcome, source path, duration, and the exception when
/// the reload failed. Dashboards, audit-log writers, and chaos-test
/// harnesses all use the same shape.
/// </para>
///
/// <para>
/// <see cref="Outcome"/> mirrors <see cref="HotReloadOutcome"/> for the
/// completed event so subscribers can distinguish a slow rebuild
/// (<c>Applied</c>) from a Deferred-then-drained run. Failed reloads
/// always carry <see cref="HotReloadOutcome.Applied"/> for the attempted
/// outcome and a non-null <see cref="Exception"/>.
/// </para>
/// </summary>
/// <param name="Outcome">The reload outcome at the time the event fired.</param>
/// <param name="Path">
/// The configuration source path, when known. Null for direct
/// <see cref="HotReloadableLoggingBootstrap.Reload(string)"/> calls that
/// did not originate from a file or named source.
/// </param>
/// <param name="DurationMs">
/// Wall-clock milliseconds the reload took, measured from the start of
/// <see cref="HotReloadableLoggingBootstrap.Reload(string)"/> (inside the
/// reload lock) to the moment the slow-path swap or fast-path level
/// update completed.
/// </param>
/// <param name="Exception">
/// The terminating exception for <see cref="HotReloadableLoggingBootstrap.OnReloadFailed"/>,
/// null for <see cref="HotReloadableLoggingBootstrap.OnReloadCompleted"/>.
/// </param>
public sealed record ReloadDiagnostics(
    HotReloadOutcome Outcome,
    string? Path,
    long DurationMs,
    Exception? Exception = null);

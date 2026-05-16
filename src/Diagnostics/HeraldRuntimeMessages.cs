#nullable enable

using System;
using System.Collections.Generic;
using MMP.Herald.Quick;
using MMP.Herald.Templating;

namespace MMP.Herald.Diagnostics;

/// <summary>
/// Process-wide static facade over the
/// <see cref="HeraldHost.Default"/>'s
/// <see cref="HeraldRuntimeMessagesInstance"/>. Every call forwards
/// to that instance — the same pattern <see cref="HeraldRegistry"/>
/// uses to give the process a single well-known surface while
/// keeping the actual state on a per-host instance.
///
/// <para>
/// <b>Per-host scoping.</b> Tests and multi-tenant hosts that need
/// channel isolation construct their own <see cref="HeraldHost"/>
/// and use <c>host.RuntimeMessages</c> directly — the static facade
/// here is for the single-host common case. Two parallel test
/// classes that touch <see cref="OnNotice"/> on the static facade
/// share the same invocation list; tests that need isolation grab
/// a fresh host.
/// </para>
///
/// <para>
/// <b>Why this channel exists at all.</b> Framework code that wants
/// to emit a diagnostic signal (naming-policy announcement, hot-
/// reload status, plugin-load warning) MUST NOT route through the
/// user's logging pipeline — doing so puts framework lines in the
/// consumer's tenant-scoped sinks, breaking the wall the consumer
/// built. This channel is the parallel surface: framework code
/// publishes here; consumers who want diagnostic visibility
/// subscribe; the user pipeline stays clean.
/// </para>
/// </summary>
public static class HeraldRuntimeMessages
{
    /// <summary>The default host's runtime-message instance. Forwards happen through this.</summary>
    public static HeraldRuntimeMessagesInstance Default => HeraldHost.Default.RuntimeMessages;

    /// <summary>
    /// Fires for every runtime notice published through the default
    /// host. Throwing subscribers are caught and swallowed so a
    /// buggy handler cannot propagate into framework code. See
    /// <see cref="HeraldRuntimeMessagesInstance.OnNotice"/> for the
    /// full contract.
    /// </summary>
    public static event Action<RuntimeNotice> OnNotice
    {
        add => Default.OnNotice += value;
        remove => Default.OnNotice -= value;
    }

    /// <summary>Oldest-first snapshot of buffered notices on the default host.</summary>
    public static IReadOnlyList<RuntimeNotice> RecentNotices => Default.RecentNotices;

    /// <summary>
    /// Notices dropped from <see cref="RecentNotices"/> because the
    /// buffer was full at publish time. See
    /// <see cref="HeraldRuntimeMessagesInstance.DroppedNoticeCount"/>.
    /// </summary>
    public static long DroppedNoticeCount => Default.DroppedNoticeCount;

    /// <summary>Empty the default host's recent-notices buffer.</summary>
    public static void ClearRecent() => Default.ClearRecent();

    /// <summary>
    /// Publish a runtime notice through the default host. See
    /// <see cref="HeraldRuntimeMessagesInstance.Publish"/> for the
    /// full contract.
    /// </summary>
    public static void Publish(string source, string message, IReadOnlyList<LogProperty>? properties = null) =>
        Default.Publish(source, message, properties);
}

/// <summary>
/// One runtime notice published through
/// <see cref="HeraldRuntimeMessages"/>. <see cref="GenSource"/>
/// always carries <see cref="HeraldGenSource.RuntimeNotice"/> so a
/// consumer who later bridges runtime notices back into their
/// pipeline can preserve the provenance marker and let any
/// downstream gate filter on it.
/// </summary>
public sealed record RuntimeNotice(
    DateTimeOffset TimeUtc,
    string Source,
    string Message,
    IReadOnlyList<LogProperty> Properties,
    string GenSource);

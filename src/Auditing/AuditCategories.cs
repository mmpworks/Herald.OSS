#nullable enable

using MMP.Herald.Events;

namespace MMP.Herald.Auditing;

/// <summary>
/// Canonical log categories for administrative actions a Herald host
/// performs. The category names share the <c>audit.</c> prefix so a
/// single category-prefix filter lights up the entire audit channel
/// without enumerating each one.
///
/// <para>The audit-emission pattern is:
/// <list type="number">
///   <item>An admin endpoint completes its mutation (run-state flipped,
///         plugin approved, license verified, etc.).</item>
///   <item>The endpoint emits a structured event to one of the
///         categories below via the standard <see cref="ILogger"/>
///         surface, carrying the relevant actor + before + after as
///         properties.</item>
///   <item>An operator-configured sink (file, OTLP, Splunk, anything)
///         consumes events whose category starts with <c>audit.</c>
///         and writes the audit trail.</item>
/// </list>
/// </para>
///
/// <para>The library does not auto-attach a default audit sink. The
/// emission contract is fixed (these constants); the destination is
/// the operator's choice.</para>
/// </summary>
public static class AuditCategories
{
    /// <summary>Common prefix for every audit category.</summary>
    public const string Prefix = "audit";

    /// <summary>Sink run-state mutations (Disabled / Live / Test).</summary>
    public static readonly LogCategory RunState = new("audit.runstate");

    /// <summary>Plugin-trust workflow: approve, dismiss, default action set.</summary>
    public static readonly LogCategory PluginTrust = new("audit.plugin-trust");

    /// <summary>Pipeline registry mutations: register, unregister, import, commit.</summary>
    public static readonly LogCategory Registry = new("audit.registry");

    /// <summary>License verification outcomes at startup and on reload.</summary>
    public static readonly LogCategory License = new("audit.license");

    /// <summary>Authentication events: token issue, login failure.</summary>
    public static readonly LogCategory Auth = new("audit.auth");

    /// <summary>Configuration mutations: minimum level, sink globals, custom levels.</summary>
    public static readonly LogCategory Config = new("audit.config");
}

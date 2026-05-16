#nullable enable

namespace MMP.Herald.Diagnostics;

/// <summary>
/// Severity dimension on a <see cref="RuntimeNotice"/>. Rank-based
/// comparison so a subscriber can ask "is this at least Warning?"
/// without enumerating cases.
///
/// <para>
/// Same shape as <see cref="MMP.Herald.HeraldEdition"/> — a sealed
/// record with named static instances and a single
/// <see cref="Includes"/> method. The pattern is: small surface,
/// total ordering, additive — a future tier (e.g. Critical) is one
/// new static instance, not a breaking change.
/// </para>
///
/// <para>
/// Default severity for runtime notices is <see cref="Info"/>.
/// Subscribers that route on severity (pager on Error, dashboard on
/// Warning, debug console on Info) check via <c>Includes</c>:
/// <c>notice.Severity.Includes(NoticeSeverity.Warning)</c> is true
/// for Warning and Error notices, false for Info.
/// </para>
/// </summary>
public sealed record NoticeSeverity(string Name, int Rank)
{
    /// <summary>
    /// Routine framework signal. The naming-policy announcement, hot-
    /// reload success, plugin-load success.
    /// </summary>
    public static readonly NoticeSeverity Info = new("Info", 0);

    /// <summary>
    /// Framework noticed something off but the system still works.
    /// Hot-reload kept the prior policy because the new one was
    /// invalid; a plugin failed to load but other plugins are fine;
    /// a config setting was clamped to a safe value.
    /// </summary>
    public static readonly NoticeSeverity Warning = new("Warning", 1);

    /// <summary>
    /// Framework can't recover. Configuration validation failed at
    /// boot; a load-bearing plugin couldn't initialise.
    /// </summary>
    public static readonly NoticeSeverity Error = new("Error", 2);

    /// <summary>
    /// True when this severity is at least the requested level.
    /// Used by subscribers that route on severity tier.
    /// </summary>
    public bool Includes(NoticeSeverity required) => Rank >= required.Rank;

    public override string ToString() => Name;
}

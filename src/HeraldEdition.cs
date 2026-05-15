#nullable enable

namespace MMP.Herald;

/// <summary>
/// Informational edition tag for components. Carries the upstream
/// Herald.Core edition designation so the Dashboard and tooling can
/// render edition badges and group features by tier, but
/// <b>Herald.OSS does not enforce</b> the tag at runtime — there is no
/// gate in the OSS distribution. Every component is reachable in OSS
/// regardless of the value it declares.
///
/// <para>
/// The three editions Herald.Core uses downstream:
/// </para>
/// <list type="bullet">
///   <item><b>Community</b> — free, open-source core (this edition).</item>
///   <item><b>Pro</b> — adds resilience and advanced pipeline features.</item>
///   <item><b>Enterprise</b> — full platform with compliance, plugins, and game-perf addons.</item>
/// </list>
///
/// <para>
/// Rank enables simple comparison: <c>current.Includes(required)</c>.
/// The Dashboard and Herald.Sinks plugin authors use the tag as
/// metadata; Herald.Core's edition gate is what actually denies
/// registration on a lower edition. In Herald.OSS the comparison
/// helper is available for completeness but no caller in the OSS
/// build actually rejects a component based on it.
/// </para>
/// </summary>
public sealed record HeraldEdition(string Name, int Rank)
{
    public static readonly HeraldEdition Community  = new("Community", 0);
    public static readonly HeraldEdition Pro        = new("Pro", 1);
    public static readonly HeraldEdition Enterprise = new("Enterprise", 2);

    /// <summary>True if this edition includes features at the given tier.</summary>
    public bool Includes(HeraldEdition required) => Rank >= required.Rank;

    public override string ToString() => Name;
}

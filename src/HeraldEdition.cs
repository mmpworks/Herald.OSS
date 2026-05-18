#nullable enable

namespace MMP.Herald;

/// <summary>
/// Identifies the minimum Herald edition required to use a feature.
///
/// Herald ships in three editions:
///   Community  — free, open-source core
///   Pro        — paid, adds resilience and advanced pipeline features
///   Enterprise — full platform with compliance, plugins, and game-perf addons
///
/// Each component carries a <see cref="Routing.ILogSinkProvider.MinimumEdition"/>
/// so the Dashboard can show edition badges and a downstream commercial
/// wrapper can refuse to register sinks above the running tier.
///
/// <para>
/// In Herald.OSS this type is informational only — OSS enforces nothing.
/// The value travels alongside the sink provider so an Enterprise host
/// reading the registry can compare <c>provider.MinimumEdition</c> against
/// its own current edition without modifying OSS source.
/// </para>
///
/// Rank enables simple comparison: <c>current.Includes(required)</c>.
/// </summary>
public sealed record HeraldEdition(string Name, int Rank)
{
    public static readonly HeraldEdition Community  = new("Community", 0);
    public static readonly HeraldEdition Pro        = new("Pro", 1);
    public static readonly HeraldEdition Enterprise = new("Enterprise", 2);

    /// <summary>
    /// Developer-mode watermark: a paid Herald module is loaded but no valid
    /// commercial license token has been registered. Paid modules call
    /// HeraldVersion.SetEdition(HeraldEdition.Dev) from their initializer
    /// when this state is detected; all sink output gets a debug tag injected
    /// by LoopbackInterceptor until the user registers a real license.
    /// <para>
    /// Dev is a presentation/watermark state, NOT an authorization tier
    /// rank. Rank -1 is the conventional placement for "less than real tier"
    /// in any Rank-based comparison (so <c>Pro.Includes(Dev)</c> returns
    /// true under the existing monotone-rank semantics, which is correct —
    /// a Pro user with valid license still sees no watermark because Dev
    /// is never set when the license is valid).
    /// </para>
    /// </summary>
    public static readonly HeraldEdition Dev = new("Dev", -1);

    /// <summary>True if this edition includes features at the given tier.</summary>
    public bool Includes(HeraldEdition required) => Rank >= required.Rank;

    public override string ToString() => Name;
}

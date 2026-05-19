#nullable enable

using System;

namespace MMP.Herald;

/// <summary>
/// Runtime query API for paid features that want to gate themselves at
/// runtime against <see cref="HeraldVersion.CurrentEdition"/>.
///
/// <para>
/// Two enforcement styles are supported because gates split into two
/// natural categories:
/// </para>
///
/// <list type="bullet">
///   <item>
///     <c>Require</c> — used at code-path gates where reaching the gate with
///     the wrong edition is a coding mistake (the caller invoked an overload
///     their edition does not support). Throws
///     <see cref="HeraldEditionRequirementException"/> so the mistake
///     surfaces with an actionable message.
///   </item>
///   <item>
///     <c>AllowsFeature</c> — used for operator-config gates where a higher
///     edition unlocks a feature but a lower edition should not crash.
///     Returns a bool so the caller can warn and fall back gracefully.
///   </item>
/// </list>
///
/// <para>
/// This is the read-side query that consumes the result of whatever paid
/// module advertised the edition via <see cref="HeraldVersion.SetEdition(HeraldEdition)"/>.
/// Any authoritative license validation lives elsewhere; this gate trusts
/// the edition the running process reports.
/// </para>
/// </summary>
public static class HeraldEditionGate
{
    /// <summary>
    /// Throws <see cref="HeraldEditionRequirementException"/> when the current
    /// edition does not include <paramref name="minimum"/>. Use for API
    /// overloads that simply do not make sense below a given edition.
    /// </summary>
    public static void Require(HeraldEdition minimum, string featureName)
    {
        RequireOn(HeraldVersion.CurrentEdition, minimum, featureName);
    }

    /// <summary>
    /// Non-throwing variant used by soft-fallback paths. Returns
    /// <c>true</c> when the current edition meets the minimum.
    /// </summary>
    public static bool AllowsFeature(HeraldEdition minimum)
    {
        ArgumentNullException.ThrowIfNull(minimum);
        return HeraldVersion.CurrentEdition.Includes(minimum);
    }

    // Internal overload for unit tests: drives the gate against any
    // edition without touching the process-global HeraldVersion. The
    // public method delegates here so production callers exercise the
    // same logic path.
    internal static void RequireOn(HeraldEdition current, HeraldEdition minimum, string featureName)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(minimum);
        ArgumentException.ThrowIfNullOrWhiteSpace(featureName);

        if (current.Includes(minimum)) return;

        throw new HeraldEditionRequirementException(featureName, minimum, current);
    }
}

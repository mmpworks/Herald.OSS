#nullable enable

using System;
using MMP.Herald.Services;

namespace MMP.Herald.Configuration;

/// <summary>
/// Single source of truth for the Community-tier Flight Recorder lock:
/// the 200-event ring with an <c>error</c>-triggered drain. The decorator
/// itself is Community (<see cref="Addons.GamePerformance.FlightRecorderLogger.MinEdition"/>
/// is <see cref="HeraldEdition.Community"/>), but configurability —
/// changing <c>bufferSize</c>, <c>minimumLevel</c>, or <c>triggerLevel</c>
/// away from the canonical shape — is gated to Pro and above.
///
/// <para>
/// Configuration entry points (<c>QuickLogBuilder.WithFlightRecorder</c>,
/// the JSON config mapper, and the management API) all route through
/// <see cref="RequireConfigurabilityEdition"/> so the gate stays consistent
/// across the three surfaces.
/// </para>
/// </summary>
public static class FlightRecorderCommunityShape
{
    /// <summary>The Community-locked buffer size. Pro/Enterprise unlocks arbitrary sizes.</summary>
    public const int BufferSize = 200;

    /// <summary>
    /// Returns <c>true</c> when the requested configuration matches the
    /// Community lock: 200-event ring, no explicit minimum-level override,
    /// and a null-or-<c>"error"</c> trigger level. Level strings are
    /// compared case-insensitively because the level registry is.
    /// </summary>
    public static bool Matches(int bufferSize, string? minimumLevel, string? triggerLevel) =>
        bufferSize == BufferSize
        && string.IsNullOrEmpty(minimumLevel)
        && (string.IsNullOrEmpty(triggerLevel)
            || string.Equals(triggerLevel, LogLevelKeys.Error, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Throw <see cref="InvalidOperationException"/> via
    /// <see cref="HeraldEditionGate.Require"/> when the requested
    /// configuration deviates from the Community-locked shape. The
    /// canonical 200/error configuration is silently allowed.
    /// </summary>
    public static void RequireConfigurabilityEdition(
        int bufferSize,
        string? minimumLevel,
        string? triggerLevel)
    {
        if (Matches(bufferSize, minimumLevel, triggerLevel)) return;

        HeraldEditionGate.Require(
            HeraldEdition.Pro,
            "Flight Recorder configurability (non-default bufferSize / minimumLevel / triggerLevel)");
    }

    // Internal overload for unit tests: lets the gate run against an
    // explicit edition instead of the compile-time HeraldVersion. Mirrors
    // HeraldEditionGate.RequireOn so tests can exercise both the pass and
    // throw branches deterministically.
    internal static void RequireConfigurabilityEditionOn(
        HeraldEdition currentEdition,
        int bufferSize,
        string? minimumLevel,
        string? triggerLevel)
    {
        if (Matches(bufferSize, minimumLevel, triggerLevel)) return;

        HeraldEditionGate.RequireOn(
            currentEdition,
            HeraldEdition.Pro,
            "Flight Recorder configurability (non-default bufferSize / minimumLevel / triggerLevel)");
    }
}

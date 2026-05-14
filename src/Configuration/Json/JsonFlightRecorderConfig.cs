#nullable enable

namespace MMP.Herald.Configuration.Json;

/// <summary>
/// JSON-facing configuration for the flight-recorder ring buffer.
///
/// <para>
/// When enabled, events below <see cref="MinimumLevel"/> are buffered in a
/// ring buffer instead of passing through. When an event at or above
/// <see cref="TriggerLevel"/> arrives, the buffer drains to the inner
/// pipeline before the trigger event itself, giving you the debug trail
/// leading up to the failure.
/// </para>
///
/// <para>
/// Both level fields are optional: <c>null</c> means "inherit". The mapper
/// resolves null <see cref="MinimumLevel"/> against the pipeline's minimum
/// level, and null <see cref="TriggerLevel"/> against <c>"error"</c>.
/// </para>
/// </summary>
public sealed record JsonFlightRecorderConfig(
    bool Enabled = false,
    int BufferSize = 200,
    string? MinimumLevel = null,
    string? TriggerLevel = null);

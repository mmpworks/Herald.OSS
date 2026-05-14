#nullable enable

using MMP.Herald.Levels;

namespace MMP.Herald.Configuration;

/// <summary>
/// Runtime policy for the flight-recorder ring buffer. Carries the resolved
/// <see cref="LogLevel"/> values plus the buffer capacity. Built by
/// <see cref="DefaultLoggingConfigurationMapper"/> from
/// <see cref="Json.JsonFlightRecorderConfig"/> and consumed by the
/// flight-recorder step handler at pipeline-build time.
/// </summary>
public sealed record FlightRecorderPolicy(
    int BufferSize,
    LogLevel MinimumLevel,
    LogLevel TriggerLevel);

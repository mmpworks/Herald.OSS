#nullable enable

using System.Collections.Generic;

namespace MMP.Herald.Quick.Serializers.Steps;

/// <summary>
/// Config serializer for the <c>"flightRecorder"</c> pipeline step. Mirrors
/// the fields set by <see cref="QuickLogBuilder.WithFlightRecorder"/>. When
/// the builder has not enabled the recorder, the dictionary still emits the
/// default-shape entries so the dashboard renders the same fields whether
/// the step is dormant or live.
/// </summary>
internal sealed class FlightRecorderStepConfigSerializer : IStepConfigSerializer
{
    public string StepName => "flightRecorder";

    public Dictionary<string, object?>? BuildConfig(QuickLogBuilder builder) => new()
    {
        ["enabled"] = builder.FlightRecorderEnabled,
        ["bufferSize"] = builder.FlightRecorderBufferSize,
        ["minimumLevel"] = builder.FlightRecorderMinLevel,
        ["triggerLevel"] = builder.FlightRecorderTriggerLevel,
    };
}

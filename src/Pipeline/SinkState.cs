#nullable enable

namespace MMP.Herald.Pipeline;

/// <summary>
/// Health state of a sink.
/// </summary>
public sealed record SinkState(string Value)
{
    /// <summary>Sink is operating normally with no recent failures.</summary>
    public static SinkState Healthy { get; } = new("Healthy");

    /// <summary>Sink has recent failures but is still accepting events.</summary>
    public static SinkState Degraded { get; } = new("Degraded");

    /// <summary>Sink is down. Circuit breaker is open or sink is unreachable.</summary>
    public static SinkState Unhealthy { get; } = new("Unhealthy");

    public override string ToString() => Value;
}

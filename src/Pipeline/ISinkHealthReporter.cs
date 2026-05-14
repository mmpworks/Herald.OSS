#nullable enable

namespace MMP.Herald.Pipeline;

/// <summary>
/// Reports the health status of a sink or sink wrapper (e.g., circuit breaker).
/// Callers can query health to monitor pipeline degradation.
/// </summary>
public interface ISinkHealthReporter
{
    SinkHealthStatus GetHealthStatus();
}

#nullable enable

using System;
using MMP.Herald.Events;

namespace MMP.Herald.Pipeline;

/// <summary>
/// Thrown when a circuit breaker is in the Open state and rejects an event.
/// Caught by FallbackLogger to redirect to a fallback sink, and by
/// SafeCompositeLogger to report to the failure sink without crashing.
/// </summary>
public sealed class CircuitBreakerOpenException : Exception
{
    public string SinkName { get; }
    public LogEvent RejectedEvent { get; }

    public CircuitBreakerOpenException(string sinkName, LogEvent rejectedEvent)
        : base($"Circuit breaker '{sinkName}' is open. Event rejected.") {
        SinkName = sinkName;
        RejectedEvent = rejectedEvent;
    }
}

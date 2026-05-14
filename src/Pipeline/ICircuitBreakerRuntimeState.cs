#nullable enable

namespace MMP.Herald.Pipeline;

/// <summary>
/// Read-only runtime-state surface for the circuit-breaker decorator.
/// Lets management-API code in Core query the live state of a circuit
/// breaker WITHOUT a compile-time dependency on the concrete logger
/// type, which lives in the <c>Herald.Pro</c> plugin assembly after
/// the Step 2b decoupling.
///
/// <para>
/// The decorator implements this interface so that
/// <c>PipelineAccessor.Get&lt;ICircuitBreakerRuntimeState&gt;()</c>
/// resolves to the same instance the strategy wired into the pipeline.
/// When the Pro plugin is not loaded, the accessor returns null and
/// the management endpoint reports "circuit breaker not configured."
/// </para>
///
/// <para>
/// All members must be lock-free reads (<c>Volatile.Read</c> or
/// equivalent) so the management API can sample state on any thread
/// without coordinating with the worker.
/// </para>
/// </summary>
public interface ICircuitBreakerRuntimeState
{
    /// <summary>
    /// Current state of the breaker, encoded as a small int so the
    /// interface stays free of an enum that would also have to live
    /// in Core. 0 = Closed, 1 = Open, 2 = HalfOpen.
    /// </summary>
    int CurrentState { get; }

    /// <summary>
    /// Count of consecutive failures observed since the last successful
    /// delivery. Reset to zero on success.
    /// </summary>
    int ConsecutiveFailures { get; }
}

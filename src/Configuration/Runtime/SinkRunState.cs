#nullable enable

namespace MMP.Herald.Configuration.Runtime;

/// <summary>
/// Operational state of a sink at runtime. Each sink in a pipeline
/// carries its own <see cref="SinkRunState"/> independent of the kind
/// it implements, so an operator can quiet one sink (e.g. Splunk) while
/// keeping the others writing normally.
///
/// <list type="bullet">
///   <item><b>Disabled.</b> Unconditional short-circuit. The sink does
///         not receive the event at all; loopback flags are not
///         consulted. Equivalent in behaviour to deleting the sink
///         from the pipeline, except the configuration stays
///         in place so the operator can flip back to <c>Live</c>
///         without re-entering connection details.</item>
///   <item><b>Live.</b> Normal operation: the event reaches the sink.
///         When <see cref="LoggingRuntimeSinkDefinition.TeeLiveToFile"/>
///         is set AND the pipeline carries a <c>TestLoopbackLogDir</c>,
///         a copy is also teed to that file. Same pairing for
///         <see cref="LoggingRuntimeSinkDefinition.TeeLiveToUrl"/>
///         and the pipeline's <c>TestLoopbackUrl</c>. The two flags
///         are the per-sink opt-in: a noisy sink can stay quiet on
///         the loopbacks even when its pipeline has loopbacks
///         configured.</item>
///   <item><b>Test.</b> The real send is suppressed. The two
///         live-loopback booleans do <i>not</i> apply in test mode —
///         every loopback the pipeline defined receives the event.
///         That keeps the dashboard's "let me peek at the traffic
///         this sink would generate" workflow simple: flip to test,
///         watch whichever loopback is configured. The operator
///         inspects the file or the URL receiver to confirm the
///         events look right before flipping back to <c>Live</c>.</item>
/// </list>
///
/// <para>The default for new sinks is <see cref="Live"/> so that
/// existing pipeline JSON files load with their previous behaviour
/// when the runtime config is rebuilt against this version.</para>
/// </summary>
public enum SinkRunState
{
    /// <summary>Drop every event immediately. No real send, no loopback.</summary>
    Disabled = 0,

    /// <summary>Normal operation. Loopback flags decide whether copies are also sent.</summary>
    Live = 1,

    /// <summary>Suppress the real send. Events flow only through active loopback channels.</summary>
    Test = 2,
}

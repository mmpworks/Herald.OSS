#nullable enable

using System;
using MMP.Herald.Events;

namespace MMP.Herald.Failures;

/// <summary>
/// Thrown when an audit-mode sink fails to deliver a log event.
/// Audit sinks are configured for critical compliance paths where silent failure is unacceptable.
/// The failure is already reported to ILogFailureSink before this exception is thrown.
/// </summary>
public sealed class AuditLogFailureException : Exception
{
    public AuditLogFailureException(string sinkName, LogEvent failedEvent, Exception innerException)
        : base($"Audit sink '{sinkName}' failed to deliver log event.", innerException)
    {
        SinkName = sinkName;
        FailedEvent = failedEvent;
    }

    public string SinkName { get; }
    public LogEvent FailedEvent { get; }
}

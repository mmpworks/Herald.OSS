#nullable enable

using System;
using MMP.Herald.Events;

namespace MMP.Herald.Failures;
/// <summary>
/// Default no-op failure sink.
/// </summary>
public sealed class NullLogFailureSink : ILogFailureSink
{
    public static NullLogFailureSink Instance { get; } = new();

    public void ReportFailure(LogEvent logEvent, Exception exception, string source)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
    }

    /// <summary>
    /// Overridden so a caller on the new overload does no work at all. The
    /// default forwarding implementation would still unpack the failure.
    /// </summary>
    public void ReportFailure(LogFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
    }
}
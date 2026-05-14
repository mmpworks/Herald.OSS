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
}
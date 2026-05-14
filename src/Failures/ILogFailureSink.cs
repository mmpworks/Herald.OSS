#nullable enable

using System;
using MMP.Herald.Events;

namespace MMP.Herald.Failures;
/// <summary>
/// Receives failures that occur while the logging pipeline emits an event.
/// </summary>
public interface ILogFailureSink
{
    void ReportFailure(LogEvent logEvent, Exception exception, string source);
}
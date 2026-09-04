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

    /// <summary>
    /// Receive a failure that already carries its code, its retryable flag, and
    /// a correlation id. Prefer this overload: the classification is made once,
    /// at the report site, instead of once per consumer.
    ///
    /// <para>
    /// The default implementation forwards to
    /// <see cref="ReportFailure(LogEvent, Exception, string)"/> and drops the
    /// added fields, so a sink written before this overload existed keeps
    /// compiling and keeps working. Override it to read the fields.
    /// </para>
    /// </summary>
    void ReportFailure(LogFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        ReportFailure(failure.LogEvent, failure.Exception, failure.Source);
    }
}

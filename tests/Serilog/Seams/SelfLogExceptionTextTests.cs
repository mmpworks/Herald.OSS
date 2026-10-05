using System;
using System.Collections.Generic;
using FluentAssertions;
using MMP.Herald.Serilog;
using MMP.Herald.Serilog.Core;
using MMP.Herald.Serilog.Debugging;
using MMP.Herald.Serilog.Events;
using Xunit;

namespace MMP.Herald.OSS.Tests.Serilog.Seams;

/// <summary>
/// A WriteTo sink that throws is reported through SelfLog. The SelfLog line names the sink and the exception
/// type, never the exception message, which can quote the value the sink failed on.
/// </summary>
// SelfLog is process-global: share the non-parallel collection the other SelfLog tests use.
[Collection(nameof(AuditVsWriteFailureTests))]
public sealed class SelfLogExceptionTextTests
{
    private const string Ssn = "999-12-3456";

    [Fact]
    public void SelfLog_line_for_a_failing_sink_carries_no_exception_text()
    {
        var lines = new List<string>();
        SelfLog.Enable(line => { lock (lines) lines.Add(line); });
        try
        {
            var log = new LoggerConfiguration().WriteTo.Sink(new QuotingSink()).CreateLogger();
            log.Information("patient lookup");
        }
        finally
        {
            SelfLog.Disable();
        }

        lines.Should().Contain(l => l.Contains("QuotingSink") && l.Contains("FormatException"));
        lines.Should().NotContain(l => l.Contains(Ssn));
    }

    private sealed class QuotingSink : ILogEventSink
    {
        public void Emit(LogEvent logEvent) =>
            throw new FormatException($"The input string '{Ssn}' was not in a correct format.");
    }
}

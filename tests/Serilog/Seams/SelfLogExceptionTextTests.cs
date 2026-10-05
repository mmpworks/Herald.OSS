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

    [Fact]
    public void SelfLog_line_for_a_failing_sub_logger_route_carries_no_exception_text()
    {
        // A route (WriteTo.Logger / WriteTo.Map) whose Accept throws with the raw value in the message.
        var lines = new List<string>();
        var later = new CollectingSink();
        var logger = new MMP.Herald.Serilog.Sinks.SerilogUserLogger(
            new List<ILogEventSink> { later }, new List<ILogEventSink>(),
            new List<MMP.Herald.Serilog.Sinks.ISubLoggerRoute> { new QuotingRoute() }, applicator: null);
        SelfLog.Enable(line => { lock (lines) lines.Add(line); });
        try
        {
            logger.Log(new MMP.Herald.Events.LogEvent(DateTimeOffset.UtcNow, MMP.Herald.Levels.KnownLogLevels.Information,
                MMP.Herald.Events.LogCategory.App, "patient lookup", "patient lookup",
                MMP.Herald.Events.LogEvent.EmptyProperties, MMP.Herald.Events.LogEvent.EmptyContext));
        }
        finally
        {
            SelfLog.Disable();
        }

        lines.Should().Contain(l => l.Contains("sub-logger route QuotingRoute: FormatException"));
        lines.Should().NotContain(l => l.Contains(Ssn));
        later.Count.Should().Be(1);
    }

    private sealed class QuotingRoute : MMP.Herald.Serilog.Sinks.ISubLoggerRoute
    {
        public void Accept(MMP.Herald.Events.LogEvent logEvent) =>
            throw new FormatException($"The input string '{Ssn}' was not in a correct format.");

        public System.Threading.Tasks.ValueTask DisposeAsync() => default;
    }

    [Fact]
    public void Throwing_SelfLog_writer_does_not_stop_later_sinks()
    {
        var collected = new CollectingSink();
        SelfLog.Enable(_ => throw new InvalidOperationException("self-log writer is broken"));
        try
        {
            var log = new LoggerConfiguration().WriteTo.Sink(new QuotingSink()).WriteTo.Sink(collected).CreateLogger();
            var act = () => log.Information("patient lookup");
            act.Should().NotThrow();
        }
        finally
        {
            SelfLog.Disable();
        }

        collected.Count.Should().Be(1);
    }

    private sealed class CollectingSink : ILogEventSink
    {
        public int Count;
        public void Emit(LogEvent logEvent) => System.Threading.Interlocked.Increment(ref Count);
    }

    private sealed class QuotingSink : ILogEventSink
    {
        public void Emit(LogEvent logEvent) =>
            throw new FormatException($"The input string '{Ssn}' was not in a correct format.");
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using FluentAssertions;
using MMP.Herald.Addons.BinarySerialization;
using MMP.Herald.Events;
using MMP.Herald.Failures;
using MMP.Herald.Levels;
using MMP.Herald.Pipeline;
using MMP.Herald.Templating;
using Xunit;

namespace MMP.Herald.OSS.Tests.Events;

/// <summary>
/// A property value whose <c>ToString</c> throws, with the raw value in the exception message. Each event
/// factory path (fast, standard, deferred), the template-error fallback on each path, real serialization, and
/// the diagnostic failure sink are covered. Every test asserts the SSN appears nowhere and nothing throws out.
/// </summary>
public sealed class ThrowingValueTests
{
    private const string Ssn = "999-12-3456";

    private static LogEventFactory Factory() => new(
        new MMP.Herald.Time.SystemDateTimeProvider(),
        new MessageTemplateParser(),
        new AsyncLocalLogScopeProvider(),
        new MMP.Herald.Enrichers.NullLogEnricher());

    private static readonly Dictionary<string, object?> CallerContext = new() { ["RequestId"] = "r-1" };

    private static LogProperty Hostile(LogPropertyVisibility? visibility = null) =>
        new("Value", new ThrowsOnToString(), Visibility: visibility);

    private static string ValueText(LogEvent e) => e.Properties.Single(p => p.Name == "Value").Value as string ?? "<not a string>";

    // Fast path (no context): the factory replaces the value before rendering, and the event serializes.
    [Fact]
    public void Fast_path_replaces_a_throwing_value_and_the_event_serializes()
    {
        var e = Factory().Create(KnownLogLevels.Information, LogCategory.App, "Patient {Value}", new[] { Hostile() });

        ValueText(e).Should().Be("[Property 'Value' ToString threw FormatException]");
        e.Message.Should().NotContain(Ssn);

        var bytes = Encoding.UTF8.GetBytes(new MessagePackLogFormatter(LogLevelRegistry.CreateDefault()).Format(e));
        Encoding.UTF8.GetString(bytes).Should().NotContain(Ssn);
    }

    // Standard path (caller context): PiiSensitive forces ToString in the eager resolver.
    [Fact]
    public void Standard_path_pii_value_with_a_throwing_ToString_never_escapes()
    {
        var act = () => Factory().Create(KnownLogLevels.Information, LogCategory.App, "Patient {Value}",
            new[] { Hostile(LogPropertyVisibility.PiiSensitive) }, context: CallerContext);

        var e = act.Should().NotThrow().Subject;
        ValueText(e).Should().NotContain(Ssn).And.Contain("FormatException");
        e.Message.Should().NotContain(Ssn);
    }

    // Deferred path: no size check runs first, so the eager resolver's ToString is the only guard.
    [Fact]
    public void Deferred_path_pii_value_with_a_throwing_ToString_never_escapes()
    {
        var factory = new DeferredLogEventFactory(new MMP.Herald.Time.SystemDateTimeProvider(), new AsyncLocalLogScopeProvider(),
            new MMP.Herald.Enrichers.NullLogEnricher());

        var act = () => factory.Create(KnownLogLevels.Information, LogCategory.App, "Patient {Value}",
            new[] { Hostile(LogPropertyVisibility.PiiSensitive) });

        var e = act.Should().NotThrow().Subject;
        ValueText(e).Should().Be("[Property 'Value' ToString threw FormatException]");
    }

    // Template error on each factory path: plain ToString works, formatting with a format string throws.
    [Fact]
    public void Template_error_on_the_fast_path_carries_no_exception_text()
    {
        var e = Factory().Create(KnownLogLevels.Information, LogCategory.App, "Patient {Value:N2}",
            new[] { new LogProperty("Value", new ThrowsWhenFormatted()) });

        e.Message.Should().Be("[Template error: FormatException] Patient {Value:N2}");
    }

    [Fact]
    public void Template_error_on_the_standard_path_carries_no_exception_text()
    {
        var e = Factory().Create(KnownLogLevels.Information, LogCategory.App, "Patient {Value:N2}",
            new[] { new LogProperty("Value", new ThrowsWhenFormatted()) }, context: CallerContext);

        e.Message.Should().Be("[Template error: FormatException] Patient {Value:N2}");
    }

    // Diagnostic failure sink: Herald writes this record and file, so it carries the exception type only.
    [Fact]
    public void Diagnostic_failure_sink_records_and_files_carry_no_exception_text()
    {
        var path = Path.Combine(Path.GetTempPath(), $"herald-failures-{Guid.NewGuid():N}.log");
        try
        {
            var sink = new DiagnosticLogFailureSink(path: path);
            var e = new LogEvent(DateTimeOffset.UtcNow, KnownLogLevels.Information, LogCategory.App, "lookup", "lookup",
                LogEvent.EmptyProperties, LogEvent.EmptyContext);

            sink.ReportFailure(e, new FormatException($"The input string '{Ssn}' was not in a correct format."), "TestSink");

            var record = sink.GetEntries().Single();
            record.ExceptionType.Should().Be("System.FormatException");
#pragma warning disable CS0618 // the obsolete field is asserted on purpose: it must stay empty
            record.ExceptionMessage.Should().BeEmpty();
#pragma warning restore CS0618
            File.ReadAllText(path).Should().NotContain(Ssn).And.Contain("exceptionType=\"System.FormatException\"");
        }
        finally
        {
            File.Delete(path);
        }
    }

    private sealed class ThrowsOnToString
    {
        public override string ToString() => throw new FormatException($"The input string '{Ssn}' was not in a correct format.");
    }

    private sealed class ThrowsWhenFormatted : IFormattable
    {
        public string ToString(string? format, IFormatProvider? formatProvider) =>
            format is null ? "plain" : throw new FormatException($"The input string '{Ssn}' was not in a correct format.");

        public override string ToString() => "plain";
    }
}

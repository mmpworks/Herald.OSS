#nullable enable

using System;
using System.Collections.Generic;
using FluentAssertions;
using MMP.Herald.Addons.OtlpSinks;
using MMP.Herald.Events;
using MMP.Herald.Levels;
using MMP.Herald.Services;
using MMP.Herald.Templating;
using Xunit;

namespace MMP.Herald.OSS.Tests.Otlp;

/// <summary>
/// OtelLogRecord emits OpenTelemetry exception semantic-convention
/// attributes (exception.type, exception.message, exception.stacktrace)
/// when the source LogEvent carries an Exception in Context. These tests
/// pin the wire shape so downstream observability backends (Sentry,
/// Honeycomb, OpenObserve, Traceway, Datadog) can group Issues by the
/// expected attribute triple.
/// </summary>
public sealed class OtelLogRecordExceptionTests
{
    [Fact]
    public void Exception_in_context_produces_otel_semconv_triple()
    {
        var exception = MakeThrown(new InvalidOperationException("boom"));
        var logEvent = MakeLogEvent(exception);

        var record = OtelLogRecord.FromLogEvent(logEvent);

        record.Attributes.Should().ContainKey("exception.type")
            .WhoseValue.Should().Be("System.InvalidOperationException");
        record.Attributes.Should().ContainKey("exception.message")
            .WhoseValue.Should().Be("boom");
        record.Attributes.Should().ContainKey("exception.stacktrace");
        record.Attributes["exception.stacktrace"].Should().BeOfType<string>()
            .Which.Should().Contain("System.InvalidOperationException")
            .And.Contain("boom");
    }

    [Fact]
    public void Empty_message_skips_attribute_per_otel_semconv()
    {
        // OTel semconv: "absent attribute == unknown." For an exception
        // whose Message is empty, the encoder omits exception.message
        // rather than emitting an empty string.
        var exception = MakeThrown(new EmptyMessageException());
        var logEvent = MakeLogEvent(exception);

        var record = OtelLogRecord.FromLogEvent(logEvent);

        record.Attributes.Should().ContainKey("exception.type");
        record.Attributes.Should().NotContainKey("exception.message");
        record.Attributes.Should().ContainKey("exception.stacktrace");
    }

    [Fact]
    public void Inner_exception_appears_in_stacktrace()
    {
        // exception.stacktrace uses Exception.ToString() so the InnerException
        // chain is included by .NET's canonical format. Backends parse this
        // shape; we don't invent a separate inner-exception attribute.
        var inner = MakeThrown(new ArgumentException("inner-cause"));
        var outer = MakeThrown(new InvalidOperationException("outer-failure", inner));
        var logEvent = MakeLogEvent(outer);

        var record = OtelLogRecord.FromLogEvent(logEvent);

        var stack = (string)record.Attributes["exception.stacktrace"]!;
        stack.Should().Contain("System.InvalidOperationException");
        stack.Should().Contain("outer-failure");
        stack.Should().Contain("System.ArgumentException");
        stack.Should().Contain("inner-cause");
    }

    [Fact]
    public void Aggregate_exception_each_inner_in_stacktrace()
    {
        // AggregateException.ToString() emits "(Inner Exception #N)" blocks
        // for each inner exception. Pin that behaviour so consumers can
        // rely on the canonical .NET format.
        var i1 = MakeThrown(new ArgumentException("first"));
        var i2 = MakeThrown(new InvalidOperationException("second"));
        var agg = MakeThrown(new AggregateException("multiple", i1, i2));
        var logEvent = MakeLogEvent(agg);

        var record = OtelLogRecord.FromLogEvent(logEvent);

        var stack = (string)record.Attributes["exception.stacktrace"]!;
        stack.Should().Contain("System.AggregateException");
        stack.Should().Contain("first");
        stack.Should().Contain("second");
    }

    [Fact]
    public void Raw_exception_object_does_not_leak_as_attribute()
    {
        // The context-copy loop must skip the "exception" key — the raw
        // Exception object is not a valid OTLP KeyValue. The semconv-aware
        // exception.* attributes are the only correct representation.
        var exception = MakeThrown(new InvalidOperationException("test"));
        var logEvent = MakeLogEvent(exception);

        var record = OtelLogRecord.FromLogEvent(logEvent);

        record.Attributes.Should().NotContainKey("exception",
            "the raw Exception object must not appear as an OTLP attribute; " +
            "only the semconv triple (type/message/stacktrace) is valid");
    }

    [Fact]
    public void No_exception_in_context_emits_no_exception_attributes()
    {
        var logEvent = new LogEvent(
            TimeUtc: DateTimeOffset.UtcNow,
            Level: KnownLogLevels.Info,
            Category: LogCategory.App,
            MessageTemplate: "regular message",
            Message: "regular message",
            Properties: Array.Empty<LogProperty>(),
            Context: LogEvent.EmptyContext);

        var record = OtelLogRecord.FromLogEvent(logEvent);

        record.Attributes.Should().NotContainKey("exception.type");
        record.Attributes.Should().NotContainKey("exception.message");
        record.Attributes.Should().NotContainKey("exception.stacktrace");
    }

    // -- Helpers --

    /// <summary>Force the exception to be thrown so it has a real stack trace.</summary>
    private static T MakeThrown<T>(T exception) where T : Exception
    {
        try { throw exception; }
        catch (T caught) { return caught; }
    }

    private static LogEvent MakeLogEvent(Exception exception)
    {
        var context = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [LogContextKeys.Exception] = exception,
        };
        return new LogEvent(
            TimeUtc: DateTimeOffset.UtcNow,
            Level: KnownLogLevels.Error,
            Category: LogCategory.App,
            MessageTemplate: "operation failed: {ErrorKind}",
            Message: "operation failed: timeout",
            Properties: Array.Empty<LogProperty>(),
            Context: context);
    }

    private sealed class EmptyMessageException : Exception
    {
        public override string Message => string.Empty;
    }
}

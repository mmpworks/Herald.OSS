#nullable enable

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using FluentAssertions;
using MMP.Herald.Events;
using MMP.Herald.Failures;
using MMP.Herald.Levels;
using MMP.Herald.Templating;
using Xunit;

namespace MMP.Herald.OSS.Tests.LogFailureContract;

/// <summary>
/// The failure contract used to carry an exception, an event, and a source
/// string. A consumer that wanted to know whether the write was worth retrying
/// had to parse the exception type itself, so every consumer wrote its own
/// version of the classifier and they disagreed.
///
/// <para>
/// LogFailure carries the decision instead: a stable Code the caller branches
/// on, a Retryable flag, and a correlation id that ties a failure to the log
/// line an operator is reading.
/// </para>
/// </summary>
public sealed class LogFailureContractTests
{
    private static LogEvent BuildEvent(string message) => new LogEvent(
        TimeUtc: DateTimeOffset.UtcNow,
        Level: KnownLogLevels.Error,
        Category: LogCategory.App,
        MessageTemplate: message,
        Message: message,
        Properties: Array.Empty<LogProperty>(),
        Context: LogEvent.EmptyContext);

    /// <summary>A sink written against the original method only. It must keep working.</summary>
    private sealed class LegacySink : ILogFailureSink
    {
        public List<string> Sources { get; } = [];

        public void ReportFailure(LogEvent logEvent, Exception exception, string source) {
            Sources.Add(source);
        }
    }

    /// <summary>A sink written against the new contract.</summary>
    private sealed class RecordingSink : ILogFailureSink
    {
        public List<LogFailure> Failures { get; } = [];

        public void ReportFailure(LogEvent logEvent, Exception exception, string source) {
            Failures.Add(LogFailure.From(logEvent, exception, source));
        }

        public void ReportFailure(LogFailure failure) {
            Failures.Add(failure);
        }
    }

    [Fact]
    public void Transient_exception_is_retryable() {
        var failure = LogFailure.From(BuildEvent("m"), new TimeoutException(), "test");

        failure.Retryable.Should().BeTrue();
        failure.Code.Should().Be(LogFailure.TransientCode);
    }

    [Fact]
    public void Permanent_exception_is_not_retryable() {
        var failure = LogFailure.From(BuildEvent("m"), new UnauthorizedAccessException(), "test");

        failure.Retryable.Should().BeFalse();
        failure.Code.Should().Be(LogFailure.PermanentCode);
    }

    [Fact]
    public void Rate_limited_write_is_retryable() {
        var ex = new HttpRequestException("throttled", null, HttpStatusCode.TooManyRequests);

        LogFailure.From(BuildEvent("m"), ex, "test").Retryable.Should().BeTrue();
    }

    [Fact]
    public void Each_failure_carries_its_own_correlation_id() {
        var a = LogFailure.From(BuildEvent("m"), new TimeoutException(), "test");
        var b = LogFailure.From(BuildEvent("m"), new TimeoutException(), "test");

        a.CorrelationId.Should().NotBeNullOrWhiteSpace();
        b.CorrelationId.Should().NotBe(a.CorrelationId);
    }

    [Fact]
    public void Failure_keeps_the_original_message_event_and_source() {
        var logEvent = BuildEvent("the message");
        var exception = new TimeoutException("boom");

        var failure = LogFailure.From(logEvent, exception, "MySink");

        failure.Message.Should().Be("boom");
        failure.Exception.Should().BeSameAs(exception);
        failure.LogEvent.Should().BeSameAs(logEvent);
        failure.Source.Should().Be("MySink");
    }

    [Fact]
    public void Legacy_sink_still_receives_a_failure_sent_on_the_new_overload() {
        ILogFailureSink sink = new LegacySink();
        var failure = LogFailure.From(BuildEvent("m"), new TimeoutException(), "MySink");

        sink.ReportFailure(failure);

        ((LegacySink)sink).Sources.Should().ContainSingle().Which.Should().Be("MySink");
    }

    [Fact]
    public void Diagnostic_sink_records_the_code_and_the_retryable_flag() {
        var sink = new DiagnosticLogFailureSink(maxEntries: 4);

        sink.ReportFailure(LogFailure.From(BuildEvent("m"), new TimeoutException(), "MySink"));

        var record = sink.GetEntries().Should().ContainSingle().Subject;
        record.Code.Should().Be(LogFailure.TransientCode);
        record.Retryable.Should().BeTrue();
        record.CorrelationId.Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// Fuzz across the exception shapes the pipeline actually sees. Retryable
    /// must agree with FailureClassifier on every one; a disagreement means a
    /// consumer branching on Retryable and a consumer branching on the category
    /// would take different paths for the same failure.
    /// </summary>
    [Fact]
    public void Fuzz_retryable_agrees_with_the_classifier() {
        const int seed = 20260904;
        var random = new Random(seed);
        var logEvent = BuildEvent("m");

        for (var i = 0; i < 5_000; i++)
        {
            Exception exception = random.Next(7) switch
            {
                0 => new TimeoutException(),
                1 => new SocketException(),
                2 => new UnauthorizedAccessException(),
                3 => new FormatException(),
                4 => new NotSupportedException(),
                5 => new HttpRequestException("s", null, (HttpStatusCode)random.Next(400, 600)),
                _ => new InvalidOperationException()
            };

            var category = FailureClassifier.Classify(exception);
            var failure = LogFailure.From(logEvent, exception, "fuzz");

            var expectedCode =
                category == FailureCategory.Transient ? LogFailure.TransientCode
                : category == FailureCategory.Permanent ? LogFailure.PermanentCode
                : LogFailure.UnknownCode;

            failure.Code.Should().Be(
                expectedCode, "{0} classifies as {1} (seed {2})", exception.GetType().Name, category, seed);
            failure.Retryable.Should().Be(
                category != FailureCategory.Permanent,
                "{0} classifies as {1} (seed {2})", exception.GetType().Name, category, seed);
        }
    }
}

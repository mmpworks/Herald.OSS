using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using MMP.Herald.Events;
using MMP.Herald.Levels;
using MMP.Herald.Pipeline;
using MMP.Herald.Pipeline.Kernel;
using MMP.Herald.Templating;
using Xunit;

namespace MMP.Herald.OSS.Tests.Events;

/// <summary>
/// When a lazy property factory or the message template throws, Herald keeps logging and writes a fallback.
/// The fallback names the property and the exception TYPE only. On .NET 8 and later an exception message often
/// quotes its input (<c>int.Parse("999-12-3456")</c> gives "The input string '999-12-3456' was not in a correct
/// format."), and a fallback that copied it would carry the raw value into a property or the Message, where
/// name-based redaction does not see it.
/// </summary>
public sealed class ExceptionTextIsolationTests
{
    private const string Ssn = "999-12-3456";

    private static object? ParseSsn() => int.Parse(Ssn);

    private static IEnumerable<string> Texts(LogEvent e) =>
        e.Properties.Select(p => p.ResolvedValue?.ToString() ?? "")
            .Append(e.Message)
            .Concat(e.Context.Values.Select(v => v?.ToString() ?? ""));

    [Fact]
    public void Lazy_property_fallback_names_the_property_and_type_only()
    {
        var value = LogProperty.Lazy("Ssn", ParseSsn).ResolvedValue as string;

        value.Should().Be("[Lazy property 'Ssn' threw FormatException]");
    }

    [Fact]
    public void Eager_resolution_fallback_carries_no_exception_text()
    {
        var properties = new List<LogProperty> { LogProperty.Lazy("Ssn", ParseSsn) };

        LogPropertyEagerResolver.ResolveInPlace(properties);

        properties[0].Value.Should().Be("[Lazy property 'Ssn' threw FormatException]");
    }

    [Fact]
    public async Task Async_handoff_fallback_carries_no_exception_text()
    {
        var inner = new CollectingLogger();
        var sink = new FastPathAsyncSink(inner, boundedCapacity: 8);
        sink.Log(new LogEvent(DateTimeOffset.UtcNow, KnownLogLevels.Information, LogCategory.App,
            "Patient {Ssn}", "Patient", new[] { LogProperty.Lazy("Ssn", ParseSsn) }, LogEvent.EmptyContext));

        await sink.DrainAsync(TimeSpan.FromSeconds(10));
        await sink.DisposeAsync();

        inner.Events.Should().ContainSingle();
        Texts(inner.Events[0]).Should().NotContain(t => t.Contains(Ssn));
        inner.Events[0].Properties[0].Value.Should().Be("[Lazy property 'Ssn' threw FormatException]");
    }

    [Fact]
    public void Template_error_message_carries_no_exception_text()
    {
        var factory = new LogEventFactory(
            new MMP.Herald.Time.SystemDateTimeProvider(),
            new MessageTemplateParser(),
            new AsyncLocalLogScopeProvider(),
            new MMP.Herald.Enrichers.NullLogEnricher());

        var e = factory.Create(KnownLogLevels.Information, LogCategory.App, "Patient {Value}",
            new[] { new LogProperty("Value", new HostileFormattable()) });

        e.Message.Should().StartWith("[Template error: FormatException]");
        e.Message.Should().NotContain(Ssn);
    }

    [Fact]
    public void Deferred_render_error_message_carries_no_exception_text()
    {
        var inner = new CollectingLogger();
        var renderer = new RenderingLogger(inner, new MessageTemplateParser());
        var context = new Dictionary<string, object?> { [DeferredLogEventFactory.DeferredSentinel] = "true" };

        renderer.Log(new LogEvent(DateTimeOffset.UtcNow, KnownLogLevels.Information, LogCategory.App,
            "Patient {Value}", "", new[] { new LogProperty("Value", new HostileFormattable()) }, context));

        inner.Events.Should().ContainSingle();
        inner.Events[0].Message.Should().StartWith("[Template error: FormatException]");
        inner.Events[0].Message.Should().NotContain(Ssn);
    }

    /// <summary>A value whose formatting throws with the raw value in the message.</summary>
    private sealed class HostileFormattable : IFormattable
    {
        public string ToString(string? format, IFormatProvider? formatProvider) =>
            throw new FormatException($"The input string '{Ssn}' was not in a correct format.");

        public override string ToString() => ToString(null, null);
    }

    private sealed class CollectingLogger : ILogger
    {
        public List<LogEvent> Events { get; } = new();
        public void Log(LogEvent logEvent) { lock (Events) Events.Add(logEvent); }
    }
}

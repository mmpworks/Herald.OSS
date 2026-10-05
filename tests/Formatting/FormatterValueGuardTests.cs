using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using FluentAssertions;
using MMP.Herald.Addons.BinarySerialization;
using MMP.Herald.Addons.Compliance;
using MMP.Herald.Events;
using MMP.Herald.Formatting;
using MMP.Herald.Levels;
using MMP.Herald.Output.Aliases;
using MMP.Herald.Output.Rendering;
using MMP.Herald.Output.Rendering.Themes;
using MMP.Herald.Pipeline.Kernel;
using MMP.Herald.Templating;
using Xunit;

namespace MMP.Herald.OSS.Tests.Formatting;

/// <summary>
/// Second layer of the ToString rule (PR #13): any object may throw on a later call even after earlier calls
/// succeeded, so every formatter guards its own ToString. Each value here succeeds on calls 1 and 2 (the pipeline
/// already used it) and throws on call 3, inside the formatter. The test asserts the formatter does not throw,
/// writes the fallback text, and writes no SSN. One test per guarded site, so each site has its own pin.
/// </summary>
public sealed class FormatterValueGuardTests
{
    private const string Ssn = "999-12-3456";
    private static readonly ILogLevelRegistry Registry = LogLevelRegistry.CreateDefault();

    private static LaterThrow Spent()
    {
        var value = new LaterThrow();
        _ = value.ToString();
        _ = value.ToString();
        return value;
    }

    private static LogEvent Event(object? property = null, object? context = null, string template = "Patient {Value}") =>
        new(DateTimeOffset.UtcNow, KnownLogLevels.Information, LogCategory.App, template, "Patient",
            property is null ? LogEvent.EmptyProperties : new[] { new LogProperty("Value", property) },
            context is null ? LogEvent.EmptyContext : new Dictionary<string, object?> { ["Ctx"] = context });

    private static void AssertGuarded(string output, string name)
    {
        output.Should().Contain($"[Property '{name}' ToString threw FormatException]");
        output.Should().NotContain(Ssn);
    }

    // Utf8JsonWriter escapes the apostrophe as '; unescape so the fallback text can be matched.
    private static string Utf8(Action<ArrayBufferWriter<byte>> write)
    {
        var buffer = new ArrayBufferWriter<byte>();
        write(buffer);
        return System.Text.RegularExpressions.Regex.Unescape(Encoding.UTF8.GetString(buffer.WrittenSpan));
    }

    // MessagePackLogFormatter.Format returns base64 of the binary payload; strings inside are plain UTF-8.
    private static string MessagePack(LogEvent e) =>
        Encoding.UTF8.GetString(Convert.FromBase64String(new MessagePackLogFormatter(Registry).Format(e)));

    // JsonFormatter
    [Fact] public void Json_event_property() => AssertGuarded(new JsonFormatter(Registry).Format(Event(property: Spent())), "Value");
    [Fact] public void Json_event_context() => AssertGuarded(new JsonFormatter(Registry).Format(Event(context: Spent())), "Ctx");

    [Fact]
    public void Json_buffer_properties()
    {
        var props = new[] { new LogProperty("Value", Spent()) };
        var buffer = new LogEventBuffer(DateTimeOffset.UtcNow, KnownLogLevels.Information, LogCategory.App, "t", "m", props.AsSpan());
        AssertGuarded(new JsonFormatter(Registry).Format(in buffer), "Value");
    }

    [Fact]
    public void Json_buffer_compact_properties()
    {
        var props = new[] { new LogPropertyCompact("Value", Spent()) };
        var buffer = new LogEventBuffer(DateTimeOffset.UtcNow, KnownLogLevels.Information, LogCategory.App, "t", "m",
            (ReadOnlySpan<LogPropertyCompact>)props.AsSpan());
        AssertGuarded(new JsonFormatter(Registry).Format(in buffer), "Value");
    }

    // Utf8JsonFormatter
    [Fact] public void Utf8Json_event_property() => AssertGuarded(Utf8(o => new Utf8JsonFormatter(Registry).Format(Event(property: Spent()), o)), "Value");
    [Fact] public void Utf8Json_event_context() => AssertGuarded(Utf8(o => new Utf8JsonFormatter(Registry).Format(Event(context: Spent()), o)), "Ctx");

    [Fact]
    public void Utf8Json_buffer_properties()
    {
        var props = new[] { new LogProperty("Value", Spent()) };
        AssertGuarded(Utf8(o =>
        {
            var buffer = new LogEventBuffer(DateTimeOffset.UtcNow, KnownLogLevels.Information, LogCategory.App, "t", "m", props.AsSpan());
            new Utf8JsonFormatter(Registry).Format(in buffer, o);
        }), "Value");
    }

    [Fact]
    public void Utf8Json_buffer_compact_reference_value()
    {
        var props = new[] { new LogPropertyCompact("Value", Spent()) };
        AssertGuarded(Utf8(o =>
        {
            var buffer = new LogEventBuffer(DateTimeOffset.UtcNow, KnownLogLevels.Information, LogCategory.App, "t", "m",
                (ReadOnlySpan<LogPropertyCompact>)props.AsSpan());
            new Utf8JsonFormatter(Registry).Format(in buffer, o);
        }), "Value");
    }

    // OutputTemplateFormatter and PlainTextFormatter
    [Fact] public void OutputTemplate_property() => AssertGuarded(new OutputTemplateFormatter(Registry, "{properties}").Format(Event(property: Spent())), "Value");
    [Fact] public void OutputTemplate_context() => AssertGuarded(new OutputTemplateFormatter(Registry, "{context}").Format(Event(context: Spent())), "Ctx");
    [Fact] public void PlainText_context() => AssertGuarded(new PlainTextFormatter(Registry).Format(Event(context: Spent())), "Ctx");

    // MessagePackLogFormatter
    [Fact] public void MessagePack_property() => AssertGuarded(MessagePack(Event(property: Spent())), "Value");

    // Output transformers
    [Fact]
    public void StandardOutput_context()
    {
        var output = new StandardLogOutputTransformer().Transform(new LogRenderContext(Event(context: Spent()), new LogOutputAlias("test"), Registry));
        AssertGuarded(string.Concat(output.Fragments.Select(f => f.Text)), "Ctx");
    }

    [Theory]
    [InlineData("Patient {Value}")]      // default rendering
    [InlineData("Patient {$Value}")]     // stringify
    public void ConsoleOutput_property(string template) =>
        AssertGuarded(Console(Event(property: Spent(), template: template)), "Value");

    [Fact]
    public void ConsoleOutput_formatted_property() =>
        AssertGuarded(Console(Event(property: SpentFormattable(), template: "Patient {Value:N2}")), "Value");

    [Fact] public void ConsoleOutput_context() => AssertGuarded(Console(Event(context: Spent())), "Ctx");

    private static string Console(LogEvent e)
    {
        var output = new ConsoleOutputTransformer(new StandardLogOutputTransformer(), BuiltInConsoleThemes.None)
            .Transform(new LogRenderContext(e, new LogOutputAlias("test"), Registry));
        return string.Concat(output.Fragments.Select(f => f.Text));
    }

    // HmacChainLogger: the guarded text goes into the HMAC input, so the test asserts the call does not throw
    // and the event reaches the inner logger.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HmacChain_signs_an_event_whose_value_throws_later(bool formattable)
    {
        var inner = new Collecting();
        using var chain = new HmacChainLogger(inner, new byte[32]);
        var value = formattable ? (object)SpentFormattable() : Spent();

        var act = () => chain.Log(Event(property: value, context: formattable ? SpentFormattable() : Spent()));

        act.Should().NotThrow();
        inner.Count.Should().Be(1);
    }

    private static LaterThrowFormattable SpentFormattable()
    {
        var value = new LaterThrowFormattable();
        _ = value.ToString("N2", CultureInfo.InvariantCulture);
        _ = value.ToString("N2", CultureInfo.InvariantCulture);
        return value;
    }

    private sealed class LaterThrow
    {
        private int _calls;

        public override string ToString() =>
            System.Threading.Interlocked.Increment(ref _calls) <= 2
                ? "plain"
                : throw new FormatException($"The input string '{Ssn}' was not in a correct format.");
    }

    private sealed class LaterThrowFormattable : IFormattable
    {
        private int _calls;

        public string ToString(string? format, IFormatProvider? formatProvider) =>
            System.Threading.Interlocked.Increment(ref _calls) <= 2
                ? "plain"
                : throw new FormatException($"The input string '{Ssn}' was not in a correct format.");

        public override string ToString() => ToString(null, null);
    }

    private sealed class Collecting : ILogger
    {
        public int Count;
        public void Log(LogEvent logEvent) => System.Threading.Interlocked.Increment(ref Count);
    }
}

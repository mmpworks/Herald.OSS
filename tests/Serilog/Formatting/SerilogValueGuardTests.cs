using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using FluentAssertions;
using MMP.Herald.Events;
using MMP.Herald.Levels;
using MMP.Herald.Serilog;
using MMP.Herald.Serilog.Core;
using MMP.Herald.Serilog.Destructuring;
using MMP.Herald.Serilog.Formatting;
using MMP.Herald.Templating;
using Xunit;
using SerilogEvent = MMP.Herald.Serilog.Events.LogEvent;
using MMP.Herald.Serilog.Events;

namespace MMP.Herald.OSS.Tests.Serilog.Formatting;

/// <summary>
/// The Serilog-compatible formatters, renderers, projection and destructuring guard every ToString on a value
/// (PR #13). A value here succeeds on its first two calls and throws on the third, with the SSN in the message.
/// Each test asserts no throw, the fallback text, and no SSN. One test per guarded site.
/// </summary>
[Collection(nameof(SerilogValueGuardTests))]
[CollectionDefinition(nameof(SerilogValueGuardTests), DisableParallelization = true)] // Console.SetOut is global
public sealed class SerilogValueGuardTests
{
    private const string Ssn = "999-12-3456";
    private const string Named = "ToString threw FormatException]";

    // ── The route review round 2 found: WriteTo.Console(new CompactJsonFormatter()) formats outside
    //    SerilogUserLogger's Emit isolation. The value succeeds at sizing and at message render and throws
    //    at serialization.
    [Fact]
    public void Console_with_compact_json_formatter_never_throws_and_never_writes_the_value()
    {
        var original = System.Console.Out;
        var captured = new StringWriter();
        System.Console.SetOut(captured);
        try
        {
            var log = new LoggerConfiguration().WriteTo.Console(new CompactJsonFormatter()).CreateLogger();
            var act = () => log.Information("Patient {Value}", new LaterThrow());
            act.Should().NotThrow();
        }
        finally
        {
            System.Console.SetOut(original);
        }

        captured.ToString().Should().Contain(Named).And.NotContain(Ssn);
    }

    // ── SerilogTokenRenderers
    [Fact]
    public void TokenRenderer_scalar() => Render(w => SerilogTokenRenderers.RenderValue(new ScalarValue(Spent()), w, literal: false, json: true));

    [Fact]
    public void TokenRenderer_formattable_scalar() =>
        Render(w => SerilogTokenRenderers.RenderValue(new ScalarValue(SpentFormattable()), w, literal: false, json: true));

    [Fact]
    public void TokenRenderer_unknown_value_kind() => Render(w => SerilogTokenRenderers.RenderValue(new ThrowingNode(), w, literal: false, json: false));

    [Fact]
    public void TokenRenderer_property_hole_with_format() =>
        Render(w => SerilogTokenRenderers.RenderToken(new HoleToken("Value", 0, "N2"), new LogEventSerilogEventView(Mirror(SpentFormattable())), w));

    // ── SerilogPropertiesRenderer (residual properties, default and :j formats)
    [Theory]
    [InlineData(null)]
    [InlineData("j")]
    public void PropertiesRenderer_scalar(string? format) => Render(w => SerilogPropertiesRenderer.Render(View(new ScalarValue(Spent())), format, w));

    [Theory]
    [InlineData(null)]
    [InlineData("j")]
    public void PropertiesRenderer_unknown_value_kind(string? format) => Render(w => SerilogPropertiesRenderer.Render(View(new ThrowingNode()), format, w));

    // ── Projection of a native event into the Serilog-shaped mirror
    [Fact]
    public void Projection_stringify() =>
        Projected(new SerilogEvent(Native(new LogProperty("Value", Spent(), LogPropertyCaptureMode.Stringify))));

    [Fact]
    public void Projection_stringify_with_destructuring_policies()
    {
        var applicator = new SerilogDestructuringApplicator();
        applicator.Add(new DeclinePolicy());
        Projected(new SerilogEvent(Native(new LogProperty("Value", Spent(), LogPropertyCaptureMode.Stringify)), applicator));
    }

    [Fact]
    public void Projection_stringify_from_enrichment_context()
    {
        var context = new LogEventEnrichmentContext(KnownLogLevels.Information, LogCategory.App, "Patient {Value}",
            new List<LogProperty> { new("Value", Spent(), LogPropertyCaptureMode.Stringify) }, new Dictionary<string, object?>());
        Projected(new SerilogEvent(context));
    }

    [Fact]
    public void Projection_destructure_beyond_max_depth()
    {
        object leaf = Spent();
        for (var i = 0; i < 12; i++) leaf = new Box(leaf);
        var mirror = new SerilogEvent(Native(new LogProperty("Value", leaf, LogPropertyCaptureMode.Destructure)));

        var act = () => mirror.Properties.Count;
        act.Should().NotThrow();
        Render(w => SerilogTokenRenderers.RenderValue(mirror.Properties["Value"], w, literal: false, json: true));
    }

    // ── Destructuring: applicator (native redaction) and policy bridge (string serialisation)
    [Fact]
    public void Applicator_unknown_node_kind()
    {
        var applicator = new SerilogDestructuringApplicator();
        applicator.Add(new ReturnPolicy(new ThrowingNode()));

        applicator.TryRedactNative(new object(), out var redacted).Should().BeTrue();
        redacted.Should().BeOfType<string>().Which.Should().Contain(Named).And.NotContain(Ssn);
    }

    [Fact]
    public void PolicyBridge_scalar() => Bridged(new ScalarValue(Spent()));

    [Fact]
    public void PolicyBridge_unknown_node_kind() => Bridged(new ThrowingNode());

    // ── Helpers
    private static void Bridged(LogEventPropertyValue tree)
    {
        var bridge = new SerilogDestructuringPolicyBridge(new ReturnPolicy(tree));
        bridge.TryDestructure(new object(), out var result).Should().BeTrue();
        result.Should().Contain(Named).And.NotContain(Ssn);
    }

    private static void Projected(SerilogEvent mirror)
    {
        var value = mirror.Properties["Value"];
        value.Should().BeOfType<ScalarValue>().Which.Value.Should().BeOfType<string>()
            .Which.Should().Contain(Named).And.NotContain(Ssn);
    }

    private static void Render(Action<TextWriter> render, bool expectFallback = true)
    {
        var writer = new StringWriter();
        var act = () => render(writer);
        act.Should().NotThrow();
        if (expectFallback) writer.ToString().Should().Contain(Named);
        writer.ToString().Should().NotContain(Ssn);
    }

    private static MMP.Herald.Events.LogEvent Native(params LogProperty[] properties) =>
        new(DateTimeOffset.UtcNow, KnownLogLevels.Information, LogCategory.App, "Patient {Value}", "Patient",
            properties, MMP.Herald.Events.LogEvent.EmptyContext);

    private static SerilogEvent Mirror(object value) => new(Native(new LogProperty("Value", value)));

    // A view whose one residual property (not a template hole) holds the given value.
    private static ISerilogEventView View(LogEventPropertyValue value) => new FixedView(value);

    private static LaterThrow Spent()
    {
        var value = new LaterThrow();
        _ = value.ToString();
        _ = value.ToString();
        return value;
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

    private sealed class ThrowingNode : LogEventPropertyValue
    {
        public override string ToString() => throw new FormatException($"The input string '{Ssn}' was not in a correct format.");
    }

    private sealed record Box(object Inner);

    private sealed class FixedView(LogEventPropertyValue value) : ISerilogEventView
    {
        public DateTimeOffset Timestamp => DateTimeOffset.UtcNow;
        public LogEventLevel Level => LogEventLevel.Information;
        public string MessageTemplate => "Patient";
        public string RenderedMessage => "Patient";
        public IReadOnlyDictionary<string, LogEventPropertyValue> Properties { get; } = new Dictionary<string, LogEventPropertyValue> { ["Value"] = value };
        public Exception? Exception => null;
        public IReadOnlyList<string> TemplateHoleNames { get; } = Array.Empty<string>();
    }

    private sealed class DeclinePolicy : MMP.Herald.Serilog.Core.IDestructuringPolicy
    {
        public bool TryDestructure(object value, ILogEventPropertyValueFactory propertyValueFactory, out LogEventPropertyValue result)
        {
            result = null!;
            return false;
        }
    }

    private sealed class ReturnPolicy(LogEventPropertyValue tree) : MMP.Herald.Serilog.Core.IDestructuringPolicy
    {
        public bool TryDestructure(object value, ILogEventPropertyValueFactory propertyValueFactory, out LogEventPropertyValue result)
        {
            result = tree;
            return true;
        }
    }
}

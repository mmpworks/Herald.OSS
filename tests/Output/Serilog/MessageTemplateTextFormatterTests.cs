#nullable enable
// P3 Task 7: MessageTemplateTextFormatter — grammar as ITextFormatter.
// Gated to net9+ — MMP.Herald.Serilog targets net9/net10 only.

using System;
using System.IO;
using FluentAssertions;
using MMP.Herald.OSS.Tests.TestSupport;
using MMP.Herald.Serilog;
using MMP.Herald.Serilog.Formatting;
using SerilogMirrorLogEvent = MMP.Herald.Serilog.Events.LogEvent;
using SerilogILogger = MMP.Herald.Serilog.ILogger;
using Xunit;

namespace MMP.Herald.OSS.Tests.Output.Serilog;

/// <summary>
/// P3 Task 7 win-condition tests: <see cref="MessageTemplateTextFormatter"/> wraps
/// <see cref="SerilogOutputTemplateRenderer"/> as an <see cref="ITextFormatter"/>.
///
/// <para>
/// <b>Strategy.</b> Every assertion that checks rendered content compares Herald's
/// output to <see cref="SerilogFormattingOracle.RenderOutputTemplate"/> rather than
/// a hand-written expected string. This pins Herald's output to real Serilog 4.x
/// behaviour at the formatter boundary.
/// </para>
///
/// <para>
/// <b>Timestamp exclusion.</b> The oracle captures a real Serilog event using the
/// current wall-clock timestamp; Herald's view carries the fixed
/// <see cref="CanonicalEventSpec.Timestamp"/>. These two timestamps are different by
/// construction. Oracle comparisons therefore use the timestamp-free template
/// <c>[{Level:u3}] {Message:lj}{NewLine}{Exception}</c>. Full-template structure
/// (including the <c>{Timestamp:HH:mm:ss}</c> bracket section) is validated by a
/// separate structural assertion.
/// </para>
/// </summary>
public sealed class MessageTemplateTextFormatterTests
{
    // The real Serilog default template — must match MessageTemplateTextFormatter.DefaultOutputTemplate.
    private const string SerilogDefaultTemplate =
        "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}";

    // Timestamp-free for oracle comparison (oracle timestamp ≠ Herald fixed timestamp).
    private const string TimestampFreeTemplate =
        "[{Level:u3}] {Message:lj}{NewLine}{Exception}";

    // ── ITextFormatter surface ────────────────────────────────────────────────

    [Fact]
    public void DefaultOutputTemplate_constant_matches_Serilog_default()
    {
        // Pin the constant so a future edit does not silently break the oracle comparison.
        MessageTemplateTextFormatter.DefaultOutputTemplate
            .Should().Be(SerilogDefaultTemplate,
                "MessageTemplateTextFormatter.DefaultOutputTemplate must exactly match " +
                "the Serilog default output template");
    }

    [Fact]
    public void Default_ctor_uses_the_Serilog_default_template()
    {
        // A formatter built with no arguments must produce the same output as one
        // built with the explicit default template string.
        var withDefault  = new MessageTemplateTextFormatter();
        var withExplicit = new MessageTemplateTextFormatter(SerilogDefaultTemplate);

        var logEvent = BuildLogEvent(new CanonicalEventSpec
        {
            LevelKey        = "information",
            MessageTemplate = "Hello {Name}",
            Properties      = [("Name", (object?)"Alice", false)],
        });

        var resultDefault  = Format(withDefault, logEvent);
        var resultExplicit = Format(withExplicit, logEvent);

        resultDefault.Should().Be(resultExplicit,
            "null outputTemplate must behave identically to passing the default template string");
    }

    [Fact]
    public void Format_writes_to_TextWriter_not_returns_string()
    {
        // Verify the Format contract: output goes to the writer, nothing is returned.
        var formatter = new MessageTemplateTextFormatter("{Level:u3}");
        var logEvent  = BuildLogEvent(CanonicalEventSpec.Simple("Ping", "warning"));
        var writer    = new StringWriter();

        formatter.Format(logEvent, writer);

        writer.ToString().Should().Be("WRN",
            "Format must write the rendered output directly to the TextWriter");
    }

    [Fact]
    public void Format_throws_on_null_logEvent()
    {
        var formatter = new MessageTemplateTextFormatter();
        var writer    = new StringWriter();

        var act = () => formatter.Format(null!, writer);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("logEvent");
    }

    [Fact]
    public void Format_throws_on_null_output()
    {
        var formatter = new MessageTemplateTextFormatter();
        var logEvent  = BuildLogEvent(CanonicalEventSpec.Simple("Ping"));

        var act = () => formatter.Format(logEvent, null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("output");
    }

    // ── Oracle-parity: single-token templates ─────────────────────────────────

    [Theory]
    [InlineData("information", "INF")]
    [InlineData("warning",     "WRN")]
    [InlineData("error",       "ERR")]
    [InlineData("fatal",       "FTL")]
    [InlineData("debug",       "DBG")]
    [InlineData("verbose",     "VRB")]
    public void Level_token_matches_oracle(string levelKey, string expectedMoniker)
    {
        const string template = "{Level:u3}";
        var spec     = CanonicalEventSpec.Simple("Ping", levelKey);
        var expected = SerilogFormattingOracle.RenderOutputTemplate(template, spec);

        var formatter = new MessageTemplateTextFormatter(template);
        var logEvent  = BuildLogEvent(spec);
        var actual    = Format(formatter, logEvent);

        actual.Should().Be(expected,
            $"Level token for '{levelKey}' must match real Serilog (expected moniker: {expectedMoniker})");
    }

    [Fact]
    public void NewLine_token_renders_as_environment_newline()
    {
        var formatter = new MessageTemplateTextFormatter("{NewLine}");
        var logEvent  = BuildLogEvent(CanonicalEventSpec.Simple("Ping"));

        var actual = Format(formatter, logEvent);

        actual.Should().Be(Environment.NewLine,
            "{NewLine} must produce Environment.NewLine");
    }

    [Fact]
    public void Custom_single_token_template_renders_only_that_token()
    {
        // Regression: formatter must not bleed non-requested tokens into output.
        var formatter = new MessageTemplateTextFormatter("{Level:u3}");
        var logEvent  = BuildLogEvent(CanonicalEventSpec.Simple("Ping", "warning"));

        var actual = Format(formatter, logEvent);

        actual.Should().Be("WRN",
            "a single-token template must produce exactly that token's output");
    }

    // ── Oracle-parity: full canonical template (timestamp-free) ───────────────

    /// <summary>
    /// The timestamp-free variant of the canonical Serilog default template matches
    /// real Serilog 4.x output for all seven representative corpus entries.
    /// This is the Task 7 win-condition: the formatter boundary is proven oracle-parity.
    /// </summary>
    [Theory]
    [InlineData(0)]  // simple scalar
    [InlineData(1)]  // integer scalar
    [InlineData(2)]  // string-valued property (:lj branch)
    [InlineData(3)]  // destructured object (:lj JSON branch)
    [InlineData(4)]  // warning level
    [InlineData(5)]  // fatal level
    [InlineData(6)]  // event with exception
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode(
        "Uses LogEventValueProjector.DefaultValueFactory which calls reflection on arbitrary types.")]
    public void Representative_corpus_matches_oracle_at_formatter_boundary(int corpusIndex)
    {
        var corpus = CanonicalEventSpec.RepresentativeCorpus();
        corpusIndex.Should().BeLessThan(corpus.Count, "corpus index out of range");

        var spec      = corpus[corpusIndex];
        var expected  = SerilogFormattingOracle.RenderOutputTemplate(TimestampFreeTemplate, spec);
        var formatter = new MessageTemplateTextFormatter(TimestampFreeTemplate);
        var logEvent  = BuildLogEvent(spec);

        var actual = Format(formatter, logEvent);

        actual.Should().Be(expected,
            $"corpus[{corpusIndex}] (level={spec.LevelKey}, template='{spec.MessageTemplate}') " +
            "must match real Serilog 4.x at the ITextFormatter boundary");
    }

    // ── Structural: default template shape ────────────────────────────────────

    [Fact]
    public void Default_template_output_has_correct_structure()
    {
        // The default template is "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}".
        // Verify the structural shape without comparing the timestamp section to the oracle
        // (oracle timestamp differs from our fixed spec timestamp by construction).
        var formatter = new MessageTemplateTextFormatter();
        var logEvent  = BuildLogEvent(new CanonicalEventSpec
        {
            LevelKey        = "warning",
            MessageTemplate = "User {Name} checked in",
            Properties      = [("Name", (object?)"Bob", false)],
        });

        var result = Format(formatter, logEvent);

        // The bracket section must start with '[' and contain 'HH:mm:ss'-shaped timestamp.
        result.Should().StartWith("[", "canonical template starts with '['");
        // Position 1..8 is the HH:mm:ss section (inside the bracket).
        result.Substring(1, 8).Should().MatchRegex(@"^\d{2}:\d{2}:\d{2}$",
            "the timestamp section must have HH:mm:ss shape");
        result.Should().Contain(" WRN] ",
            "the level moniker must appear in the bracket section after the timestamp");
        result.Should().Contain("Bob",
            "the message section must include the rendered property value");
    }

    // ── Exception rendering ───────────────────────────────────────────────────

    [Fact]
    public void Exception_token_renders_exception_when_present()
    {
        const string template = "{Exception}";
        var spec = new CanonicalEventSpec
        {
            LevelKey        = "error",
            MessageTemplate = "Boom",
            Exception       = new InvalidOperationException("kaboom"),
        };

        var expected  = SerilogFormattingOracle.RenderOutputTemplate(template, spec);
        var formatter = new MessageTemplateTextFormatter(template);
        var logEvent  = BuildLogEvent(spec);

        var actual = Format(formatter, logEvent);

        actual.Should().Be(expected,
            "{Exception} must match the oracle's exception rendering (including trailing newline)");
    }

    [Fact]
    public void Exception_token_is_empty_when_no_exception()
    {
        const string template = "{Exception}";
        var spec      = CanonicalEventSpec.Simple("No exception here");
        var formatter = new MessageTemplateTextFormatter(template);
        var logEvent  = BuildLogEvent(spec);

        var actual = Format(formatter, logEvent);

        actual.Should().BeEmpty(
            "{Exception} must produce no output when the event has no exception");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    // Format a LogEvent using the formatter and return the string written.
    private static string Format(MessageTemplateTextFormatter formatter, SerilogMirrorLogEvent logEvent)
    {
        var writer = new StringWriter();
        formatter.Format(logEvent, writer);
        return writer.ToString();
    }

    // Build a MMP.Herald.Serilog.Events.LogEvent (mirror) from a CanonicalEventSpec.
    //
    // Pattern: drive a SerilogLoggerAdapter over a capturing Herald pipeline,
    // then wrap the captured native event in the P1 mirror type.
    // The mirror constructor is internal; InternalsVisibleTo("Herald.OSS.Tests")
    // on MMP.Herald.Serilog grants access.
    //
    // Cognitive complexity note: the level-key dispatch is the longest branch here.
    // It is intentionally explicit rather than using a generic Enum.Parse, so a
    // wrong key produces a clear "no matching case" read rather than a runtime exception
    // with an opaque enum value.
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode(
        "Uses LogEventValueProjector.DefaultValueFactory which calls reflection on arbitrary types.")]
    private static SerilogMirrorLogEvent BuildLogEvent(CanonicalEventSpec spec)
    {
        var (herald, sink) = TestLoggers.CreateCapturing<MessageTemplateTextFormatterTests>();
        SerilogILogger adapter = new SerilogLoggerAdapter(herald);

        // Build the argument array from the spec's template properties.
        var args = new object?[spec.Properties.Count];
        for (var i = 0; i < spec.Properties.Count; i++)
            args[i] = spec.Properties[i].Value;

        // Dispatch to the correct level method.
        // The exception, when present, is Serilog convention: first overload parameter.
        switch (spec.LevelKey)
        {
            case "verbose":
                if (spec.Exception is { } vex) adapter.Verbose(vex, spec.MessageTemplate, args);
                else                            adapter.Verbose(spec.MessageTemplate, args);
                break;
            case "debug":
                if (spec.Exception is { } dex) adapter.Debug(dex, spec.MessageTemplate, args);
                else                           adapter.Debug(spec.MessageTemplate, args);
                break;
            case "warning":
                if (spec.Exception is { } wex) adapter.Warning(wex, spec.MessageTemplate, args);
                else                           adapter.Warning(spec.MessageTemplate, args);
                break;
            case "error":
                if (spec.Exception is { } eex) adapter.Error(eex, spec.MessageTemplate, args);
                else                           adapter.Error(spec.MessageTemplate, args);
                break;
            case "fatal":
                if (spec.Exception is { } fex) adapter.Fatal(fex, spec.MessageTemplate, args);
                else                           adapter.Fatal(spec.MessageTemplate, args);
                break;
            default: // information + any unknown key
                if (spec.Exception is { } iex) adapter.Information(iex, spec.MessageTemplate, args);
                else                           adapter.Information(spec.MessageTemplate, args);
                break;
        }

        var native = sink.GetEvents();
        native.Should().HaveCount(1, "exactly one event must have been captured");

        // Construct the mirror — internal constructor, accessible via InternalsVisibleTo.
        return new SerilogMirrorLogEvent(native[0]);
    }
}


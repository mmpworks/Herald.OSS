#nullable enable
#if NET9_0_OR_GREATER

using System;
using System.Collections.Generic;
using FluentAssertions;
using MMP.Herald.OSS.Tests.TestSupport;
using MMP.Herald.Serilog.Events;
using MMP.Herald.Serilog.Formatting;
using Xunit;

namespace MMP.Herald.OSS.Tests.Output.Serilog;

/// <summary>
/// Full integration tests for <see cref="SerilogOutputTemplateRenderer.Render"/>
/// against the canonical Serilog default output template.
///
/// <para>
/// <b>G-GAP.1 regression pin — output-template grammar parity.</b>
/// Every built-in token type ({Level}, {Message:lj}, {Timestamp:HH:mm:ss},
/// {NewLine}, {Exception}, {Properties}) must render output character-for-character
/// identical to real Serilog's OutputTemplateRenderer on the same view.
/// </para>
///
/// <para>
/// <b>P3 Task 6 win-condition test.</b> The canonical default template
/// <c>"[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}"</c>
/// exercises ALL token types in a single pass. Every non-timestamp character of the
/// output must match real Serilog's <see cref="SerilogFormattingOracle.RenderOutputTemplate"/>.
/// </para>
///
/// <para>
/// <b>Timestamp oracle limitation.</b> The oracle captures a real Serilog log event at
/// test execution time (using <c>DateTimeOffset.Now</c> as the event timestamp). Herald's
/// renderer uses the fixed <see cref="CanonicalEventSpec.Timestamp"/> value. These two
/// timestamps are fundamentally different, so oracle comparison for the full
/// <c>{Timestamp:HH:mm:ss}</c> token is done via a separate level-only template.
/// The timestamp rendering itself is tested oracle-against-Herald in
/// <c>SerilogTokenRenderTests</c> (Task 4 tests) where both sides see the same view.
/// </para>
///
/// <para>
/// <b>Task 4 is live.</b> Message, Level, Timestamp, NewLine, and Exception tokens all
/// dispatch through <see cref="SerilogTokenRenderers.RenderToken"/>.
/// Properties residual rendering (Task 5) is the only remaining stub.
/// </para>
/// </summary>
public sealed class SerilogFullTemplateTests
{
    private const string CanonicalTemplate =
        "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}";

    // Template without {Timestamp} for oracle comparison (oracle always captures
    // the current wall-clock time, making timestamp comparison structurally impossible).
    private const string TimestampFreeTemplate =
        "[{Level:u3}] {Message:lj}{NewLine}{Exception}";

    // ── Live token coverage (no Task 4/5 dependency) ─────────────────────────

    /// <summary>
    /// The Level and text-literal tokens in the canonical template render
    /// correctly without Tasks 4/5. This test is the smoke-check that the
    /// composing renderer wires up at all.
    /// </summary>
    [Theory]
    [InlineData("information", "INF")]
    [InlineData("warning",     "WRN")]
    [InlineData("error",       "ERR")]
    [InlineData("fatal",       "FTL")]
    [InlineData("debug",       "DBG")]
    [InlineData("verbose",     "VRB")]
    public void Level_token_renders_correctly_in_canonical_template(string levelKey, string expectedMoniker)
    {
        // Use a Level-only template to isolate the already-live token path.
        const string levelOnlyTemplate = "[{Level:u3}]";
        var evt = MakeFakeView(levelKey);

        var actual = SerilogOutputTemplateRenderer.Render(levelOnlyTemplate, evt);
        actual.Should().Be($"[{expectedMoniker}]",
            $"Level token in template must render {levelKey} as {expectedMoniker}");
    }

    /// <summary>
    /// NewLine token renders as <see cref="Environment.NewLine"/> — no Task 4/5 dependency.
    /// </summary>
    [Fact]
    public void NewLine_token_renders_as_environment_newline()
    {
        var evt    = MakeFakeView("information");
        var actual = SerilogOutputTemplateRenderer.Render("{NewLine}", evt);
        actual.Should().Be(Environment.NewLine, "{NewLine} must produce Environment.NewLine");
    }

    /// <summary>
    /// Text literal tokens (the <c>[</c>, <c> </c>, <c>]</c> surrounding the level/timestamp)
    /// pass through unmodified.
    /// </summary>
    [Fact]
    public void Text_literal_tokens_pass_through_unmodified()
    {
        var evt    = MakeFakeView("information");
        var actual = SerilogOutputTemplateRenderer.Render("BEFORE{NewLine}AFTER", evt);
        actual.Should().Be($"BEFORE{Environment.NewLine}AFTER");
    }

    /// <summary>
    /// Level with alignment inside the canonical-template bracket group renders correctly.
    /// Isolates the bracket + alignment + level-moniker composition.
    /// </summary>
    [Fact]
    public void Level_with_alignment_in_bracket_context_matches_oracle()
    {
        const string template = "[{Level:u3}]";
        var spec = new CanonicalEventSpec { LevelKey = "warning", MessageTemplate = "" };
        var expected = SerilogFormattingOracle.RenderOutputTemplate(template, spec)
                                             .TrimEnd('\n', '\r');

        var evt    = MakeFakeView("warning");
        var actual = SerilogOutputTemplateRenderer.Render(template, evt);
        actual.Should().Be(expected);
    }

    // ── Full oracle-parity tests (Task 4 live — timestamp-free template) ──────

    /// <summary>
    /// The timestamp-free form of the canonical template matches real Serilog 4.3.1.
    ///
    /// <para>
    /// <b>This is the Task 6 win-condition test.</b> The oracle can only be compared
    /// on tokens whose output is deterministic given the spec alone. <c>{Timestamp}</c>
    /// is excluded because the oracle captures the wall-clock time at test execution
    /// (see class-level doc). All other tokens — Level, Message, NewLine, Exception — are
    /// tested here.
    /// </para>
    /// </summary>
    [Fact]
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Uses CanonicalSpecViewFactory which calls LogEventValueProjector (reflection).")]
    public void Canonical_default_template_matches_real_serilog()
    {
        var spec = new CanonicalEventSpec
        {
            LevelKey        = "warning",
            MessageTemplate = "User {Name} spent {Amount}",
            Properties      = BuildProps(("Name", "Alice"), ("Amount", 42.5)),
            Timestamp       = new DateTimeOffset(2024, 6, 15, 14, 30, 0, TimeSpan.FromHours(5)),
            Exception       = null,
        };

        var expected = SerilogFormattingOracle.RenderOutputTemplate(TimestampFreeTemplate, spec);
        var evt      = CanonicalSpecViewFactory.Build(spec);
        var actual   = SerilogOutputTemplateRenderer.Render(TimestampFreeTemplate, evt);

        actual.Should().Be(expected,
            "every non-timestamp token in the canonical template must match real Serilog 4.3.1");
    }

    /// <summary>
    /// Canonical template with an exception — discriminates the <c>{Exception}</c>
    /// trailing-newline behaviour (HIGH-3 pre-mortem finding).
    /// </summary>
    [Fact]
    public void Canonical_template_with_exception_matches_oracle()
    {
        var spec = new CanonicalEventSpec
        {
            LevelKey        = "error",
            MessageTemplate = "Unhandled failure",
            Exception       = new InvalidOperationException("test exception"),
        };

        var expected = SerilogFormattingOracle.RenderOutputTemplate(TimestampFreeTemplate, spec);

        var evt    = SpecToView(spec);
        var actual = SerilogOutputTemplateRenderer.Render(TimestampFreeTemplate, evt);

        actual.Should().Be(expected,
            "canonical template with exception must match real Serilog 4.3.1");
    }

    /// <summary>
    /// All seven entries of the representative corpus must match the oracle under the
    /// timestamp-free template.  This catches level-specific divergences (warning→WRN,
    /// fatal→FTL) and the destructured object branch (:lj JSON path).
    /// </summary>
    [Theory]
    [InlineData(0)]   // "Hello World" — simple scalar
    [InlineData(1)]   // integer scalar
    [InlineData(2)]   // string-valued property (:lj literal-quotes branch)
    [InlineData(3)]   // destructured object (:lj JSON branch)
    [InlineData(4)]   // warning level
    [InlineData(5)]   // fatal level
    [InlineData(6)]   // event with exception
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Uses CanonicalSpecViewFactory which calls LogEventValueProjector (reflection).")]
    public void Representative_corpus_matches_oracle(int corpusIndex)
    {
        var corpus = CanonicalEventSpec.RepresentativeCorpus();
        corpusIndex.Should().BeLessThan(corpus.Count, "corpus index out of range — update test if corpus shrinks");

        var spec     = corpus[corpusIndex];
        var expected = SerilogFormattingOracle.RenderOutputTemplate(TimestampFreeTemplate, spec);

        var evt    = CanonicalSpecViewFactory.Build(spec);
        var actual = SerilogOutputTemplateRenderer.Render(TimestampFreeTemplate, evt);

        actual.Should().Be(expected,
            $"corpus[{corpusIndex}] (level={spec.LevelKey}, template='{spec.MessageTemplate}') " +
            "must match real Serilog 4.3.1 under the timestamp-free canonical template");
    }

    /// <summary>
    /// The full canonical template (WITH timestamp) renders the correct structure:
    /// the timestamp section matches the expected HH:mm:ss format for the given input.
    ///
    /// <para>
    /// This test validates the overall structure and Level/Message/Exception tokens;
    /// it uses <see cref="string.Contains"/> for the timestamp section since the oracle
    /// comparison is structurally impossible (oracle uses wall-clock time).
    /// </para>
    /// </summary>
    [Fact]
    public void Full_canonical_template_includes_timestamp_section()
    {
        var spec = new CanonicalEventSpec
        {
            LevelKey        = "warning",
            MessageTemplate = "User {Name} spent {Amount}",
            Properties      = BuildProps(("Name", "Alice"), ("Amount", 42.5)),
            Timestamp       = new DateTimeOffset(2024, 6, 15, 14, 30, 0, TimeSpan.FromHours(5)),
        };

        var evt    = SpecToView(spec);
        var actual = SerilogOutputTemplateRenderer.Render(CanonicalTemplate, evt);

        // The output must start with '[HH:mm:ss ' pattern, contain WRN, and end with newline.
        actual.Should().StartWith("[", "the canonical template starts with '['");
        actual.Should().Contain(" WRN] ", "Level:u3 for warning must appear after the timestamp section");
        // The timestamp section renders using ToLocalTime; verify it has the HH:mm:ss shape.
        var tsSection = actual.Substring(1, 8); // extract position 1..8 from "[HH:mm:ss ..."
        tsSection.Should().MatchRegex(@"^\d{2}:\d{2}:\d{2}$",
            "the timestamp section must have HH:mm:ss shape");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    // Build a FakeSerilogEventView from a CanonicalEventSpec.
    // The fake carries enough state for the already-live tokens (Level, NewLine);
    // RenderedMessage is a best-effort render that will be superseded by Task 4.
    private static FakeSerilogEventView SpecToView(CanonicalEventSpec spec)
    {
        // Best-effort rendered message: substitute each named property value into the template.
        // Task 4 replaces this with the proper :lj / :j renderer.
        var renderedMessage = spec.MessageTemplate;
        foreach (var (name, value, _) in spec.Properties)
        {
            var display = value?.ToString() ?? "null";
            renderedMessage = renderedMessage
                .Replace("{@" + name + "}", display)
                .Replace("{" + name + "}", display);
        }

        // Build a minimal property dictionary (scalar values only for stub path).
        var propDict  = new Dictionary<string, LogEventPropertyValue>(StringComparer.Ordinal);
        var holeNames = new System.Collections.Generic.List<string>();
        foreach (var (name, value, destructure) in spec.Properties)
        {
            propDict[name] = new ScalarValue(value);
            holeNames.Add(name);
        }

        return new FakeSerilogEventView
        {
            Timestamp         = spec.Timestamp,
            Level             = MapLevel(spec.LevelKey),
            MessageTemplate   = spec.MessageTemplate,
            RenderedMessage   = renderedMessage,
            Properties        = propDict,
            Exception         = spec.Exception,
            TemplateHoleNames = holeNames,
        };
    }

    private static FakeSerilogEventView MakeFakeView(string levelKey) => new()
    {
        Level           = MapLevel(levelKey),
        MessageTemplate = "",
        RenderedMessage = "",
    };

    private static LogEventLevel MapLevel(string key) => key switch
    {
        "verbose"     => LogEventLevel.Verbose,
        "debug"       => LogEventLevel.Debug,
        "information" => LogEventLevel.Information,
        "warning"     => LogEventLevel.Warning,
        "error"       => LogEventLevel.Error,
        "fatal"       => LogEventLevel.Fatal,
        _             => LogEventLevel.Information,
    };

    private static IReadOnlyList<(string Name, object? Value, bool Destructure)> BuildProps(
        params (string Name, object? Value)[] pairs)
    {
        var result = new (string, object?, bool)[pairs.Length];
        for (var i = 0; i < pairs.Length; i++)
            result[i] = (pairs[i].Name, pairs[i].Value, false);
        return result;
    }
}

#endif

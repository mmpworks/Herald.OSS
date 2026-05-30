#nullable enable
#if NET9_0_OR_GREATER

using FluentAssertions;
using MMP.Herald.Serilog.Formatting;
using Xunit;

namespace MMP.Herald.OSS.Tests.Output.Serilog;

public sealed class SerilogOutputTemplateParserTests
{
    // -------------------------------------------------------------------------
    // Round-trip smoke tests — every listed template must parse without error.
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("{Message}")]
    [InlineData("[{Level:u3}]")]
    [InlineData("{Level,-5:w}")]
    [InlineData("{Timestamp:HH:mm:ss}")]
    [InlineData("{{literal}}")]
    [InlineData("{Properties:j}")]
    [InlineData("{Level,5:u3}")]           // right-align, positive alignment
    [InlineData("{Level,-5}")]             // left-align, no format
    [InlineData("{Message:lj}")]           // the canonical specifier
    [InlineData("[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}] {Message}")]  // complex
    public void Parser_round_trips_the_template(string template)
    {
        var tokens = SerilogOutputTemplateParser.Parse(template);
        tokens.Should().NotBeNull();
        tokens.Should().NotBeEmpty();
    }

    // -------------------------------------------------------------------------
    // Escape sequences
    // -------------------------------------------------------------------------

    [Fact]
    public void Escaped_braces_become_literal_text()
    {
        var tokens = SerilogOutputTemplateParser.Parse("{{literal}}");
        tokens.Should().HaveCount(1);
        var text = tokens[0].Should().BeOfType<TextToken>().Subject;
        text.Text.Should().Be("{literal}");
    }

    // -------------------------------------------------------------------------
    // Alignment and format parsing
    // -------------------------------------------------------------------------

    [Fact]
    public void Hole_with_alignment_and_format_parsed_correctly()
    {
        var tokens = SerilogOutputTemplateParser.Parse("{Level,-5:u3}");
        var hole = tokens.Should().ContainSingle().Which.Should().BeOfType<HoleToken>().Subject;
        hole.Name.Should().Be("Level");
        hole.Alignment.Should().Be(-5);
        hole.Format.Should().Be("u3");
    }

    [Fact]
    public void Hole_with_positive_alignment_and_format_parsed_correctly()
    {
        var tokens = SerilogOutputTemplateParser.Parse("{Level,5:u3}");
        var hole = tokens.Should().ContainSingle().Which.Should().BeOfType<HoleToken>().Subject;
        hole.Name.Should().Be("Level");
        hole.Alignment.Should().Be(5);
        hole.Format.Should().Be("u3");
    }

    [Fact]
    public void Hole_with_alignment_and_no_format()
    {
        var tokens = SerilogOutputTemplateParser.Parse("{Level,-5}");
        var hole = tokens.Should().ContainSingle().Which.Should().BeOfType<HoleToken>().Subject;
        hole.Name.Should().Be("Level");
        hole.Alignment.Should().Be(-5);
        hole.Format.Should().BeNull();
    }

    // -------------------------------------------------------------------------
    // The HH:mm:ss trap — colons inside a format string are NOT delimiters.
    // -------------------------------------------------------------------------

    [Fact]
    public void Colon_inside_format_is_not_a_delimiter()
    {
        // {Timestamp:HH:mm:ss} — the colons inside HH:mm:ss must NOT split the token.
        var tokens = SerilogOutputTemplateParser.Parse("{Timestamp:HH:mm:ss}");
        var hole = tokens.Should().ContainSingle().Which.Should().BeOfType<HoleToken>().Subject;
        hole.Name.Should().Be("Timestamp");
        hole.Format.Should().Be("HH:mm:ss");
    }

    [Fact]
    public void Complex_datetime_format_with_timezone_preserved()
    {
        var tokens = SerilogOutputTemplateParser.Parse("{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}");
        var hole = tokens.Should().ContainSingle().Which.Should().BeOfType<HoleToken>().Subject;
        hole.Name.Should().Be("Timestamp");
        hole.Format.Should().Be("yyyy-MM-dd HH:mm:ss.fff zzz");
        hole.Alignment.Should().Be(0);
    }

    // -------------------------------------------------------------------------
    // Mixed text and holes
    // -------------------------------------------------------------------------

    [Fact]
    public void Mixed_text_and_holes()
    {
        // "[{Level:u3}] {Message}" → "[", Level hole, "] ", Message hole
        var tokens = SerilogOutputTemplateParser.Parse("[{Level:u3}] {Message}");
        tokens.Should().HaveCount(4);
        tokens[0].Should().BeOfType<TextToken>().Which.Text.Should().Be("[");
        tokens[1].Should().BeOfType<HoleToken>().Which.Name.Should().Be("Level");
        tokens[2].Should().BeOfType<TextToken>().Which.Text.Should().Be("] ");
        tokens[3].Should().BeOfType<HoleToken>().Which.Name.Should().Be("Message");
    }

    [Fact]
    public void Hole_with_name_only_has_zero_alignment_and_null_format()
    {
        var tokens = SerilogOutputTemplateParser.Parse("{Message}");
        var hole = tokens.Should().ContainSingle().Which.Should().BeOfType<HoleToken>().Subject;
        hole.Name.Should().Be("Message");
        hole.Alignment.Should().Be(0);
        hole.Format.Should().BeNull();
    }

    // -------------------------------------------------------------------------
    // Edge cases
    // -------------------------------------------------------------------------

    [Fact]
    public void Empty_template_returns_empty_list()
    {
        SerilogOutputTemplateParser.Parse("").Should().BeEmpty();
    }

    [Fact]
    public void Null_template_returns_empty_list()
    {
        SerilogOutputTemplateParser.Parse(null).Should().BeEmpty();
    }

    [Fact]
    public void Malformed_hole_no_close_brace_becomes_literal()
    {
        // "{unclosed" → one TextToken whose text starts with '{'
        var tokens = SerilogOutputTemplateParser.Parse("{unclosed");
        tokens.Should().ContainSingle().Which.Should().BeOfType<TextToken>();
    }

    [Fact]
    public void Literal_text_only_template()
    {
        var tokens = SerilogOutputTemplateParser.Parse("hello world");
        tokens.Should().ContainSingle().Which.Should().BeOfType<TextToken>()
            .Which.Text.Should().Be("hello world");
    }
}

#endif

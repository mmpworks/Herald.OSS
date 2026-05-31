#nullable enable

using FluentAssertions;
using MMP.Herald.Generators;
using Xunit;

namespace MMP.Herald.OSS.Tests.Generators;

/// <summary>
/// Golden-file regression tests for <see cref="HeraldLogGenerator"/>.
/// Drives a Roslyn CSharpGeneratorDriver against a known input and
/// asserts on stable substrings of the generated output. Catches a
/// generator change that compiles but flips semantics — the gap the
/// principal review (#15) called out.
/// </summary>
public sealed class HeraldLogGeneratorGoldenTests
{
    [Fact]
    public void HeraldLog_method_generates_body_with_level_check_and_log_call()
    {
        const string source = """
            using MMP.Herald.Pipeline;

            namespace Sample;

            public static partial class Loggers
            {
                [HeraldLog(Level = "info", Category = "App", Message = "user {UserId} signed in")]
                public static partial void EmitUserSignIn(StructuredLogger logger, string userId);
            }
            """;

        var trees = GeneratorGoldenTestHarness.RunGenerator(new HeraldLogGenerator(), source);
        var text = GeneratorGoldenTestHarness.AllGeneratedText(trees);

        trees.Should().NotBeEmpty("the generator must emit at least one syntax tree for a [HeraldLog]-decorated method");
        text.Should().Contain("EmitUserSignIn",
            "the generated method must keep the original signature name");
        text.Should().Contain("KnownLogLevels.Information",
            "Level=\"info\" must resolve to the matching KnownLogLevels accessor at compile time");
        text.Should().Contain("user {UserId} signed in",
            "the template body must round-trip into the generated Log call");
    }

    [Fact]
    public void HeraldLog_escapes_control_characters_in_message_template()
    {
        // The CodeRabbit-flagged regression: EscapeString needs to handle
        // \r \n \t \0 so a message with a newline produces compilable code.
        // Glenn 1 fixed this in commit fb85a85; the golden test pins the
        // post-fix behaviour so a future generator edit cannot regress it.
        const string source = """
            using MMP.Herald.Pipeline;

            namespace Sample;

            public static partial class Loggers
            {
                [HeraldLog(Level = "warn", Category = "App", Message = "line\nbreak {Token}")]
                public static partial void EmitWithNewline(StructuredLogger logger, string token);
            }
            """;

        var trees = GeneratorGoldenTestHarness.RunGenerator(new HeraldLogGenerator(), source);
        var text = GeneratorGoldenTestHarness.AllGeneratedText(trees);

        // The newline must NOT survive as a raw newline in a string literal
        // (that would produce non-compilable code). Either escaped \\n or
        // hex-escaped \\u000A is acceptable.
        text.Should().NotContain("\"line\nbreak",
            "raw newlines inside string literals produce non-compilable C# — the escape path must intercept them");
        (text.Contains("line\\nbreak") || text.Contains("line\\u000Abreak") || text.Contains("line\\u000abreak"))
            .Should().BeTrue("newline must be escaped as \\n or \\uXXXX in the generated literal");
    }
}

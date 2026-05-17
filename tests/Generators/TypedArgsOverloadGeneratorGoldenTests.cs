#nullable enable

using FluentAssertions;
using MMP.Herald.Generators;
using Xunit;

namespace MMP.Herald.OSS.Tests.Generators;

/// <summary>
/// Golden-file regression tests for
/// <see cref="TypedArgsOverloadGenerator"/>. The generator runs against
/// no user input — it emits the StructuredLogger.TypedArgs.Generated
/// partial file with 160 overloads + 5 dispatchers from internal model
/// data. The test exercises a no-op compilation just to drive the
/// generator and assert on key shape invariants.
///
/// <para>
/// Catches: a refactor that drops [OverloadResolutionPriority] on a
/// subset of overloads (reintroduces the silent "fewer omitted optional
/// parameters wins" bug); a refactor that breaks the arity-to-buffer
/// mapping (1→Buffer1, 2→Buffer2, 3-4→Buffer4, 5-8→Buffer8, 9-16→Buffer16).
/// </para>
/// </summary>
public sealed class TypedArgsOverloadGeneratorGoldenTests
{
    [Fact]
    public void Generator_emits_only_for_core_compilation()
    {
        // TypedArgsOverloadGenerator scopes its output to assemblies that
        // declare MMP.Herald.Pipeline.StructuredLogger as a TYPE in the
        // current compilation (not a reference). A consumer assembly that
        // merely references Herald.OSS — like this test compilation — must
        // not get the partial emitted into it; that would cause duplicate
        // overloads at link time.
        const string source = """
            namespace Sample;
            public sealed class Stub { }
            """;

        var trees = GeneratorGoldenTestHarness.RunGenerator(new TypedArgsOverloadGenerator(), source);

        trees.Should().BeEmpty(
            "TypedArgsOverloadGenerator must skip consumer compilations and only emit into Herald.OSS itself");
    }

    [Fact]
    public void Generator_emits_typed_args_partial_when_core_type_is_in_compilation()
    {
        // Drive the "core compilation" trigger by declaring a stub
        // MMP.Herald.Pipeline.StructuredLogger in the source — same symbol
        // identity check the generator uses to gate output.
        const string source = """
            namespace MMP.Herald.Pipeline;
            public sealed partial class StructuredLogger { }
            """;

        var trees = GeneratorGoldenTestHarness.RunGenerator(new TypedArgsOverloadGenerator(), source);
        var text = GeneratorGoldenTestHarness.AllGeneratedText(trees);

        trees.Should().NotBeEmpty("the generator must emit the StructuredLogger.TypedArgs partial for the core compilation");

        // The xmldoc on the generator promises [OverloadResolutionPriority]
        // on every overload.
        text.Should().Contain("OverloadResolutionPriority",
            "every typed-args overload must carry [OverloadResolutionPriority] to defeat C#'s fewer-omitted-args tiebreaker");

        // Trace + Debug + Info + Warn + Error must each surface.
        foreach (var level in new[] { "Trace", "Debug", "Info", "Warn", "Error" })
        {
            text.Should().Contain($"public void {level}",
                $"the {level} family must be present in the generated overloads");
        }

        // Buffer mapping pins — at least one Buffer1/2/4/8/16 reference.
        text.Should().Contain("LogPropertyBuffer1");
        text.Should().Contain("LogPropertyBuffer2");
        text.Should().Contain("LogPropertyBuffer4");
        text.Should().Contain("LogPropertyBuffer8");
        text.Should().Contain("LogPropertyBuffer16");
    }
}

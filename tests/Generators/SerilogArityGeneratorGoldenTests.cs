#nullable enable

using FluentAssertions;
using MMP.Herald.Generators;
using Xunit;

namespace MMP.Herald.OSS.Tests.Generators;

/// <summary>
/// Golden-file regression tests for <see cref="SerilogArityGenerator"/>.
/// The generator runs against no user input — it emits the
/// SerilogLoggerAdapter.Holes.Generated partial file with 6×16 typed
/// overloads from its internal model data.
///
/// <para>
/// Key invariants this test pins:
/// <list type="bullet">
///   <item>Only fires for the <c>MMP.Herald.Serilog</c> compilation (identity gate).</item>
///   <item>Emits the six Serilog levels (Verbose/Debug/Information/Warning/Error/Fatal).</item>
///   <item>Every overload carries [OverloadResolutionPriority(arity)].</item>
///   <item>Hole names come from <c>NameAt(</c> — NOT from CallerArgumentExpression.</item>
///   <item>Each hole's capture mode is packed via <c>CaptureModeAt(</c>.</item>
///   <item>All five buffer sizes (1/2/4/8/16) appear for correct arity bands.</item>
/// </list>
/// </para>
/// </summary>
public sealed class SerilogArityGeneratorGoldenTests
{
    [Fact]
    public void Generator_skips_non_Serilog_compilations()
    {
        const string source = "namespace Other; public class Stub {}";
        var trees = GeneratorGoldenTestHarness.RunGenerator(new SerilogArityGenerator(), source);
        trees.Should().BeEmpty("the generator must not emit for non-Serilog compilations");
    }

    [Fact]
    public void Generator_emits_hole_named_overloads_for_the_compat_compilation()
    {
        // Trigger identity gate by providing the marker type SerilogLoggerAdapter.
        const string source = """
            namespace MMP.Herald.Serilog;
            public sealed partial class SerilogLoggerAdapter { }
            """;

        var trees = GeneratorGoldenTestHarness.RunGenerator(new SerilogArityGenerator(), source);
        var text = GeneratorGoldenTestHarness.AllGeneratedText(trees);

        trees.Should().NotBeEmpty("the generator must emit for the Serilog compat compilation");
        text.Should().Contain("OverloadResolutionPriority",
            "every typed-args overload must carry [OverloadResolutionPriority]");

        // The SIX Serilog levels — including Fatal which the native generator does not emit.
        foreach (var level in new[] { "Verbose", "Debug", "Information", "Warning", "Error", "Fatal" })
        {
            text.Should().Contain($"public void {level}<",
                $"{level} typed family must be present");
        }

        // DISTINGUISHING invariant: Serilog names from holes, NOT from CallerArgumentExpression.
        text.Should().NotContain("[CallerArgumentExpression",
            "Serilog names from template holes, not arg source text");
        text.Should().Contain("NameAt(",
            "hole names must be resolved via positional index, not CallerArgumentExpression");

        // Each hole packs its capture mode into the compact slot via CaptureModeAt.
        // No fast/slow branch — every hole rides the compact buffer regardless of mode.
        text.Should().Contain("CaptureModeAt(",
            "each hole must pack its capture mode into the compact slot");

        // Buffer size mapping pins — same as native generator.
        foreach (var buf in new[] { "LogPropertyBuffer1", "LogPropertyBuffer2",
                                    "LogPropertyBuffer4", "LogPropertyBuffer8",
                                    "LogPropertyBuffer16" })
        {
            text.Should().Contain(buf, $"{buf} must be present for the correct arity band");
        }
    }
}

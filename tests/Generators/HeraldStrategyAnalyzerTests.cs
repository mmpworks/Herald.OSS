#nullable enable

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using MMP.Herald.Generators;
using Xunit;

namespace MMP.Herald.OSS.Tests.Generators;

/// <summary>
/// Roslyn-driven coverage for <see cref="HeraldStrategyAnalyzer"/> HERALD004
/// after the 0.6.0 symbol-binding tighten. Confirms the analyzer fires on
/// <c>QuickLogBuilder.Build()</c> calls without a sink AND stays silent on
/// look-alike <c>.Build()</c> calls in consumer code (e.g. test helpers
/// such as <c>LogEventBuilder.Build()</c> in Modules/Server/tests/Helpers/).
///
/// <para>
/// Pre-0.6.0 the HERALD004 check name-matched <c>.Build()</c> globally
/// (filtered only by the presence of a <c>Create</c> call earlier in the
/// chain). That fired on every <c>LogEventBuilder.Create(...).Build()</c>
/// site in Server tests — 9 false positives. The fix binds the diagnostic
/// to the actual <see cref="MMP.Herald.Quick.QuickLogBuilder.Build"/>
/// symbol via <see cref="SemanticModel"/>.
/// </para>
/// </summary>
public sealed class HeraldStrategyAnalyzerTests
{
    [Fact]
    public async Task HERALD004_fires_on_QuickLogBuilder_Build_without_sink()
    {
        const string source = """
            using MMP.Herald.Quick;

            internal static class Program
            {
                public static void Main()
                {
                    var result = QuickLogBuilder.Create()
                        .WithMinimumLevel("debug")
                        .Build();
                    _ = result;
                }
            }
            """;

        var diagnostics = await RunAnalyzer(source);

        diagnostics
            .Where(d => d.Id == "HERALD004")
            .Should().HaveCount(1,
                "HERALD004 must fire on a QuickLogBuilder.Build() call that has no sink configured");
    }

    [Fact]
    public async Task HERALD004_does_not_fire_on_QuickLogBuilder_Build_with_sink()
    {
        const string source = """
            using MMP.Herald.Quick;

            internal static class Program
            {
                public static void Main()
                {
                    var result = QuickLogBuilder.Create()
                        .WithConsoleSink()
                        .Build();
                    _ = result;
                }
            }
            """;

        var diagnostics = await RunAnalyzer(source);

        diagnostics
            .Where(d => d.Id == "HERALD004")
            .Should().BeEmpty(
                "HERALD004 must stay silent when a sink (WithConsoleSink) is configured");
    }

    [Fact]
    public async Task HERALD004_does_not_fire_on_unrelated_Builder_Build_call()
    {
        // Mirrors Modules/Server/tests/Helpers/LogEventBuilder.cs — a fluent
        // builder with the same .Create() / .Build() shape but in a
        // completely unrelated type. Pre-0.6.0 the analyzer's
        // name-match-only check flagged this as HERALD004. Post-0.6.0
        // the symbol bind to MMP.Herald.Quick.QuickLogBuilder keeps it silent.
        const string source = """
            namespace UnrelatedConsumer
            {
                public sealed class LogEventBuilder
                {
                    public static LogEventBuilder Create() => new();
                    public LogEventBuilder WithLevel(string level) => this;
                    public LogEventBuilder WithMessage(string template) => this;
                    public string Build() => "unrelated-build-result";
                }

                internal static class Program
                {
                    public static void Main()
                    {
                        var s = LogEventBuilder.Create()
                            .WithLevel("Info")
                            .WithMessage("hello")
                            .Build();
                        _ = s;
                    }
                }
            }
            """;

        var diagnostics = await RunAnalyzer(source);

        diagnostics
            .Where(d => d.Id == "HERALD004")
            .Should().BeEmpty(
                "HERALD004 must symbol-bind to MMP.Herald.Quick.QuickLogBuilder and " +
                "stay silent on unrelated Builder.Build() chains in consumer code");
    }

    // -- Harness ---------------------------------------------------------
    //
    // Mirrors HeraldNamingPolicyAssertionAnalyzerTests.RunAnalyzer — same
    // trusted-platform-assemblies pull + Herald.OSS metadata reference so
    // the SemanticModel can resolve MMP.Herald.Quick.QuickLogBuilder.

    private static async Task<ImmutableArray<Diagnostic>> RunAnalyzer(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var references = BuildReferences();
        var compilation = CSharpCompilation.Create(
            assemblyName: "Herald.OSS.AnalyzerTest",
            syntaxTrees: new[] { tree },
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var analyzer = new HeraldStrategyAnalyzer();
        var compilationWithAnalyzers = compilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(analyzer));

        return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
    }

    private static ImmutableArray<MetadataReference> BuildReferences()
    {
        var trustedPlatformAssemblies = (string?)System.AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty;
        var refs = new List<MetadataReference>();
        foreach (var path in trustedPlatformAssemblies.Split(System.IO.Path.PathSeparator))
        {
            if (!string.IsNullOrEmpty(path) && System.IO.File.Exists(path))
            {
                refs.Add(MetadataReference.CreateFromFile(path));
            }
        }
        // Herald.OSS itself — needed so the SemanticModel can resolve
        // MMP.Herald.Quick.QuickLogBuilder during symbol binding.
        refs.Add(MetadataReference.CreateFromFile(typeof(MMP.Herald.Quick.QuickLogBuilder).Assembly.Location));
        return refs.ToImmutableArray();
    }
}

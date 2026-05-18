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
/// Roslyn-driven coverage for <see cref="HeraldNamingPolicyAssertionAnalyzer"/>
/// (HRLD0051). Verifies the analyzer fires only when the consumer's
/// <c>HeraldNamingPolicyAssertion</c> MSBuild property is set to
/// <c>Default</c>, and only on calls to the Herald-owned override APIs.
/// </summary>
public sealed class HeraldNamingPolicyAssertionAnalyzerTests
{
    [Fact]
    public async Task WithNamingPolicy_in_asserting_assembly_fires_HRLD0051()
    {
        const string source = """
            using MMP.Herald.Quick;
            using MMP.Herald.Templating.NamingPolicies;

            internal static class Program
            {
                public static void Main()
                {
                    var b = QuickLogBuilder.Create()
                        .WithNamingPolicy(SnakeCasePolicy.Instance);
                    _ = b;
                }
            }
            """;

        var diagnostics = await RunAnalyzer(source, assertion: "Default");

        diagnostics
            .Where(d => d.Id == HeraldNamingPolicyAssertionAnalyzer.OverrideCalledId)
            .Should().HaveCount(1,
                "HRLD0051 should fire once for the WithNamingPolicy call in an asserting assembly");
    }

    [Fact]
    public async Task WithNamingPolicy_without_assertion_does_not_fire()
    {
        const string source = """
            using MMP.Herald.Quick;
            using MMP.Herald.Templating.NamingPolicies;

            internal static class Program
            {
                public static void Main()
                {
                    var b = QuickLogBuilder.Create()
                        .WithNamingPolicy(SnakeCasePolicy.Instance);
                    _ = b;
                }
            }
            """;

        var diagnostics = await RunAnalyzer(source, assertion: null);

        diagnostics
            .Where(d => d.Id == HeraldNamingPolicyAssertionAnalyzer.OverrideCalledId)
            .Should().BeEmpty("HRLD0051 should stay silent without the assertion");
    }

    [Fact]
    public async Task Non_Herald_WithNamingPolicy_does_not_fire_even_when_asserting()
    {
        // A same-named extension method on an unrelated type must NOT fire
        // HRLD0051 — the analyzer's semantic check filters by namespace.
        const string source = """
            namespace MyApp
            {
                public static class Unrelated
                {
                    public static MyApp.Bag WithNamingPolicy(this MyApp.Bag b, string p) => b;
                }

                public sealed class Bag { }

                internal static class Program
                {
                    public static void Main()
                    {
                        var bag = new Bag().WithNamingPolicy("ignored");
                        _ = bag;
                    }
                }
            }
            """;

        var diagnostics = await RunAnalyzer(source, assertion: "Default");

        diagnostics
            .Where(d => d.Id == HeraldNamingPolicyAssertionAnalyzer.OverrideCalledId)
            .Should().BeEmpty(
                "HRLD0051 must only fire on Herald-namespaced override APIs");
    }

    // -- Harness ---------------------------------------------------------

    private static async Task<ImmutableArray<Diagnostic>> RunAnalyzer(string source, string? assertion)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var references = BuildReferences();
        var compilation = CSharpCompilation.Create(
            assemblyName: "Herald.OSS.AnalyzerTest",
            syntaxTrees: new[] { tree },
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var analyzer = new HeraldNamingPolicyAssertionAnalyzer();
        var options = new TestAnalyzerOptions(assertion);
        var compilationWithAnalyzers = compilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(analyzer),
            options);

        return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
    }

    private static ImmutableArray<MetadataReference> BuildReferences()
    {
        // Same trick as GeneratorGoldenTestHarness — pull the loaded
        // platform assemblies + Herald.OSS itself.
        var trustedPlatformAssemblies = (string?)System.AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty;
        var refs = new List<MetadataReference>();
        foreach (var path in trustedPlatformAssemblies.Split(System.IO.Path.PathSeparator))
        {
            if (!string.IsNullOrEmpty(path) && System.IO.File.Exists(path))
            {
                refs.Add(MetadataReference.CreateFromFile(path));
            }
        }
        refs.Add(MetadataReference.CreateFromFile(typeof(MMP.Herald.Pipeline.StructuredLogger).Assembly.Location));
        return refs.ToImmutableArray();
    }

    private sealed class TestAnalyzerOptions : AnalyzerOptions
    {
        public TestAnalyzerOptions(string? assertion)
            : base(ImmutableArray<AdditionalText>.Empty,
                   new TestConfigOptionsProvider(assertion))
        {
        }
    }

    private sealed class TestConfigOptionsProvider : AnalyzerConfigOptionsProvider
    {
        private readonly TestConfigOptions _global;

        public TestConfigOptionsProvider(string? assertion)
        {
            var bag = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
            if (assertion is not null)
            {
                bag["build_property.HeraldNamingPolicyAssertion"] = assertion;
            }
            _global = new TestConfigOptions(bag);
        }

        public override AnalyzerConfigOptions GlobalOptions => _global;

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => _global;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => _global;
    }

    private sealed class TestConfigOptions : AnalyzerConfigOptions
    {
        private readonly IReadOnlyDictionary<string, string> _bag;

        public TestConfigOptions(IReadOnlyDictionary<string, string> bag) => _bag = bag;

        public override bool TryGetValue(string key, out string value)
        {
            if (_bag.TryGetValue(key, out var v))
            {
                value = v;
                return true;
            }
            value = string.Empty;
            return false;
        }
    }
}

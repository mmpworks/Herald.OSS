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
/// Roslyn-driven coverage for <see cref="HeraldExternalInjectionAnalyzer"/>
/// (HRLD0060). The analyzer flags use of the AllowExternalEventInjection() switch
/// at build time - the conspicuousness nudge that pairs with the runtime drop
/// notice and the disclaimer. MT1 from the security review pins the two
/// load-bearing behaviours: the warning fires at the call site, and the
/// assembly-wide MSBuild acknowledgement (HeraldAllowExternalInjection=true)
/// suppresses it. HeraldStrictMode escalation and the false-positive guard are
/// covered too, since suppression and escalation are the mechanisms counsel and
/// the ADR rely on.
/// </summary>
public sealed class HeraldExternalInjectionAnalyzerTests
{
    // A minimal stand-in for the real QuickLogBuilder. Same namespace
    // (MMP.Herald.Quick) and type name, with the AllowExternalEventInjection()
    // method the analyzer binds to by symbol identity. Using a fixture keeps the
    // test independent of the full builder API surface while exercising the real
    // matching path (namespace + type name + method name).
    private const string BuilderFixture = """
        namespace MMP.Herald.Quick
        {
            public sealed class QuickLogBuilder
            {
                public QuickLogBuilder AllowExternalEventInjection() => this;
            }
        }
        """;

    private static string CallSite(string body) => BuilderFixture + """

        internal static class Program
        {
            public static void Main()
            {
                var b = new MMP.Herald.Quick.QuickLogBuilder();
        """ + body + """

            }
        }
        """;

    [Fact]
    public async Task Calling_the_switch_fires_HRLD0060()
    {
        var source = CallSite("        b.AllowExternalEventInjection();");

        var diagnostics = await RunAnalyzer(source, buildProperties: null);

        diagnostics
            .Where(d => d.Id == HeraldExternalInjectionAnalyzer.DiagnosticId)
            .Should().HaveCount(1,
                "AllowExternalEventInjection() must be flagged at the call site");
    }

    [Fact]
    public async Task MSBuild_acknowledgement_suppresses_HRLD0060()
    {
        // The blessed assembly-wide acknowledgement. Setting
        // HeraldAllowExternalInjection=true is the deliberate consent that
        // silences the analyzer across the assembly.
        var source = CallSite("        b.AllowExternalEventInjection();");
        var props = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase)
        {
            ["build_property.HeraldAllowExternalInjection"] = "true",
        };

        var diagnostics = await RunAnalyzer(source, props);

        diagnostics
            .Where(d => d.Id == HeraldExternalInjectionAnalyzer.DiagnosticId)
            .Should().BeEmpty(
                "HeraldAllowExternalInjection=true is the compile-time acknowledgement and suppresses HRLD0060");
    }

    [Fact]
    public async Task StrictMode_escalates_HRLD0060_to_error()
    {
        // HeraldStrictMode=true turns the warning into a build error for teams
        // that want a hard gate. The runtime behaviour is unaffected.
        var source = CallSite("        b.AllowExternalEventInjection();");
        var props = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase)
        {
            ["build_property.HeraldStrictMode"] = "true",
        };

        var diagnostics = await RunAnalyzer(source, props);

        var hits = diagnostics
            .Where(d => d.Id == HeraldExternalInjectionAnalyzer.DiagnosticId)
            .ToList();
        hits.Should().HaveCount(1, "the switch call still fires once under strict mode");
        hits[0].Severity.Should().Be(DiagnosticSeverity.Error,
            "HeraldStrictMode escalates HRLD0060 from Warning to Error");
    }

    [Fact]
    public async Task Unrelated_method_named_like_the_switch_does_not_fire()
    {
        // A same-named method on an unrelated type must not trip the analyzer.
        // Symbol identity (namespace + type) is what matters, not the name.
        const string source = """
            namespace MyApp
            {
                public sealed class Bag
                {
                    public Bag AllowExternalEventInjection() => this;
                }

                internal static class Program
                {
                    public static void Main()
                    {
                        var bag = new Bag().AllowExternalEventInjection();
                        _ = bag;
                    }
                }
            }
            """;

        var diagnostics = await RunAnalyzer(source, buildProperties: null);

        diagnostics
            .Where(d => d.Id == HeraldExternalInjectionAnalyzer.DiagnosticId)
            .Should().BeEmpty(
                "an unrelated AllowExternalEventInjection method must never trip HRLD0060");
    }

    // -- Harness ---------------------------------------------------------

    private static async Task<ImmutableArray<Diagnostic>> RunAnalyzer(
        string source, IReadOnlyDictionary<string, string>? buildProperties)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var references = BuildReferences();
        var compilation = CSharpCompilation.Create(
            assemblyName: "Herald.OSS.ExternalInjectionAnalyzerTest",
            syntaxTrees: new[] { tree },
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var analyzer = new HeraldExternalInjectionAnalyzer();
        var options = new TestAnalyzerOptions(buildProperties);
        var compilationWithAnalyzers = compilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(analyzer), options);

        return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
    }

    private static ImmutableArray<MetadataReference> BuildReferences()
    {
        var trustedPlatformAssemblies =
            (string?)System.AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty;
        var refs = new List<MetadataReference>();
        foreach (var path in trustedPlatformAssemblies.Split(System.IO.Path.PathSeparator))
        {
            if (!string.IsNullOrEmpty(path) && System.IO.File.Exists(path))
            {
                refs.Add(MetadataReference.CreateFromFile(path));
            }
        }
        return refs.ToImmutableArray();
    }

    private sealed class TestAnalyzerOptions : AnalyzerOptions
    {
        public TestAnalyzerOptions(IReadOnlyDictionary<string, string>? buildProperties)
            : base(ImmutableArray<AdditionalText>.Empty,
                   new TestConfigOptionsProvider(buildProperties))
        {
        }
    }

    private sealed class TestConfigOptionsProvider : AnalyzerConfigOptionsProvider
    {
        private readonly TestConfigOptions _global;

        public TestConfigOptionsProvider(IReadOnlyDictionary<string, string>? buildProperties)
        {
            var bag = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
            if (buildProperties is not null)
            {
                foreach (var pair in buildProperties) bag[pair.Key] = pair.Value;
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

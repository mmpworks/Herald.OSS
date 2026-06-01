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
/// Roslyn-driven coverage for <see cref="HeraldSerilogBindingAnalyzer"/>
/// (HRLD0003). The analyzer warns a C# &lt; 13 consumer that a typed Serilog
/// overload call at hazardous arity may silently bind to a lower-arity overload
/// (because <c>[OverloadResolutionPriority]</c> is ignored before C# 13).
///
/// <para>
/// These tests pin the conservative-correctness contract: the rule MUST fire on
/// a C# 12 consumer at arity 2+, and MUST stay silent on C# 13+, on single-arg
/// calls, and on unrelated methods named like a level. False positives here are
/// worse than the gap, so the silence cases carry the weight.
/// </para>
/// </summary>
public sealed class HeraldSerilogBindingAnalyzerTests
{
    // C# 13 == LanguageVersion value 1300. The pinned Roslyn (4.8.0) predates the
    // named LanguageVersion.CSharp13 member, so reference it by its stable numeric
    // value — exactly as the analyzer under test does (it cannot name CSharp13
    // either). When the consumer compiler is Roslyn 4.13+, 1300 resolves to C# 13.
    private const LanguageVersion CSharp13 = (LanguageVersion)1300;

    // A minimal stand-in for the generated typed Serilog surface. Same namespace
    // (MMP.Herald.Serilog), same type name (SerilogLoggerAdapter), same generic
    // typed-overload shape + [OverloadResolutionPriority]. The analyzer binds by
    // symbol identity (namespace + type name + generic method-type-parameter
    // value params), so this fixture exercises the real matching path without
    // needing the full Herald.OSS generated source in the test compilation.
    private const string AdapterFixture = """
        namespace System.Runtime.CompilerServices
        {
            internal sealed class OverloadResolutionPriorityAttribute : System.Attribute
            {
                public OverloadResolutionPriorityAttribute(int priority) { }
            }
        }
        namespace MMP.Herald.Serilog
        {
            public sealed class SerilogLoggerAdapter
            {
                // params fallback (lower priority).
                public void Information(string messageTemplate, params object?[]? values) { }

                // Typed overloads (high priority) — generic, level-named.
                [System.Runtime.CompilerServices.OverloadResolutionPriority(1)]
                public void Information<T1>(string messageTemplate, T1 v1) { }

                [System.Runtime.CompilerServices.OverloadResolutionPriority(2)]
                public void Information<T1, T2>(string messageTemplate, T1 v1, T2 v2) { }

                [System.Runtime.CompilerServices.OverloadResolutionPriority(3)]
                public void Information<T1, T2, T3>(string messageTemplate, T1 v1, T2 v2, T3 v3) { }
            }
        }
        """;

    private static string CallSite(string body) => AdapterFixture + """

        internal static class Program
        {
            public static void Main()
            {
                var log = new MMP.Herald.Serilog.SerilogLoggerAdapter();
        """ + body + """

            }
        }
        """;

    [Fact]
    public async Task CSharp12_consumer_arity3_typed_call_fires_HRLD0003()
    {
        var source = CallSite("        log.Information(\"a {A} b {B} c {C}\", 1, 2, 3);");

        var diagnostics = await RunAnalyzer(source, LanguageVersion.CSharp12);

        diagnostics
            .Where(d => d.Id == HeraldSerilogBindingAnalyzer.DiagnosticId)
            .Should().HaveCount(1,
                "a C# 12 consumer calling a typed overload at arity 3 hits the silent-miscount hazard");
    }

    [Fact]
    public async Task CSharp12_consumer_arity2_typed_call_fires_HRLD0003()
    {
        // Arity 2 is the floor where a lower-arity bind can drop a value into a
        // CallerArgumentExpression slot.
        var source = CallSite("        log.Information(\"a {A} b {B}\", 1, 2);");

        var diagnostics = await RunAnalyzer(source, LanguageVersion.CSharp12);

        diagnostics
            .Where(d => d.Id == HeraldSerilogBindingAnalyzer.DiagnosticId)
            .Should().HaveCount(1,
                "arity 2 is the hazard floor on a C# 12 consumer");
    }

    [Fact]
    public async Task CSharp12_consumer_arity1_typed_call_does_not_fire()
    {
        // A single value arg cannot be miscounted — there is no trailing arg to
        // consume as a CallerArgumentExpression override.
        var source = CallSite("        log.Information(\"a {A}\", 1);");

        var diagnostics = await RunAnalyzer(source, LanguageVersion.CSharp12);

        diagnostics
            .Where(d => d.Id == HeraldSerilogBindingAnalyzer.DiagnosticId)
            .Should().BeEmpty(
                "a single value arg is unambiguous — no silent miscount is possible");
    }

    [Fact]
    public async Task CSharp13_consumer_arity3_typed_call_does_not_fire()
    {
        // On C# 13 [OverloadResolutionPriority] is honored — the call binds to
        // the correct typed overload. No hazard, no diagnostic.
        var source = CallSite("        log.Information(\"a {A} b {B} c {C}\", 1, 2, 3);");

        var diagnostics = await RunAnalyzer(source, CSharp13);

        diagnostics
            .Where(d => d.Id == HeraldSerilogBindingAnalyzer.DiagnosticId)
            .Should().BeEmpty(
                "a C# 13 consumer honors [OverloadResolutionPriority] — no miscount, no warning");
    }

    [Fact]
    public async Task CSharp12_unrelated_method_named_like_a_level_does_not_fire()
    {
        // A method named "Information" on an unrelated type must not trip the
        // analyzer. Symbol identity (namespace + type) is what matters, not the
        // name. This is the canonical false-positive guard.
        const string source = """
            internal static class Unrelated
            {
                public static void Information<T1, T2>(string template, T1 a, T2 b) { }

                public static void Main()
                {
                    Information("x {A} y {B}", 1, 2);
                }
            }
            """;

        var diagnostics = await RunAnalyzer(source, LanguageVersion.CSharp12);

        diagnostics
            .Where(d => d.Id == HeraldSerilogBindingAnalyzer.DiagnosticId)
            .Should().BeEmpty(
                "an unrelated method named Information must never trip HRLD0003");
    }

    // -- Harness ---------------------------------------------------------

    private static async Task<ImmutableArray<Diagnostic>> RunAnalyzer(
        string source, LanguageVersion languageVersion)
    {
        var tree = CSharpSyntaxTree.ParseText(
            source, new CSharpParseOptions(languageVersion));
        var references = BuildReferences();
        var compilation = CSharpCompilation.Create(
            assemblyName: "Herald.OSS.SerilogBindingAnalyzerTest",
            syntaxTrees: new[] { tree },
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var analyzer = new HeraldSerilogBindingAnalyzer();
        var compilationWithAnalyzers = compilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(analyzer));

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
}

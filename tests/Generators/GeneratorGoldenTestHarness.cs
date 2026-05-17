#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace MMP.Herald.OSS.Tests.Generators;

/// <summary>
/// Shared helpers for golden-file regression tests against the
/// Herald.OSS source generators (principal-review queue #15). The
/// harness assembles a Roslyn CSharpCompilation, runs the supplied
/// generator, and exposes the generated SyntaxTrees so callers can
/// assert on their text or syntactic shape.
///
/// <para>
/// Golden-file in spirit: we don't snapshot a checked-in .verified.cs
/// file (the codebase has no Verify/Snapshooter dependency), instead
/// the tests assert on stable substrings + count invariants over the
/// generated output. That's enough to surface a regression where a
/// generator change compiles but flips semantics — exactly the gap the
/// review flagged.
/// </para>
/// </summary>
internal static class GeneratorGoldenTestHarness
{
    /// <summary>
    /// Compile <paramref name="source"/> with <paramref name="generator"/>
    /// attached, return the generator's added syntax trees. The
    /// compilation references Herald.OSS so generators can resolve
    /// HeraldLog / HeraldLogSink / LogPropertyCompact symbols.
    /// </summary>
    public static IReadOnlyList<SyntaxTree> RunGenerator(IIncrementalGenerator generator, string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        var references = BuildReferences();
        var compilation = CSharpCompilation.Create(
            assemblyName: "Herald.OSS.GoldenTest",
            syntaxTrees: new[] { syntaxTree },
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver.Create(generator);
        var ran = driver.RunGenerators(compilation);
        var result = ran.GetRunResult();

        // Filter out the input syntax tree; only return what the generator emitted.
        var input = new HashSet<SyntaxTree> { syntaxTree };
        var generated = new List<SyntaxTree>();
        foreach (var tree in result.GeneratedTrees)
        {
            if (input.Contains(tree)) continue;
            generated.Add(tree);
        }
        return generated;
    }

    /// <summary>
    /// Convenience: concatenate all generated source into one string so a
    /// test can search for stable substrings without iterating trees.
    /// </summary>
    public static string AllGeneratedText(IReadOnlyList<SyntaxTree> trees)
    {
        if (trees.Count == 0) return string.Empty;
        return string.Join("\n\n// ---- next generated file ----\n\n",
            trees.Select(t => t.ToString()));
    }

    private static ImmutableArray<MetadataReference> BuildReferences()
    {
        // Reference Herald.OSS + the BCL the generated code targets. Using
        // the assemblies the test process already loaded means we don't
        // hand-curate a reference set.
        var trustedPlatformAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty;
        var references = new List<MetadataReference>();
        foreach (var path in trustedPlatformAssemblies.Split(System.IO.Path.PathSeparator))
        {
            if (!string.IsNullOrEmpty(path) && System.IO.File.Exists(path))
            {
                references.Add(MetadataReference.CreateFromFile(path));
            }
        }
        // Add Herald.OSS itself.
        references.Add(MetadataReference.CreateFromFile(typeof(MMP.Herald.Pipeline.StructuredLogger).Assembly.Location));
        return references.ToImmutableArray();
    }
}

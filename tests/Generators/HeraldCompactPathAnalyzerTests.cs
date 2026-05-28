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
/// Roslyn-driven coverage for <see cref="HeraldCompactPathAnalyzer"/>
/// (HERALD014). Verifies the analyzer fires when an axis-bearing
/// LogProperty construction appears inside a LogPropertyCompact
/// constructor or factory argument, and stays silent on the
/// default-axis shapes and on full-path API usage.
/// </summary>
public sealed class HeraldCompactPathAnalyzerTests
{
    [Fact]
    public async Task LogProperty_Silent_inside_LogPropertyCompact_ctor_fires_HERALD014()
    {
        // Lossy boundary: LogProperty.Silent constructs an axis-bearing
        // property, then its .Value is taken into the compact form — the
        // Visibility=Silent axis is dropped.
        const string source = """
            using System;
            using MMP.Herald.Pipeline.Kernel;
            using MMP.Herald.Templating;

            internal static class Program
            {
                public static void Main()
                {
                    var c = new LogPropertyCompact(
                        "trace", LogProperty.Silent("trace", "x").Value);
                    _ = c;
                }
            }
            """;

        var diagnostics = await RunAnalyzer(source);

        diagnostics
            .Where(d => d.Id == HeraldCompactPathAnalyzer.DiagnosticId)
            .Should().HaveCount(1,
                "HERALD014 should fire when LogProperty.Silent is constructed inside a LogPropertyCompact ctor argument");
    }

    [Fact]
    public async Task LogProperty_Lazy_inside_LogPropertyCompact_From_fires_HERALD014()
    {
        const string source = """
            using System;
            using MMP.Herald.Pipeline.Kernel;
            using MMP.Herald.Templating;

            internal static class Program
            {
                public static void Main()
                {
                    var c = LogPropertyCompact.From<object?>(
                        "trace", LogProperty.Lazy("trace", () => "x").Value);
                    _ = c;
                }
            }
            """;

        var diagnostics = await RunAnalyzer(source);

        diagnostics
            .Where(d => d.Id == HeraldCompactPathAnalyzer.DiagnosticId)
            .Should().HaveCount(1,
                "HERALD014 should fire when LogProperty.Lazy is constructed inside LogPropertyCompact.From");
    }

    [Fact]
    public async Task LogProperty_named_axis_ctor_inside_LogPropertyCompact_ctor_fires_HERALD014()
    {
        const string source = """
            using System;
            using MMP.Herald.Pipeline.Kernel;
            using MMP.Herald.Templating;

            internal static class Program
            {
                public static void Main()
                {
                    var c = new LogPropertyCompact(
                        "trace",
                        new LogProperty("trace", "x", Visibility: LogPropertyVisibility.Silent).Value);
                    _ = c;
                }
            }
            """;

        var diagnostics = await RunAnalyzer(source);

        diagnostics
            .Where(d => d.Id == HeraldCompactPathAnalyzer.DiagnosticId)
            .Should().HaveCount(1,
                "HERALD014 should fire when a named-axis LogProperty ctor flows through a LogPropertyCompact ctor");
    }

    [Fact]
    public async Task LogProperty_plain_ctor_inside_LogPropertyCompact_ctor_does_not_fire()
    {
        // A plain (name, value) LogProperty constructed inline carries no
        // non-default axis. The analyzer must stay silent on the supported
        // shape — only axis-bearing properties are lossy on the compact path.
        const string source = """
            using System;
            using MMP.Herald.Pipeline.Kernel;
            using MMP.Herald.Templating;

            internal static class Program
            {
                public static void Main()
                {
                    var c = new LogPropertyCompact(
                        "trace", new LogProperty("trace", "x").Value);
                    _ = c;
                }
            }
            """;

        var diagnostics = await RunAnalyzer(source);

        diagnostics
            .Where(d => d.Id == HeraldCompactPathAnalyzer.DiagnosticId)
            .Should().BeEmpty(
                "HERALD014 must not fire on a default-axis LogProperty constructed inline");
    }

    [Fact]
    public async Task LogProperty_Silent_used_on_full_LogProperty_path_does_not_fire()
    {
        // The full-path API accepts the Silent-tagged LogProperty without
        // loss. The analyzer is scoped to compact-path APIs; calls into the
        // full-path API are correct usage and must stay silent.
        const string source = """
            using System;
            using MMP.Herald.Templating;

            internal static class Program
            {
                public static void Main()
                {
                    var p = LogProperty.Silent("trace", "x");
                    Receive(p);
                }

                private static void Receive(LogProperty property) { _ = property; }
            }
            """;

        var diagnostics = await RunAnalyzer(source);

        diagnostics
            .Where(d => d.Id == HeraldCompactPathAnalyzer.DiagnosticId)
            .Should().BeEmpty(
                "HERALD014 must only fire on calls into compact-path APIs");
    }

    // -- Harness ---------------------------------------------------------

    private static async Task<ImmutableArray<Diagnostic>> RunAnalyzer(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var references = BuildReferences();
        var compilation = CSharpCompilation.Create(
            assemblyName: "Herald.OSS.AnalyzerTest",
            syntaxTrees: new[] { tree },
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var analyzer = new HeraldCompactPathAnalyzer();
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
        refs.Add(MetadataReference.CreateFromFile(typeof(MMP.Herald.Pipeline.StructuredLogger).Assembly.Location));
        return refs.ToImmutableArray();
    }
}

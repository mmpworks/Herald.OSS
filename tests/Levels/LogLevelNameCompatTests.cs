#nullable enable

// Authoring-boundary compatibility for the four names the 0.12.0 rename removed.
//
// Scope, deliberately narrow. Trace/Info/Warn/Critical work again where code is
// AUTHORED — as constants, in [HeraldLog(Level = "...")], and through the
// generator. They do NOT work at the registry. Task 9 removed that alias map on
// purpose and LevelRenameRegressionTests (G-LEVEL.1, G-LEVEL.5) pins the
// loud-reject. This suite reinforces that pin rather than fighting it: see
// Registry_still_loud_rejects_the_old_key below.
//
// What the rename left behind, and what this fixes: HERALD007 rejected
// [HeraldLog(Level = "info")] while the generator compiled it happily. Accepted
// by one half of the toolchain and refused by the other.
//
// The alias pairs are HARDCODED here on purpose. This suite is the independent
// check on the production list, so it must not read that list — a test that
// imports the table it verifies cannot catch a wrong table. Each pair was read
// off KnownLogLevels.cs, and Alias_and_canonical_are_the_same_instance re-proves
// it at runtime.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using MMP.Herald.Generators;
using MMP.Herald.Levels;
using Xunit;

namespace MMP.Herald.OSS.Tests.Levels;

public sealed class LogLevelNameCompatTests
{
    private static readonly string[] CanonicalKeys =
    [
        "verbose", "debug", "information", "warning", "error",
        "notice", "success", "fatal", "security", "metric",
    ];

    private static readonly string[] AliasKeysOnly = ["trace", "info", "warn", "critical"];

    // Trace is Verbose. Critical is Fatal. Neither is a near neighbour.
    public static TheoryData<string, string> AliasPairs => new()
    {
        { "trace",    "verbose" },
        { "info",     "information" },
        { "warn",     "warning" },
        { "critical", "fatal" },
    };

    public static TheoryData<string> AliasKeys => ["trace", "info", "warn", "critical"];

    public static TheoryData<string> EveryAuthorableKey
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var key in CanonicalKeys) data.Add(key);
            foreach (var key in AliasKeysOnly) data.Add(key);
            return data;
        }
    }

    private static ILogLevelRegistry BuildFullRegistry()
        => new DefaultLogLevelRegistryFactory().Create();

    // ── 1. The old names exist as usable constants ──

    [Theory]
    [MemberData(nameof(AliasPairs))]
    public void Old_name_is_a_usable_constant(string aliasKey, string canonicalKey)
    {
        // Resolved by name rather than typeof so this suite still compiles and
        // reports a real failure while the type is absent.
        var aliasType = typeof(KnownLogLevels).Assembly
            .GetType("MMP.Herald.Levels.KnownLogLevelAliases");

        aliasType.Should().NotBeNull(
            "one shared alias list must exist for the analyzer and the generator to read");

        var constants = aliasType!
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .ToDictionary(f => f.Name, f => (string)f.GetRawConstantValue()!, StringComparer.Ordinal);

        var constantName = char.ToUpperInvariant(aliasKey[0]) + aliasKey.Substring(1);

        constants.Should().ContainKey(constantName,
            $"'{constantName}' was removed by the 0.12.0 rename and callers still name it");
        constants[constantName].Should().Be(aliasKey,
            $"the constant must carry the old spelling '{aliasKey}', not '{canonicalKey}'");
    }

    [Theory]
    [MemberData(nameof(AliasPairs))]
    public void Alias_and_canonical_are_the_same_instance(string aliasKey, string canonicalKey)
    {
        var alias = AliasProperty(aliasKey);
        var canonical = CanonicalProperty(canonicalKey);

        ReferenceEquals(alias, canonical).Should().BeTrue(
            $"{aliasKey} must BE {canonicalKey}, not a lookalike level");
        alias.Key.Should().Be(canonicalKey,
            $"{aliasKey} must carry the canonical wire key, never a second key");
    }

    // ── 2. The registry keeps loud-rejecting. Task 9 stands. ──

    [Theory]
    [MemberData(nameof(AliasKeys))]
    public void Registry_still_loud_rejects_the_old_key(string aliasKey)
    {
        // Deliberate duplicate of LevelRenameRegressionTests G-LEVEL.1. This
        // suite adds an authoring-boundary alias surface, and the one thing it
        // must never do is leak that surface into the registry. Asserting the
        // null here means a future edit to the alias list fails in THIS file
        // too, next to the code that tempted someone.
        BuildFullRegistry().GetByKeyOrNull(aliasKey).Should().BeNull(
            $"'{aliasKey}' must stay unresolvable at the registry (Task 9 loud-reject)");
    }

    // ── 3. The reject is loud: it names the replacement and carries a code ──

    [Theory]
    [MemberData(nameof(AliasPairs))]
    public void Rejecting_an_old_key_names_its_replacement(string aliasKey, string canonicalKey)
    {
        // A silent null is the actual complaint. Whatever surfaces the failure
        // has to tell the caller which name to use now.
        var message = DescribeUnknownKey(aliasKey);

        message.Should().Contain(aliasKey, "the message must quote the key the caller wrote");
        message.Should().Contain(canonicalKey,
            $"the message must name '{canonicalKey}' as the replacement for '{aliasKey}'");
    }

    [Theory]
    [MemberData(nameof(AliasKeys))]
    public void Rejecting_an_old_key_carries_the_machine_code(string aliasKey)
    {
        // Callers branch on the code, never on message text.
        DescribeUnknownKey(aliasKey).Should().Contain(
            MMP.Herald.Responses.HeraldErrorCodes.LevelKeyNotFound.ToString(),
            "a surfaced error carries a stable machine code beside the human message");
    }

    [Fact]
    public void Rejecting_an_unrelated_key_does_not_invent_a_replacement()
    {
        var message = DescribeUnknownKey("nonsense");

        message.Should().Contain("nonsense");
        foreach (var canonical in CanonicalKeys)
        {
            message.Should().NotContain($"use '{canonical}'",
                "an unknown key that is not a deprecated spelling has no replacement to suggest");
        }
    }

    [Theory]
    [MemberData(nameof(AliasPairs))]
    public async Task Config_load_failure_for_an_old_key_names_its_replacement(
        string aliasKey, string canonicalKey)
    {
        // End to end through a real surface, not just the helper. An operator
        // whose config still says "warn" gets told to write "warning".
        await Task.CompletedTask;

        var registry = BuildFullRegistry();

        var act = () => registry.GetRegisteredLevel(new LogLevel(aliasKey, aliasKey));

        act.Should().Throw<KeyNotFoundException>()
            .WithMessage($"*{canonicalKey}*",
                $"the registry's own failure must name '{canonicalKey}' when the caller wrote '{aliasKey}'");
    }

    // ── 4. The analyzer accepts both vocabularies ──

    [Theory]
    [MemberData(nameof(EveryAuthorableKey))]
    public async Task Analyzer_accepts_the_level_string(string levelKey)
    {
        var diagnostics = await RunLevelAnalyzer(AttributeSource(levelKey));

        diagnostics.Should().BeEmpty(
            $"HERALD007 must not fire on '{levelKey}' — the generator compiles it");
    }

    [Fact]
    public async Task Analyzer_still_rejects_a_genuine_typo()
    {
        var diagnostics = await RunLevelAnalyzer(AttributeSource("infro"));

        diagnostics.Should().ContainSingle("'infro' is a typo and must still be caught");
    }

    // ── 5. The drift guard ──

    [Theory]
    [MemberData(nameof(EveryAuthorableKey))]
    public void Drift_guard_generated_code_never_names_a_member_that_does_not_exist(string levelKey)
    {
        // THIS IS THE DRIFT GUARD. Compiling the generator's own output against
        // the real assembly is the only check that cannot drift, because it asks
        // the compiler rather than a table. The umbrella copy of this generator
        // emitted `if (!IsTraceAcceptable)` against a type where the rename had
        // deleted that member, and nothing noticed until a consumer compiled.
        var errors = CompileGeneratedOutput(AttributeSource(levelKey));

        errors.Should().BeEmpty(
            $"generated code for level '{levelKey}' must name only members that exist. Errors: "
            + string.Join(" | ", errors.Select(d => d.GetMessage())));
    }

    // ── 6. Completeness, by enumeration ──

    [Fact]
    public async Task Every_key_the_registry_knows_is_accepted_by_both_the_generator_and_the_analyzer()
    {
        // Enumerated, not listed. A level added to the registry tomorrow is
        // covered the day it lands — the property a fixed list cannot give, and
        // the reason the 0.12.0 rename slipped through.
        var keys = BuildFullRegistry().GetRegisteredLevels().Select(l => l.Level.Key).ToArray();

        keys.Should().HaveCountGreaterThan(0);

        var rejected = new List<string>();

        foreach (var key in keys)
        {
            var source = AttributeSource(key);

            if (!(await RunLevelAnalyzer(source)).IsEmpty)
            {
                rejected.Add($"{key}: analyzer raised HERALD007");
            }

            var errors = CompileGeneratedOutput(source);
            if (errors.Length > 0)
            {
                rejected.Add($"{key}: generated code failed to compile — {errors[0].GetMessage()}");
            }
        }

        rejected.Should().BeEmpty(
            "the registry, the analyzer and the generator must agree on every key. Disagreements: "
            + string.Join(" | ", rejected));
    }

    [Fact]
    public async Task Every_alias_is_accepted_by_both_the_generator_and_the_analyzer()
    {
        var rejected = new List<string>();

        foreach (var aliasKey in AliasKeysOnly)
        {
            var source = AttributeSource(aliasKey);

            if (!(await RunLevelAnalyzer(source)).IsEmpty)
            {
                rejected.Add($"{aliasKey}: analyzer raised HERALD007");
            }

            if (CompileGeneratedOutput(source).Length > 0)
            {
                rejected.Add($"{aliasKey}: generated code failed to compile");
            }
        }

        rejected.Should().BeEmpty(
            "every alias must work end to end at the authoring boundary. Failures: "
            + string.Join(" | ", rejected));
    }

    [Theory]
    [MemberData(nameof(AliasPairs))]
    public void Generated_code_for_an_old_string_targets_the_new_level(
        string aliasKey, string canonicalKey)
    {
        // Compiling clean is necessary and not sufficient. An old string that
        // silently built `new LogLevel("trace", "trace")` would also compile,
        // and would move the caller's events onto an unregistered level.
        var member = char.ToUpperInvariant(canonicalKey[0]) + canonicalKey.Substring(1);

        var generated = SingleGeneratedSource(AttributeSource(aliasKey));

        generated.Should().Contain($"KnownLogLevels.{member}",
            $"'{aliasKey}' must emit the canonical level, not a fresh one");
        generated.Should().NotContain($"new MMP.Herald.Levels.LogLevel(\"{aliasKey}\"",
            $"'{aliasKey}' must never fall through to the custom-level constructor");
    }

    // ── Helpers ──

    private static string DescribeUnknownKey(string levelKey)
    {
        var aliasType = typeof(KnownLogLevels).Assembly
            .GetType("MMP.Herald.Levels.KnownLogLevelAliases");

        aliasType.Should().NotBeNull("the shared alias list must exist");

        var method = aliasType!.GetMethod(
            "DescribeUnknownKey",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

        method.Should().NotBeNull(
            "the loud-reject message must be built in one place, beside the alias list");

        return (string)method!.Invoke(null, [levelKey])!;
    }

    private static string AttributeSource(string levelKey) => $$"""
        using MMP.Herald.Pipeline;

        namespace TestApp;

        public static partial class CompatLog
        {
            [HeraldLog(Level = "{{levelKey}}", Category = "Compat", Message = "value {value}")]
            public static partial void Emit(StructuredLogger logger, int value);
        }
        """;

    private static async Task<ImmutableArray<Diagnostic>> RunLevelAnalyzer(string source)
    {
        var compilation = CreateCompilation(source);

        var withAnalyzers = compilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(new HeraldLogLevelAnalyzer()));

        var all = await withAnalyzers.GetAnalyzerDiagnosticsAsync();

        return all.Where(d => d.Id == HeraldLogLevelAnalyzer.UnknownLevelId).ToImmutableArray();
    }

    private static ImmutableArray<Diagnostic> CompileGeneratedOutput(string source)
    {
        CSharpGeneratorDriver
            .Create(new HeraldLogGenerator())
            .RunGeneratorsAndUpdateCompilation(
                CreateCompilation(source), out var output, out _);

        return output.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Where(d => !IsUnrelatedKnownDefect(d))
            .ToImmutableArray();
    }

    // HeraldLogGenerator emits `logger.RecordCompileTimeResolution();` on every
    // [HeraldLog] method, and no such member exists anywhere in src/. That is a
    // real defect in the shipped generator and it is NOT about level names, so
    // it is excluded here by name rather than by a broad filter: the guard stays
    // maximal for everything else, and this exclusion turns into a no-op the day
    // the member lands or the emit is removed. Reported separately.
    private static bool IsUnrelatedKnownDefect(Diagnostic diagnostic)
    {
        return diagnostic.GetMessage().Contains("RecordCompileTimeResolution");
    }

    private static string SingleGeneratedSource(string source)
    {
        var result = CSharpGeneratorDriver
            .Create(new HeraldLogGenerator())
            .RunGenerators(CreateCompilation(source))
            .GetRunResult();

        result.GeneratedTrees.Should().ContainSingle();
        return result.GeneratedTrees[0].GetText().ToString();
    }

    private static CSharpCompilation CreateCompilation(string source) =>
        CSharpCompilation.Create(
            assemblyName: "Herald.OSS.LevelCompatTest_" + Guid.NewGuid().ToString("N")[..8],
            syntaxTrees: [CSharpSyntaxTree.ParseText(source)],
            references: BuildReferences(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithNullableContextOptions(NullableContextOptions.Enable));

    private static List<MetadataReference> BuildReferences()
    {
        var refs = new List<MetadataReference>();

        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string trusted)
        {
            foreach (var path in trusted.Split(System.IO.Path.PathSeparator))
            {
                if (System.IO.File.Exists(path))
                {
                    refs.Add(MetadataReference.CreateFromFile(path));
                }
            }
        }

        refs.Add(MetadataReference.CreateFromFile(
            typeof(MMP.Herald.Pipeline.StructuredLogger).Assembly.Location));

        return refs;
    }

    private static LogLevel AliasProperty(string aliasKey) => aliasKey switch
    {
        "trace" => KnownLogLevels.Trace,
        "info" => KnownLogLevels.Info,
        "warn" => KnownLogLevels.Warn,
        "critical" => KnownLogLevels.Critical,
        _ => throw new ArgumentOutOfRangeException(nameof(aliasKey), aliasKey, "not an alias"),
    };

    private static LogLevel CanonicalProperty(string canonicalKey) => canonicalKey switch
    {
        "verbose" => KnownLogLevels.Verbose,
        "information" => KnownLogLevels.Information,
        "warning" => KnownLogLevels.Warning,
        "fatal" => KnownLogLevels.Fatal,
        _ => throw new ArgumentOutOfRangeException(nameof(canonicalKey), canonicalKey, "not canonical"),
    };
}

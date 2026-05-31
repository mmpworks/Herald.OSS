#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using Xunit;

namespace MMP.Herald.OSS.Tests.Levels;

/// <summary>
/// Cross-table equivalence guard (G-LEVEL.6). Herald carries two independent
/// level-key tables that must agree:
///
/// <list type="bullet">
///   <item>the analyzer table <c>MMP.Herald.Levels.KnownLogLevelKeys</c> —
///         linked into the netstandard2.0 generator so the HERALD007 analyzer
///         can validate <c>[HeraldLog(Level = "...")]</c> strings at edit
///         time, and</item>
///   <item>the runtime table <c>MMP.Herald.Services.LogLevelKeys</c> — the
///         string keys the fluent <c>WithMinimumLevel()</c> / sink APIs
///         accept.</item>
/// </list>
///
/// <para>
/// Nothing in the product compiles these two tables against each other, so
/// they can drift silently. They already have: the runtime table carries
/// <c>"fatal"</c> that the analyzer table does not. This suite is the single
/// place both tables are read together. It pins the intended post-rename key
/// set as the contract and fails until both tables match it.
/// </para>
///
/// <para>
/// The tables expose their keys as <c>public const string</c> fields only
/// (no other string consts are mixed in), so reflection over the literal
/// string fields enumerates each table's value-set unambiguously without
/// adding an enumeration member to the product types.
/// </para>
///
/// <para>
/// The analyzer table's source file is link-shared into both the
/// <c>Herald.OSS</c> runtime assembly and the netstandard2.0 generator
/// assembly, so a bare <c>typeof(KnownLogLevelKeys)</c> is ambiguous in this
/// test project (CS0433). Both table types are therefore resolved by name
/// from the runtime <c>Herald.OSS</c> assembly — the copy that ships.
/// </para>
/// </summary>
public sealed class LevelTableEquivalenceTests
{
    /// <summary>
    /// The intended post-rename key set — the Serilog-aligned vocabulary.
    /// This is the contract. Do NOT weaken it to make the test pass; the
    /// rename task (Task 3) is what makes both tables match this set.
    /// </summary>
    private static readonly IReadOnlySet<string> ExpectedKeys = new HashSet<string>(StringComparer.Ordinal)
    {
        "verbose",
        "debug",
        "information",
        "warning",
        "error",
        "fatal",
        "notice",
        "success",
        "security",
        "metric",
    };

    // The runtime Herald.OSS assembly hosts both table types. We anchor to it
    // via the unambiguous runtime table, then resolve the analyzer table's
    // runtime-linked copy by full name from the same assembly — sidestepping
    // the CS0433 collision with the generator-linked copy.
    private const string AnalyzerTableFullName = "MMP.Herald.Levels.KnownLogLevelKeys";
    private const string RuntimeTableFullName = "MMP.Herald.Services.LogLevelKeys";

    // Anchor to the runtime assembly via the unambiguous runtime table type
    // (LogLevelKeys lives only in Herald.OSS, not the generator assembly).
    private static readonly Assembly RuntimeAssembly =
        typeof(MMP.Herald.Services.LogLevelKeys).Assembly;

    [Fact]
    public void Analyzer_table_key_set_matches_expected_post_rename_set()
    {
        ReadKeyValues(ResolveTable(AnalyzerTableFullName))
            .Should().BeEquivalentTo(ExpectedKeys);
    }

    [Fact]
    public void Runtime_table_key_set_matches_expected_post_rename_set()
    {
        ReadKeyValues(ResolveTable(RuntimeTableFullName))
            .Should().BeEquivalentTo(ExpectedKeys);
    }

    [Fact]
    public void Analyzer_and_runtime_tables_agree_with_each_other()
    {
        // Drift guard: the two tables must expose the same value-set even
        // independent of the expected contract. Today they differ ("fatal").
        var analyzerKeys = ReadKeyValues(ResolveTable(AnalyzerTableFullName));
        var runtimeKeys = ReadKeyValues(ResolveTable(RuntimeTableFullName));

        analyzerKeys.Should().BeEquivalentTo(runtimeKeys);
    }

    /// <summary>
    /// Resolves a level-key table type by full name from the runtime
    /// <c>Herald.OSS</c> assembly, pinning past the duplicate generator-linked
    /// copy of the analyzer table.
    /// </summary>
    private static Type ResolveTable(string fullName)
    {
        var tableType = RuntimeAssembly.GetType(fullName, throwOnError: false);
        tableType.Should().NotBeNull(
            $"{fullName} should be present in the runtime {RuntimeAssembly.GetName().Name} assembly");
        return tableType!;
    }

    /// <summary>
    /// Enumerates the <c>public const string</c> key values declared on a
    /// level-key table type. Compile-time string constants surface as
    /// <see cref="FieldInfo.IsLiteral"/> with <c>string</c> field type.
    /// </summary>
    private static IReadOnlySet<string> ReadKeyValues(Type tableType)
    {
        var values = tableType
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && !field.IsInitOnly && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToHashSet(StringComparer.Ordinal);

        values.Should().NotBeEmpty(
            $"{tableType.FullName} should declare public const string key fields");

        return values;
    }
}

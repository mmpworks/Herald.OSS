#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using MMP.Herald.Pipeline;
using MMP.Herald.Serilog.Events;
using Xunit;

namespace MMP.Herald.OSS.Tests.Serilog;

/// <summary>
/// Guard 1 (Jared/Richard): the zero-alloc engine must not reference the
/// Serilog value-model mirror. Dependency arrow: Serilog→Herald, never Herald→Serilog.
///
/// Granularity note (0.12.0): the Serilog API surface now compiles directly into
/// Herald.OSS.dll (commit 51890e1 — typed overloads need [OverloadResolutionPriority] on
/// net9+, and shipping one package is the advertised experience). Engine and mirror therefore
/// share an assembly. The confinement invariant survives that consolidation but is expressed at
/// NAMESPACE granularity instead of ASSEMBLY granularity: inside the merged assembly the engine
/// is everything NOT under MMP.Herald.Serilog.*, and the mirror is everything under it.
///
/// The invariant that actually protects the hot path: no engine type may have a member
/// (field / parameter / return) typed from the MMP.Herald.Serilog.* mirror namespace. Packaging
/// is irrelevant to that; the type-reference direction is what keeps the zero-alloc path free of
/// the Serilog value-model.
/// </summary>
public sealed class Guard1_MirrorConfinementTests
{
    // Post-consolidation both engine and mirror types live in Herald.OSS.dll; the partition is
    // the namespace prefix, not the assembly boundary.
    private static readonly Assembly HeraldAssembly =
        typeof(StructuredLogger).Assembly;

    // The mirror's root namespace. Every value-model / API-surface type lives at or under it.
    private const string MirrorNamespacePrefix = "MMP.Herald.Serilog";

    [Fact]
    public void Mirror_types_and_engine_types_share_the_one_shipped_assembly()
    {
        // Documents the 0.12.0 packaging decision so a future reader knows the assembly-level
        // guard was retired on purpose, not lost. If this stops being true (a real re-split),
        // revisit whether an assembly-level guard should return.
        typeof(LogEvent).Assembly.Should().BeSameAs(HeraldAssembly,
            "0.12.0 ships the Serilog surface inside Herald.OSS.dll; the mirror is partitioned " +
            "by namespace (MMP.Herald.Serilog.*), not by assembly");
    }

    [Fact]
    public void No_engine_type_has_a_member_typed_from_the_mirror_namespace()
    {
        // Walk every ENGINE type (NOT under MMP.Herald.Serilog.*) and assert none of its fields /
        // method parameters / return types is a mirror type. This is the surviving invariant.
        var violations = new List<string>();

        foreach (var type in HeraldAssembly.GetTypes())
        {
            // Skip mirror types themselves and their compiler-generated closures/anon-types —
            // a mirror type referencing a mirror type is expected, not a leak.
            if (IsMirrorType(type))
                continue;

            foreach (var field in type.GetFields(
                BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Instance | BindingFlags.Static))
            {
                if (ReferencesMirrorNamespace(field.FieldType))
                    violations.Add($"Field {type.FullName}.{field.Name}: {field.FieldType.FullName}");
            }

            foreach (var method in type.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Instance | BindingFlags.Static |
                BindingFlags.DeclaredOnly))
            {
                if (ReferencesMirrorNamespace(method.ReturnType))
                    violations.Add($"Method {type.FullName}.{method.Name} return: {method.ReturnType.FullName}");
                foreach (var param in method.GetParameters())
                {
                    if (ReferencesMirrorNamespace(param.ParameterType))
                        violations.Add($"Param {type.FullName}.{method.Name}({param.Name}): {param.ParameterType.FullName}");
                }
            }
        }

        violations.Should().BeEmpty(
            "no engine type may reference a mirror type — the structural confinement is enforced " +
            "at namespace granularity now that engine and mirror share the shipped assembly");
    }

    // A type belongs to the mirror if its namespace is, or sits under, MMP.Herald.Serilog.
    // Compiler-generated closures/anon-types inherit the namespace of their declaring type,
    // so this correctly treats a mirror closure as a mirror type.
    private static bool IsMirrorType(Type type)
    {
        var ns = type.Namespace;
        return ns is not null
            && (ns == MirrorNamespacePrefix
                || ns.StartsWith(MirrorNamespacePrefix + ".", StringComparison.Ordinal));
    }

    // Recursively checks whether a type (or any of its generic arguments) is a mirror type.
    private static bool ReferencesMirrorNamespace(Type type)
    {
        if (IsMirrorType(type)) return true;
        if (type.IsGenericType)
            return type.GetGenericArguments().Any(ReferencesMirrorNamespace);
        return false;
    }
}

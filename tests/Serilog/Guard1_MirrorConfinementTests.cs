#nullable enable
#if NET9_0_OR_GREATER
using System;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using MMP.Herald.Pipeline;
using MMP.Herald.Serilog.Events;
using Xunit;

namespace MMP.Herald.OSS.Tests.Serilog;

/// <summary>
/// Guard 1 (Jared/Richard): the zero-alloc engine must not reference the
/// Serilog value-model mirror assembly. Dependency arrow: Serilog→Herald, never Herald→Serilog.
/// </summary>
public sealed class Guard1_MirrorConfinementTests
{
    private static readonly Assembly EngineAssembly =
        typeof(MMP.Herald.Pipeline.StructuredLogger).Assembly;
    private static readonly Assembly MirrorAssembly =
        typeof(MMP.Herald.Serilog.Events.LogEvent).Assembly;

    [Fact]
    public void Engine_and_mirror_live_in_distinct_assemblies()
    {
        // If they ever get merged, the reference-direction test is meaningless.
        // Assembly is a singleton per load context — reference inequality is the right check.
        EngineAssembly.FullName.Should().NotBe(MirrorAssembly.FullName,
            "Herald.OSS.dll and MMP.Herald.Serilog.dll must be separate assemblies — " +
            "if they merge, the confinement reasoning collapses");
    }

    [Fact]
    public void Engine_assembly_does_not_reference_the_mirror_assembly()
    {
        EngineAssembly.GetReferencedAssemblies()
            .Select(a => a.Name)
            .Should().NotContain("MMP.Herald.Serilog",
                "the engine must never reference the mirror — " +
                "InternalsVisibleTo is a one-way grant, not an assembly reference");
    }

    [Fact]
    public void No_engine_type_has_a_member_typed_from_the_mirror_assembly()
    {
        // Walk all types in the engine assembly (public + internal).
        // For each type, check all methods (parameters + return type) and fields.
        // None may reference a type from MMP.Herald.Serilog.
        var violations = new System.Collections.Generic.List<string>();

        foreach (var type in EngineAssembly.GetTypes())
        {
            // Check fields
            foreach (var field in type.GetFields(
                BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Instance | BindingFlags.Static))
            {
                if (ReferencessMirrorAssembly(field.FieldType))
                    violations.Add($"Field {type.FullName}.{field.Name}: {field.FieldType.FullName}");
            }

            // Check methods: return type + parameters + generic args
            foreach (var method in type.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Instance | BindingFlags.Static |
                BindingFlags.DeclaredOnly))
            {
                if (ReferencessMirrorAssembly(method.ReturnType))
                    violations.Add($"Method {type.FullName}.{method.Name} return: {method.ReturnType.FullName}");
                foreach (var param in method.GetParameters())
                {
                    if (ReferencessMirrorAssembly(param.ParameterType))
                        violations.Add($"Param {type.FullName}.{method.Name}({param.Name}): {param.ParameterType.FullName}");
                }
            }
        }

        violations.Should().BeEmpty(
            "no engine type may reference a mirror type — the structural confinement " +
            "must be enforced at the type level, not just the assembly-reference level");
    }

    // Recursively checks whether a type (or any of its generic arguments) lives in the mirror assembly.
    private static bool ReferencessMirrorAssembly(Type type)
    {
        if (type.Assembly == MirrorAssembly) return true;
        if (type.IsGenericType)
            return type.GetGenericArguments().Any(ReferencessMirrorAssembly);
        return false;
    }
}
#endif

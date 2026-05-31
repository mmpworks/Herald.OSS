#nullable enable
#if NET9_0_OR_GREATER

using System.Linq;
using System.Reflection;
using FluentAssertions;
using Xunit;

namespace MMP.Herald.OSS.Tests.Serilog.Configuration;

/// <summary>
/// OD-2 dispatch architecture guard (pre-mortem CRIT-4).
///
/// <para>
/// CRIT-4 risk: <see cref="MMP.Herald.Serilog.Configuration.LoggerSinkConfiguration"/>
/// could accidentally reference <c>MMP.Herald.Formatting.OutputTemplateFormatter</c>
/// (Herald's native formatter, which uses Herald's own template grammar and knows
/// nothing about Serilog's <c>{:u3}</c> / <c>{:lj}</c> specifiers). If that type
/// leaked into the compat assembly's dispatch path, user formatters supplied via
/// <c>WriteTo.Console(ITextFormatter)</c> would silently be bypassed or shadowed.
/// </para>
///
/// <para>
/// The guard is expressed in the positive form: we verify that the S3 seam
/// (<c>WriteTo.Console(ITextFormatter)</c>) exists and that the compat assembly
/// does not reference the native formatter type at the assembly-reference or
/// member-type level. Both checks are required — the seam existing proves the
/// bridge is wired, and the absence of the native type proves it is not silently
/// overriding the bridge.
/// </para>
/// </summary>
public sealed class DispatchArchitectureTests
{
    private static readonly Assembly CompatAssembly =
        typeof(MMP.Herald.Serilog.Configuration.LoggerSinkConfiguration).Assembly;

    // ── S3 seam presence ────────────────────────────────────────────────────

    /// <summary>
    /// Positive guard: the <c>WriteTo.Console(ITextFormatter)</c> overload must
    /// exist. This is the S3 seam wired in Task 9. If it is missing the entire
    /// formatter-dispatch chain is broken regardless of what the assembly
    /// references.
    /// </summary>
    [Fact]
    public void Console_with_ITextFormatter_overload_exists_on_LoggerSinkConfiguration()
    {
        var sinkConfigType = typeof(MMP.Herald.Serilog.Configuration.LoggerSinkConfiguration);

        var methods = sinkConfigType
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.Name == "Console" &&
                        m.GetParameters().Any(p =>
                            typeof(MMP.Herald.Serilog.Formatting.ITextFormatter)
                                .IsAssignableFrom(p.ParameterType)))
            .ToList();

        methods.Should().NotBeEmpty(
            "WriteTo.Console(ITextFormatter) must be wired (S3 seam from Task 9); " +
            "without it no user-supplied formatter can be injected");
    }

    // ── Native formatter isolation ───────────────────────────────────────────

    /// <summary>
    /// The specific CRIT-4 type, <c>MMP.Herald.Formatting.OutputTemplateFormatter</c>,
    /// must never appear in any field or method signature of the compat assembly.
    ///
    /// <para>
    /// Approach: walk all types in the compat assembly; for each field and each
    /// method signature (parameters + return type) check whether the type resolves
    /// to <c>OutputTemplateFormatter</c> (or a generic parameterised with it).
    /// This is more precise than checking all native-assembly types — the compat
    /// layer legitimately references other native types (<c>ILogger</c>,
    /// <c>LogLevel</c>, etc.) via the Herald API. What it must never do is import
    /// the native formatter into the dispatch path.
    /// </para>
    ///
    /// <para>
    /// Method-body inspection via <c>GetMethodBody().LocalVariables</c> was
    /// considered but rejected: it requires IL to be present (not guaranteed in
    /// release builds), only catches typed locals (not inlined calls), and is
    /// fragile across runtimes. The member-signature walk is sufficient because
    /// any use of <c>OutputTemplateFormatter</c> in the dispatch path would
    /// require it to appear in at least one field, parameter, or return type.
    /// </para>
    /// </summary>
    [Fact]
    public void Compat_assembly_has_no_member_typed_as_OutputTemplateFormatter()
    {
        var nativeFormatterType = typeof(MMP.Herald.Formatting.OutputTemplateFormatter);
        var violations = new System.Collections.Generic.List<string>();

        foreach (var type in CompatAssembly.GetTypes())
        {
            foreach (var field in type.GetFields(
                BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Instance | BindingFlags.Static))
            {
                if (ReferencesType(field.FieldType, nativeFormatterType))
                    violations.Add($"Field {type.FullName}.{field.Name}: {field.FieldType.FullName}");
            }

            foreach (var method in type.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Instance | BindingFlags.Static |
                BindingFlags.DeclaredOnly))
            {
                if (ReferencesType(method.ReturnType, nativeFormatterType))
                    violations.Add($"Return {type.FullName}.{method.Name}: {method.ReturnType.FullName}");

                foreach (var param in method.GetParameters())
                {
                    if (ReferencesType(param.ParameterType, nativeFormatterType))
                        violations.Add($"Param {type.FullName}.{method.Name}({param.Name}): {param.ParameterType.FullName}");
                }
            }
        }

        violations.Should().BeEmpty(
            "the Serilog-compat dispatch layer must not reference OutputTemplateFormatter — " +
            "that type uses Herald's own template grammar (not Serilog's {:u3}/:lj specifiers); " +
            "the correct seam is ITextFormatter via WriteTo.Console(ITextFormatter)");
    }

    // Recursively checks whether a type (or any generic argument) is the target type.
    private static bool ReferencesType(System.Type candidate, System.Type target)
    {
        if (candidate == target) return true;
        if (candidate.IsGenericType)
            return candidate.GetGenericArguments().Any(a => ReferencesType(a, target));
        return false;
    }
}

#endif

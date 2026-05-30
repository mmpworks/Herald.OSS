#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MMP.Herald.Generators;

/// <summary>
/// Incremental source generator that emits method bodies for static partial methods
/// decorated with [HeraldLog]. Produces zero-allocation logging calls with pre-baked
/// property names and compile-time level resolution.
///
/// Input:
///   [HeraldLog(Level = "info", Category = "App", Message = "Player {name} scored {pts}")]
///   public static partial void PlayerScored(StructuredLogger logger, string name, int pts);
///
/// Output:
///   public static partial void PlayerScored(StructuredLogger logger, string name, int pts)
///   {
///       if (!logger.IsEnabled(MMP.Herald.Levels.KnownLogLevels.Information)) return;
///       logger.Log(
///           MMP.Herald.Levels.KnownLogLevels.Information,
///           new MMP.Herald.Events.LogCategory("App"),
///           "Player {name} scored {pts}",
///           properties: new MMP.Herald.Templating.LogProperty[]
///           {
///               new("name", name),
///               new("pts", pts)
///           });
///   }
/// </summary>
[Generator]
public sealed class HeraldLogGenerator : IIncrementalGenerator
{
    private const string AttributeFullName = "MMP.Herald.Pipeline.HeraldLogAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Find all partial methods with [HeraldLog]
        var methods = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                AttributeFullName,
                predicate: static (node, _) => node is MethodDeclarationSyntax mds
                    && mds.Modifiers.Any(SyntaxKind.PartialKeyword)
                    && mds.Modifiers.Any(SyntaxKind.StaticKeyword),
                transform: static (ctx, ct) => ExtractModel(ctx, ct))
            .Where(static m => m is not null)
            .Select(static (m, _) => m!);

        // Project-wide naming policy from the MSBuild property
        // `HeraldNamingPolicy` (default "pascal"). Source-gen pre-resolves
        // names against this policy so [HeraldLog] dispatch pays zero
        // runtime cost. An unknown value lands as a build break via the
        // diagnostic in the source output below.
        var policyOption = context.AnalyzerConfigOptionsProvider
            .Select(static (provider, _) =>
            {
                if (provider.GlobalOptions.TryGetValue("build_property.HeraldNamingPolicy", out var value)
                    && !string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
                return "pascal"; // spec default for Herald.OSS 1.0+
            });

        // Combine each method with the policy so RegisterSourceOutput
        // sees both together. The policy is the same for every method in
        // the project so this is essentially broadcasting one value.
        var methodsWithPolicy = methods.Combine(policyOption);

        context.RegisterSourceOutput(methodsWithPolicy, static (ctx, pair) =>
        {
            var (model, projectPolicyId) = pair;

            // Validate the project-wide policy first. Spec: "BUILD BREAK on
            // unknown MSBuild value, NOT silent default." A bad project
            // policy short-circuits before any per-method work runs so the
            // operator sees one clear diagnostic rather than N per-method
            // duplicates of the same root cause.
            if (!IsKnownPolicyId(projectPolicyId))
            {
                ctx.ReportDiagnostic(Diagnostic.Create(
                    UnknownProjectNamingPolicyRule,
                    Location.None,
                    projectPolicyId));
                return;
            }

            // Per-method override (HeraldLog(NamingPolicy = "...")). Null
            // means "use the project default for this method." Unknown
            // non-null values are a build error pointing at the attribute,
            // not at the project — different fix site, distinct diagnostic.
            string effectivePolicyId;
            if (model.NamingPolicyOverride is null)
            {
                effectivePolicyId = projectPolicyId;
            }
            else if (IsKnownPolicyId(model.NamingPolicyOverride))
            {
                effectivePolicyId = model.NamingPolicyOverride;
            }
            else
            {
                ctx.ReportDiagnostic(Diagnostic.Create(
                    UnknownMethodNamingPolicyRule,
                    model.AttributeLocation ?? Location.None,
                    model.NamingPolicyOverride));
                return;
            }

            var source = GenerateSource(model, effectivePolicyId);
            ctx.AddSource($"{model.ContainingType}.{model.MethodName}.g.cs", source);
        });
    }

    private static bool IsKnownPolicyId(string id) =>
        id == "pascal" || id == "camel" || id == "snake";

    private static readonly DiagnosticDescriptor UnknownProjectNamingPolicyRule = new(
        id: "HERALD0410",
        title: "Unknown HeraldNamingPolicy value",
        messageFormat:
            "HeraldNamingPolicy is set to '{0}' which is not a recognised policy id. " +
            "Use one of: 'pascal' (default), 'camel', 'snake'.",
        category: "Herald.OSS",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor UnknownMethodNamingPolicyRule = new(
        id: "HERALD0411",
        title: "Unknown [HeraldLog(NamingPolicy = ...)] value",
        messageFormat:
            "[HeraldLog(NamingPolicy = \"{0}\")] is not a recognised policy id. " +
            "Use one of: \"pascal\" (default), \"camel\", \"snake\", " +
            "or omit the argument to inherit the project HeraldNamingPolicy.",
        category: "Herald.OSS",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    // -- Model extraction (runs in the pipeline, must be fast and deterministic) --

    private static LogMethodModel? ExtractModel(
        GeneratorAttributeSyntaxContext ctx, CancellationToken ct)
    {
        if (ctx.TargetSymbol is not IMethodSymbol method)
            return null;

        // Must be partial, static, returns void
        if (!method.IsPartialDefinition || !method.IsStatic || !method.ReturnsVoid)
            return null;

        // First parameter must be StructuredLogger
        if (method.Parameters.Length == 0)
            return null;

        var firstParam = method.Parameters[0];
        if (firstParam.Type.Name != "StructuredLogger")
            return null;

        // Read attribute values
        var attr = method.GetAttributes().FirstOrDefault(a =>
            a.AttributeClass?.ToDisplayString() == AttributeFullName);
        if (attr is null) return null;

        var level = GetNamedArgString(attr, "Level") ?? "info";
        var category = GetNamedArgString(attr, "Category") ?? "App";
        var message = GetNamedArgString(attr, "Message") ?? "";

        // NamingPolicy is optional. Null means "use the project default."
        // An explicit empty string ("") is treated as null — the source
        // text most often produces that when a user starts typing the
        // value and the build runs mid-edit; failing the build on "" is
        // a worse UX than falling through to the project default.
        var namingPolicyOverride = GetNamedArgString(attr, "NamingPolicy");
        if (string.IsNullOrWhiteSpace(namingPolicyOverride))
        {
            namingPolicyOverride = null;
        }

        // Attribute syntax location so HERALD0411 points at the attribute
        // itself rather than at Location.None or the method signature. The
        // syntax reference resolves cheaply because the attribute is
        // already in the binding context.
        var attributeLocation = attr.ApplicationSyntaxReference is { } syntaxRef
            ? Location.Create(syntaxRef.SyntaxTree, syntaxRef.Span)
            : null;

        // Collect parameters (skip first = logger). ImmutableArray<T>
        // is the Roslyn-recommended container for incremental-pipeline
        // model values — its Equals runs SequenceEqual, so two extract
        // passes that produce identical parameters compare equal and
        // the downstream RegisterSourceOutput stage short-circuits.
        var parametersBuilder = ImmutableArray.CreateBuilder<ParameterModel>(method.Parameters.Length - 1);
        for (var i = 1; i < method.Parameters.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            var p = method.Parameters[i];
            parametersBuilder.Add(new ParameterModel(
                p.Name,
                p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
        }
        var parameters = parametersBuilder.ToImmutable();

        // Containing type info
        var containingType = method.ContainingType;
        var ns = containingType.ContainingNamespace.IsGlobalNamespace
            ? null
            : containingType.ContainingNamespace.ToDisplayString();

        // Accessibility
        var typeAccessibility = AccessibilityToString(containingType.DeclaredAccessibility);
        var methodAccessibility = AccessibilityToString(method.DeclaredAccessibility);

        return new LogMethodModel(
            Namespace: ns,
            ContainingType: containingType.Name,
            TypeAccessibility: typeAccessibility,
            MethodName: method.Name,
            MethodAccessibility: methodAccessibility,
            Level: level,
            Category: category,
            Message: message,
            Parameters: parameters,
            LoggerParameterName: firstParam.Name,
            NamingPolicyOverride: namingPolicyOverride,
            AttributeLocation: attributeLocation);
    }

    private static string? GetNamedArgString(AttributeData attr, string name)
    {
        foreach (var arg in attr.NamedArguments)
        {
            if (arg.Key == name && arg.Value.Value is string s)
                return s;
        }
        return null;
    }

    private static string AccessibilityToString(Accessibility accessibility) => accessibility switch
    {
        Accessibility.Public => "public",
        Accessibility.Internal => "internal",
        Accessibility.Protected => "protected",
        Accessibility.ProtectedOrInternal => "protected internal",
        Accessibility.Private => "private",
        _ => "internal"
    };

    // -- Code generation --
    //
    // P2 (reject path + hot path win):
    //   - Hoist the LogCategory into a per-method `private static readonly`
    //     field. One heap allocation at class-load, not 24 B per call.
    //     Same reasoning as the HERALD006 analyzer flags when users write
    //     `new LogCategory(...)` at a call site.
    //   - Emit `LogCompact` + `LogPropertyBufferN` (InlineArray) for
    //     1..8 parameters. Zero heap allocation at the call site once the
    //     pipeline is kernel-eligible — matching the hand-written Fast.cs
    //     shape. For >8 parameters, fall back to the legacy
    //     `LogProperty[]` path since the buffer family caps at 16 and 9+
    //     is rare enough that the extra indexing isn't worth a new size.

    private const string CategoryType = "global::MMP.Herald.Events.LogCategory";
    private const string CompactType = "global::MMP.Herald.Pipeline.Kernel.LogPropertyCompact";
    private const string RoSpanCompact =
        "global::System.ReadOnlySpan<global::MMP.Herald.Pipeline.Kernel.LogPropertyCompact>";

    private static string GenerateSource(LogMethodModel model, string policyId)
    {
        // Pre-resolve every property name at compile time. The generator
        // applies the active naming policy to (a) the template token name
        // when one exists at the slot index, otherwise (b) the C# parameter
        // name. The resolved name lands as a bare string literal in the
        // emitted code so dispatch pays zero runtime cost for naming.
        var resolvedNames = ResolveCompileTimeNames(model, policyId);

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();

        if (model.Namespace is not null)
        {
            sb.AppendLine($"namespace {model.Namespace};");
            sb.AppendLine();
        }

        sb.AppendLine($"{model.TypeAccessibility} static partial class {model.ContainingType}");
        sb.AppendLine("{");

        // Hoisted category: one allocation at class-load instead of 24 B
        // per call. Unique field name per method so two methods in the
        // same partial class don't clash on category hoisting.
        var categoryField = $"_HeraldLog_Cat_{model.MethodName}";
        sb.AppendLine($"    private static readonly {CategoryType} {categoryField} =");
        sb.AppendLine($"        new(\"{EscapeString(model.Category)}\");");
        sb.AppendLine();

        // Method signature
        sb.Append($"    {model.MethodAccessibility} static partial void {model.MethodName}(");
        sb.Append($"MMP.Herald.Pipeline.StructuredLogger {model.LoggerParameterName}");
        foreach (var p in model.Parameters)
        {
            sb.Append($", {p.Type} {p.Name}");
        }
        sb.AppendLine(")");
        sb.AppendLine("    {");

        // Level guard. For the six levels Herald precomputes a public bool field
        // for (Trace/Debug/Info/Warn/Error/Critical), emit a direct field read —
        // ~1 ns vs the FrozenDictionary-backed IsEnabled(LogLevel) which runs
        // ~5-8 ns even when inlined. Other known levels and custom levels fall
        // back to IsEnabled because they have no precomputed field.
        var levelField = LevelKeyToField(model.Level);
        var fastField = LevelKeyToAcceptableField(model.Level);
        var guard = fastField is not null
            ? $"{model.LoggerParameterName}.{fastField}"
            : $"{model.LoggerParameterName}.IsEnabled({levelField})";
        sb.AppendLine($"        if (!{guard}) return;");

        // Compile-time-resolution diagnostics counter. Source-gen paths
        // never touch the runtime cache; this bump keeps an operator able
        // to tell from GetNamingPolicyDiagnostics that the [HeraldLog]
        // surface is carrying load.
        sb.AppendLine($"        {model.LoggerParameterName}.RecordCompileTimeResolution();");

        // Dispatch shape varies by parameter count.
        var count = model.Parameters.Length;
        if (count == 0)
        {
            EmitZeroParamCall(sb, model, levelField, categoryField);
        }
        else if (count <= 8)
        {
            EmitCompactPath(sb, model, levelField, categoryField, resolvedNames);
        }
        else
        {
            EmitLegacyArrayPath(sb, model, levelField, categoryField, resolvedNames);
        }

        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }

    private static void EmitZeroParamCall(
        StringBuilder sb, LogMethodModel model, string levelField, string categoryField)
    {
        sb.AppendLine($"        {model.LoggerParameterName}.Log(");
        sb.AppendLine($"            {levelField},");
        sb.AppendLine($"            {categoryField},");
        sb.AppendLine($"            \"{EscapeString(model.Message)}\");");
    }

    private static void EmitCompactPath(
        StringBuilder sb, LogMethodModel model, string levelField, string categoryField,
        IReadOnlyList<string> resolvedNames)
    {
        var count = model.Parameters.Length;
        var bufferSize = PickBufferSize(count);
        var bufferType = $"global::MMP.Herald.Pipeline.Kernel.LogPropertyBuffer{bufferSize}";

        sb.AppendLine($"        var __buf = new {bufferType}();");
        for (var i = 0; i < count; i++)
        {
            var p = model.Parameters[i];
            // Use the typed-slot From<T> factory so primitive values flow
            // straight into LogPropertyCompact.ScalarBits without boxing.
            // The JIT specializes per T at first use; the type-checks
            // inside From<T> fold to compile-time constants per
            // specialization. The first argument is the policy-resolved
            // property name baked at compile time — zero runtime cost
            // for naming on the source-gen path.
            sb.AppendLine($"        __buf[{i}] = global::MMP.Herald.Pipeline.Kernel.LogPropertyCompact.From(\"{EscapeString(resolvedNames[i])}\", {p.Name});");
        }

        // For exact-fit arities (1, 2, 4, 8), the InlineArray converts to
        // a ReadOnlySpan covering every slot, which is what we want. For
        // odd arities (3, 5, 6, 7), slice to drop the uninitialized tail.
        if (count == bufferSize)
        {
            sb.AppendLine($"        {model.LoggerParameterName}.LogCompact(");
            sb.AppendLine($"            {levelField},");
            sb.AppendLine($"            {categoryField},");
            sb.AppendLine($"            \"{EscapeString(model.Message)}\",");
            sb.AppendLine("            __buf);");
        }
        else
        {
            sb.AppendLine($"        {RoSpanCompact} __span =");
            sb.AppendLine($"            (({RoSpanCompact})__buf)[..{count}];");
            sb.AppendLine($"        {model.LoggerParameterName}.LogCompact(");
            sb.AppendLine($"            {levelField},");
            sb.AppendLine($"            {categoryField},");
            sb.AppendLine($"            \"{EscapeString(model.Message)}\",");
            sb.AppendLine("            __span);");
        }
    }

    private static void EmitLegacyArrayPath(
        StringBuilder sb, LogMethodModel model, string levelField, string categoryField,
        IReadOnlyList<string> resolvedNames)
    {
        // >8 parameters. Rare in practice. Fall back to the original
        // LogProperty[] shape — still correct, still zero-alloc on
        // reject; just pays the array + records on accept. Worth a
        // follow-up if a real workload demands 9+ properties per call.
        sb.AppendLine($"        {model.LoggerParameterName}.Log(");
        sb.AppendLine($"            {levelField},");
        sb.AppendLine($"            {categoryField},");
        sb.AppendLine($"            \"{EscapeString(model.Message)}\",");
        sb.AppendLine("            properties: new MMP.Herald.Templating.LogProperty[]");
        sb.AppendLine("            {");

        for (var i = 0; i < model.Parameters.Length; i++)
        {
            var p = model.Parameters[i];
            var comma = i < model.Parameters.Length - 1 ? "," : "";
            sb.AppendLine($"                new(\"{EscapeString(resolvedNames[i])}\", {p.Name}){comma}");
        }

        sb.AppendLine("            });");
    }

    // -- Compile-time name resolution -----------------------------------------
    //
    // Source-gen pre-resolves every property name at compile time so the
    // emitted dispatch pays zero runtime cost for naming. The actual source
    // selection (token first, then CAE, then argN) and casing transform live
    // in the shared CompileTimeNameResolver helper so HeraldLogGenerator and
    // the multi-policy InterceptorGenerator stay byte-identical on the bake.
    //
    // All three built-ins are token-first; they only differ in the casing
    // transform applied to the selected source: Pascal -> ToPascalCase,
    // Snake -> ToSnakeCase, Camel -> ToCamelCase.

    private static IReadOnlyList<string> ResolveCompileTimeNames(
        LogMethodModel model, string policyId)
    {
        var caeNames = new string[model.Parameters.Length];
        for (var i = 0; i < model.Parameters.Length; i++)
        {
            caeNames[i] = model.Parameters[i].Name;
        }

        return CompileTimeNameResolver.Resolve(
            model.Message, caeNames, MapStringToBuiltinPolicy(policyId));
    }

    private static BuiltinPolicyKind MapStringToBuiltinPolicy(string policyId) => policyId switch
    {
        "camel" => BuiltinPolicyKind.Camel,
        "snake" => BuiltinPolicyKind.Snake,
        _ /* pascal */ => BuiltinPolicyKind.Pascal,
    };

    private static int PickBufferSize(int paramCount) => paramCount switch
    {
        1 => 1,
        2 => 2,
        3 or 4 => 4,
        _ => 8
    };

    // Maps a level key to Herald's precomputed accept-field name on StructuredLogger,
    // or null when no fast field exists for that level (falls back to IsEnabled).
    // Only the six core levels are precomputed; Notice/Success/Security/Metric are
    // registered but rare enough that an IsEnabled call is fine.
    // Maps a level key to Herald's precomputed accept-field name on StructuredLogger,
    // or null when no fast field exists for that level (falls back to IsEnabled).
    // NOTE: The accept-field names on StructuredLogger still use pre-Task-3 names
    // (IsTraceAcceptable, IsInfoAcceptable, etc.) — they are renamed in a later task.
    // Both new keys (information/warning/fatal/verbose) and old transitional keys
    // resolve to the matching current property name.
    private static string? LevelKeyToAcceptableField(string levelKey) => levelKey.ToLowerInvariant() switch
    {
        "verbose" or "trace" => "IsTraceAcceptable",
        "debug" => "IsDebugAcceptable",
        "information" or "info" => "IsInfoAcceptable",
        "warning" or "warn" => "IsWarnAcceptable",
        "error" => "IsErrorAcceptable",
        "fatal" or "critical" => "IsCriticalAcceptable",
        _ => null
    };

    private static string LevelKeyToField(string levelKey) => levelKey.ToLowerInvariant() switch
    {
        // New Serilog-aligned keys (post-Task-3)
        "verbose" => "MMP.Herald.Levels.KnownLogLevels.Verbose",
        "information" => "MMP.Herald.Levels.KnownLogLevels.Information",
        "warning" => "MMP.Herald.Levels.KnownLogLevels.Warning",
        "fatal" => "MMP.Herald.Levels.KnownLogLevels.Fatal",
        // Transitional old keys — resolve to the renamed member so existing
        // [HeraldLog(Level="info")] attributes still generate valid code.
        // HERALD007 warns at edit time on these; the generator still compiles.
        "trace" => "MMP.Herald.Levels.KnownLogLevels.Verbose",
        "info" => "MMP.Herald.Levels.KnownLogLevels.Information",
        "warn" => "MMP.Herald.Levels.KnownLogLevels.Warning",
        "critical" => "MMP.Herald.Levels.KnownLogLevels.Fatal",
        // Unchanged keys
        "debug" => "MMP.Herald.Levels.KnownLogLevels.Debug",
        "error" => "MMP.Herald.Levels.KnownLogLevels.Error",
        "notice" => "MMP.Herald.Levels.KnownLogLevels.Notice",
        "success" => "MMP.Herald.Levels.KnownLogLevels.Success",
        "security" => "MMP.Herald.Levels.KnownLogLevels.Security",
        "metric" => "MMP.Herald.Levels.KnownLogLevels.Metric",
        // Custom levels: construct at runtime
        _ => $"new MMP.Herald.Levels.LogLevel(\"{EscapeString(levelKey)}\", \"{EscapeString(levelKey)}\")"
    };

    // Escapes a string for safe inclusion inside a C# double-quoted
    // verbatim-but-not literal in the generated source. A user
    // supplying [HeraldLog(Message = "line\nbreak")] used to produce
    // non-compilable code; a hostile or templated value containing a
    // closing quote followed by C# would inject code at build time.
    // We mirror JsonEscaper's treatment of control characters so any
    // C0 byte is preserved as an escape sequence rather than written
    // raw into the literal.
    //
    // Cognitive note: the switch case is positional — \\ and \" first
    // so the catch-all '< 0x20' branch can stay simple.
    private static string EscapeString(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;

        // Hot test: nothing to escape -> return the input unchanged.
        // Most attribute strings are plain template text and skip the
        // allocation entirely.
        var needs = false;
        foreach (var ch in s)
        {
            if (ch == '\\' || ch == '"' || ch < 0x20 || ch == 0x7F)
            {
                needs = true;
                break;
            }
        }
        if (!needs) return s;

        var sb = new System.Text.StringBuilder(s.Length + 4);
        foreach (var ch in s)
        {
            switch (ch)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"':  sb.Append("\\\""); break;
                case '\r': sb.Append("\\r");  break;
                case '\n': sb.Append("\\n");  break;
                case '\t': sb.Append("\\t");  break;
                case '\0': sb.Append("\\0");  break;
                default:
                    if (ch < 0x20 || ch == 0x7F)
                    {
                        // Other C0 + DEL go through \uXXXX so the
                        // emitted source stays valid C# regardless of
                        // file encoding.
                        sb.Append("\\u");
                        sb.Append(((int)ch).ToString(
                            "X4", System.Globalization.CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        sb.Append(ch);
                    }
                    break;
            }
        }
        return sb.ToString();
    }

    // -- Models --
    //
    // Sealed records with ImmutableArray<ParameterModel> so the Roslyn
    // incremental pipeline's value-equality compare on each pipeline
    // node sees true semantic equality. The previous shape used
    // classes (reference equality) and List<T> (also reference
    // equality), which defeated every cache: every Extract pass
    // produced a fresh instance and every downstream stage re-ran
    // even when the model hadn't changed. Records' synthesized Equals
    // runs SequenceEqual on ImmutableArray fields, so an unchanged
    // attribute pattern short-circuits to the cached output.
    //
    // netstandard2.0 doesn't ship IsExternalInit; the polyfill below
    // is the standard one-class shim that lets records' synthesized
    // init accessors compile against the older BCL.

    private sealed record LogMethodModel(
        string? Namespace,
        string ContainingType,
        string TypeAccessibility,
        string MethodName,
        string MethodAccessibility,
        string Level,
        string Category,
        string Message,
        ImmutableArray<ParameterModel> Parameters,
        string LoggerParameterName,
        string? NamingPolicyOverride,
        Location? AttributeLocation);

    private sealed record ParameterModel(string Name, string Type);
}

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
///       if (!logger.IsEnabled(MMP.Herald.Levels.KnownLogLevels.Info)) return;
///       logger.Log(
///           MMP.Herald.Levels.KnownLogLevels.Info,
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

        // Generate source for each method
        context.RegisterSourceOutput(methods, static (ctx, model) =>
        {
            var source = GenerateSource(model);
            ctx.AddSource($"{model.ContainingType}.{model.MethodName}.g.cs", source);
        });
    }

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

        // Collect parameters (skip first = logger)
        var parameters = new List<ParameterModel>();
        for (var i = 1; i < method.Parameters.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            var p = method.Parameters[i];
            parameters.Add(new ParameterModel(
                p.Name,
                p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
        }

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
            LoggerParameterName: firstParam.Name);
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

    private static string GenerateSource(LogMethodModel model)
    {
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

        // Dispatch shape varies by parameter count.
        var count = model.Parameters.Count;
        if (count == 0)
        {
            EmitZeroParamCall(sb, model, levelField, categoryField);
        }
        else if (count <= 8)
        {
            EmitCompactPath(sb, model, levelField, categoryField);
        }
        else
        {
            EmitLegacyArrayPath(sb, model, levelField, categoryField);
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
        StringBuilder sb, LogMethodModel model, string levelField, string categoryField)
    {
        var count = model.Parameters.Count;
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
            // specialization.
            sb.AppendLine($"        __buf[{i}] = global::MMP.Herald.Pipeline.Kernel.LogPropertyCompact.From(\"{EscapeString(p.Name)}\", {p.Name});");
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
        StringBuilder sb, LogMethodModel model, string levelField, string categoryField)
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

        for (var i = 0; i < model.Parameters.Count; i++)
        {
            var p = model.Parameters[i];
            var comma = i < model.Parameters.Count - 1 ? "," : "";
            sb.AppendLine($"                new(\"{EscapeString(p.Name)}\", {p.Name}){comma}");
        }

        sb.AppendLine("            });");
    }

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
    private static string? LevelKeyToAcceptableField(string levelKey) => levelKey.ToLowerInvariant() switch
    {
        "trace" => "IsTraceAcceptable",
        "debug" => "IsDebugAcceptable",
        "info" => "IsInfoAcceptable",
        "warn" => "IsWarnAcceptable",
        "error" => "IsErrorAcceptable",
        "critical" => "IsCriticalAcceptable",
        _ => null
    };

    private static string LevelKeyToField(string levelKey) => levelKey.ToLowerInvariant() switch
    {
        "trace" => "MMP.Herald.Levels.KnownLogLevels.Trace",
        "debug" => "MMP.Herald.Levels.KnownLogLevels.Debug",
        "info" => "MMP.Herald.Levels.KnownLogLevels.Info",
        "warn" => "MMP.Herald.Levels.KnownLogLevels.Warn",
        "error" => "MMP.Herald.Levels.KnownLogLevels.Error",
        "notice" => "MMP.Herald.Levels.KnownLogLevels.Notice",
        "success" => "MMP.Herald.Levels.KnownLogLevels.Success",
        "critical" => "MMP.Herald.Levels.KnownLogLevels.Critical",
        "security" => "MMP.Herald.Levels.KnownLogLevels.Security",
        "metric" => "MMP.Herald.Levels.KnownLogLevels.Metric",
        // Custom levels: construct at runtime
        _ => $"new MMP.Herald.Levels.LogLevel(\"{EscapeString(levelKey)}\", \"{EscapeString(levelKey)}\")"
    };

    private static string EscapeString(string s) =>
        s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    // -- Models (classes, not records, because netstandard2.0 lacks IsExternalInit) --

    private sealed class LogMethodModel
    {
        public string? Namespace { get; }
        public string ContainingType { get; }
        public string TypeAccessibility { get; }
        public string MethodName { get; }
        public string MethodAccessibility { get; }
        public string Level { get; }
        public string Category { get; }
        public string Message { get; }
        public List<ParameterModel> Parameters { get; }
        public string LoggerParameterName { get; }

        public LogMethodModel(
            string? Namespace, string ContainingType, string TypeAccessibility,
            string MethodName, string MethodAccessibility,
            string Level, string Category, string Message,
            List<ParameterModel> Parameters, string LoggerParameterName)
        {
            this.Namespace = Namespace;
            this.ContainingType = ContainingType;
            this.TypeAccessibility = TypeAccessibility;
            this.MethodName = MethodName;
            this.MethodAccessibility = MethodAccessibility;
            this.Level = Level;
            this.Category = Category;
            this.Message = Message;
            this.Parameters = Parameters;
            this.LoggerParameterName = LoggerParameterName;
        }
    }

    private sealed class ParameterModel
    {
        public string Name { get; }
        public string Type { get; }

        public ParameterModel(string Name, string Type)
        {
            this.Name = Name;
            this.Type = Type;
        }
    }
}

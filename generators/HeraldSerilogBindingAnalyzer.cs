#nullable enable

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace MMP.Herald.Generators;

/// <summary>
/// Roslyn DiagnosticAnalyzer (HRLD0003) that warns when a consumer compiling at
/// <b>C# &lt; 13</b> invokes one of Herald's typed Serilog overloads at an arity
/// where overload resolution can silently miscount the arguments.
///
/// <para>
/// The typed family (<c>SerilogLoggerAdapter.Information&lt;T1..Tn&gt;(...)</c>,
/// and the Layer-2 <c>Serilog.Core.Logger</c> / <c>Serilog.Log</c> mirrors) is
/// decorated with <c>[OverloadResolutionPriority]</c> so the high-arity typed
/// overload wins over the params-object overload. That attribute is honored by
/// the <b>C# 13</b> compiler and ignored by C# 12 and earlier. On a C# 12
/// consumer a call like <c>Information(tmpl, a, b, c)</c> can bind to a
/// lower-arity overload, consuming the trailing args as
/// <c>CallerArgumentExpression</c> name overrides — the exact silent-miscount
/// the attribute exists to prevent.
/// </para>
///
/// <para>
/// The allocation behavior is identical on net8/net9/net10 — this is purely a
/// consumer-SDK requirement, not a runtime gap. net9/net10 consumers ship the
/// C# 13 SDK by default and never see this. A net8 consumer can legitimately
/// still be on C# 12, which is where the hazard lives.
/// </para>
///
/// <para>
/// Conservative by design — false positives here are worse than the gap. The
/// diagnostic fires ONLY when ALL of:
/// <list type="bullet">
///   <item>the consumer's effective language version is below C# 13;</item>
///   <item>the call binds to a Herald typed Serilog overload (by symbol
///         identity — the generic <c>Verbose/Debug/Information/Warning/Error/Fatal</c>
///         family on the Herald adapter / Serilog mirror types);</item>
///   <item>the call passes 2+ template value arguments, where a silent
///         lower-arity bind is actually possible. A single value arg cannot be
///         miscounted.</item>
/// </list>
/// On C# 13+ consumers, and on non-typed-overload calls, it never fires. The
/// fix is the consumer's explicit choice: <c>&lt;LangVersion&gt;13&lt;/LangVersion&gt;</c>
/// (available on SDK 9+ even when targeting net8) or named arguments at the call
/// site.
/// </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HeraldSerilogBindingAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "HRLD0003";

    // C# 13 == LanguageVersion value 1300. The Roslyn build this analyzer
    // compiles against (4.8.0) predates the named LanguageVersion.CSharp13
    // member, so we compare the stable numeric backing value. The enum
    // numbering (CSharp11 = 1100, CSharp12 = 1200, CSharp13 = 1300) is a
    // documented, stable scheme. When the analyzer runs inside a C# 13
    // consumer's compiler (Roslyn 4.13+), the effective value resolves to 1300.
    private const int CSharp13Value = 1300;

    // The smallest argument count at which a silent lower-arity bind can drop a
    // value into a CallerArgumentExpression slot. One value arg is unambiguous.
    private const int HazardousValueArgCount = 2;

    // Hole-named typed levels emitted by SerilogArityGenerator.
    private static readonly ImmutableHashSet<string> TypedLevelNames =
        ImmutableHashSet.Create(
            "Verbose", "Debug", "Information", "Warning", "Error", "Fatal");

    private static readonly DiagnosticDescriptor LowerArityBindHazard = new(
        id: DiagnosticId,
        title: "Typed Serilog overload may bind to a lower arity on C# < 13",
        messageFormat:
            "This call may bind to a lower-arity overload because " +
            "[OverloadResolutionPriority] requires C# 13. " +
            "Set <LangVersion>13</LangVersion> or use named arguments.",
        category: "Herald.Serilog",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description:
            "Herald's typed Serilog overloads use [OverloadResolutionPriority] so the " +
            "high-arity typed overload wins over the params-object overload. That attribute " +
            "is only honored by the C# 13 compiler. On a C# 12 consumer the call can bind to " +
            "a lower-arity overload and silently consume trailing arguments as " +
            "CallerArgumentExpression name overrides. The allocation behavior is identical " +
            "across net8/net9/net10; this is a consumer-SDK requirement. Build the consumer " +
            "with <LangVersion>13</LangVersion> (available on SDK 9+, including when targeting " +
            "net8) or pass the values as named arguments.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(LowerArityBindHazard);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        // Gate 1 — consumer language version. Cheapest correct filter: if the
        // consumer is on C# 13+, [OverloadResolutionPriority] is honored and
        // there is no hazard. Read the effective version from the call site's
        // own syntax tree so per-tree parse options are respected.
        if (!IsBelowCSharp13(context.Node.SyntaxTree)) return;

        var invocation = (InvocationExpressionSyntax)context.Node;

        // Gate 2 — cheap structural name filter before binding. The method name
        // must be one of the typed levels. Most invocations in a codebase are
        // not Herald Serilog calls; skip them without a semantic-model query.
        var methodName = GetInvokedSimpleName(invocation.Expression);
        if (methodName is null || !TypedLevelNames.Contains(methodName)) return;

        // Gate 3 — hazard requires 2+ template value arguments. The typed
        // overloads take (messageTemplate, v1..vN) or
        // (exception, messageTemplate, v1..vN), so a hazardous call has at
        // least 1 (or 2) leading non-value args plus 2+ values. Cheap arg-count
        // floor before binding: a call with too few total args can't be it.
        if (invocation.ArgumentList.Arguments.Count < (1 + HazardousValueArgCount)) return;

        // Gate 4 — bind. On a C# < 13 consumer the call may resolve to the
        // params overload OR a typed overload depending on the exact argument
        // shapes; which one binds is not a reliable key. What IS reliable: the
        // resolved method is a level-named method on a Herald Serilog surface
        // type, and that type hosts the typed family. Keying on the CONTAINING
        // TYPE (not the precise overload) makes the rule robust to whichever
        // overload betterness picked, while symbol identity still prevents
        // false positives on unrelated methods named "Information".
        var symbol = context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol;
        if (symbol is not IMethodSymbol method) return;
        if (!TypedLevelNames.Contains(method.Name)) return;
        if (!IsHeraldSerilogSurfaceType(method.ContainingType)) return;

        // Gate 5 — a typed generic overload of this level name must exist on the
        // receiver type. Only then is the [OverloadResolutionPriority] hazard in
        // play. Guards against a hypothetical params-only surface.
        if (!HasTypedOverloadOfSameName(method)) return;

        // Gate 6 — count the template VALUE arguments actually passed: total
        // supplied args minus the leading non-value ones (messageTemplate;
        // optional leading exception). 2+ values is the hazard band where a
        // silent lower-arity bind can drop a value into a CallerArgumentExpression
        // slot. A single value arg is unambiguous.
        var leadingNonValueArgs = LeadingNonValueArgCount(method);
        var valueArgCount = invocation.ArgumentList.Arguments.Count - leadingNonValueArgs;
        if (valueArgCount < HazardousValueArgCount) return;

        context.ReportDiagnostic(Diagnostic.Create(
            LowerArityBindHazard, invocation.GetLocation()));
    }

    // Effective C# language version of the call site's tree. Returns true only
    // when it is strictly below C# 13. Anything we cannot read as a CSharp tree
    // is treated as "not below" (do not fire) — conservative by design.
    private static bool IsBelowCSharp13(SyntaxTree tree)
    {
        if (tree.Options is not CSharpParseOptions options) return false;
        return (int)options.LanguageVersion < CSharp13Value;
    }

    // Pull the invoked simple name out of either `target.Information(...)` or a
    // bare `Information(...)` call, without touching the semantic model.
    private static string? GetInvokedSimpleName(ExpressionSyntax expression) => expression switch
    {
        MemberAccessExpressionSyntax member => member.Name.Identifier.Text,
        IdentifierNameSyntax identifier => identifier.Identifier.Text,
        GenericNameSyntax generic => generic.Identifier.Text,
        MemberBindingExpressionSyntax binding => binding.Name.Identifier.Text,
        _ => null,
    };

    // Leading non-value argument count for a resolved Serilog-surface level
    // method. Every overload shares the same leading structure:
    //   (string messageTemplate, ...)            → 1 leading non-value arg
    //   (Exception? exception, string template, ...) → 2 leading non-value args
    // Determined from the first parameter type: a string first param is the
    // no-exception form; anything else (the Exception? first param) is the
    // exception form. Defaults to 1 when the shape is unexpected (conservative:
    // a higher value-arg count only makes the rule MORE likely to fire, so we
    // bias the unknown shape toward the smaller leading count = 1).
    private static int LeadingNonValueArgCount(IMethodSymbol method)
    {
        if (method.Parameters.Length == 0) return 1;
        return method.Parameters[0].Type.SpecialType == SpecialType.System_String ? 1 : 2;
    }

    // True when the receiver type declares a generic (typed) overload of the
    // same level name — i.e. a typed binding was genuinely available to be
    // missed. Without this, the rule could fire on a params-only surface.
    private static bool HasTypedOverloadOfSameName(IMethodSymbol method)
    {
        foreach (var member in method.ContainingType.GetMembers(method.Name))
        {
            if (member is IMethodSymbol m && m.IsGenericMethod) return true;
        }
        return false;
    }

    // The three surfaces the typed overloads are emitted onto:
    //   MMP.Herald.Serilog.SerilogLoggerAdapter (net8/net9/net10)
    //   Serilog.Core.Logger                     (Layer-2, net9+)
    //   Serilog.Log                             (Layer-2, net9+)
    private static bool IsHeraldSerilogSurfaceType(INamedTypeSymbol? type)
    {
        if (type is null) return false;

        // Segments are passed INNERMOST-first to match the walk direction
        // (from type.ContainingNamespace outward). MMP.Herald.Serilog → the
        // innermost containing namespace is "Serilog", then "Herald", then "MMP".
        switch (type.Name)
        {
            case "SerilogLoggerAdapter":
                return IsNamespace(type.ContainingNamespace, "Serilog", "Herald", "MMP");
            case "Logger":
                return IsNamespace(type.ContainingNamespace, "Core", "Serilog");
            case "Log":
                return IsNamespace(type.ContainingNamespace, "Serilog");
            default:
                return false;
        }
    }

    // Walks a namespace symbol against an expected segment list, INNERMOST
    // segment first. `IsNamespace(ns, "Serilog", "Herald", "MMP")` matches the
    // namespace MMP.Herald.Serilog exactly (global root above "MMP"). The walk
    // starts at the passed namespace (the type's innermost container) and moves
    // outward, so the list is ordered innermost → outermost.
    private static bool IsNamespace(INamespaceSymbol? ns, params string[] segmentsInToOut)
    {
        for (var i = 0; i < segmentsInToOut.Length; i++)
        {
            if (ns is null || ns.IsGlobalNamespace) return false;
            if (ns.Name != segmentsInToOut[i]) return false;
            ns = ns.ContainingNamespace;
        }
        return ns is not null && ns.IsGlobalNamespace;
    }
}

#nullable enable

using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace MMP.Herald.Generators;

/// <summary>
/// Roslyn DiagnosticAnalyzer that enforces the default-axes-only contract
/// of <c>MMP.Herald.Pipeline.Kernel.LogPropertyCompact</c>.
///
/// <para>
/// The compact path carries name, value, and capture mode. Format and
/// visibility cannot be represented on the compact slot. A caller that
/// hands a Format- or Visibility-bearing <c>LogProperty</c> through the
/// compact path silently drops that axis at the boundary — the caller's
/// intent does not survive the trip through <c>LogPropertyCompact</c>.
/// </para>
///
/// <para>
/// HERALD014 flags the lossy construction directly: any call to
/// <c>new LogPropertyCompact(...)</c> or <c>LogPropertyCompact.From(...)</c>
/// where an argument expression contains a <c>LogProperty.Silent(...)</c>,
/// <c>LogProperty.Lazy(...)</c>, or a <c>new LogProperty(...)</c> with a
/// non-default format / visibility argument. The diagnostic
/// reports on the argument node so the IDE squiggle lands on the
/// axis-bearing construction.
/// </para>
///
/// <para>
/// Default severity is Warning; <c>&lt;HeraldStrictMode&gt;true&lt;/HeraldStrictMode&gt;</c>
/// (HRLD0002) escalates to Error along with the rest of the Herald
/// analyzer family.
/// </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HeraldCompactPathAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "HERALD014";

    private static readonly DiagnosticDescriptor AxisOnCompactPath = new(
        id: DiagnosticId,
        title: "LogProperty with Format or Visibility axis passed to compact-path API",
        messageFormat:
            "LogProperty constructed with a Format or Visibility axis ({0}) flows into a compact-path API; " +
            "the axis is dropped at the boundary. Use the full LogProperty path or remove the axis.",
        category: "Herald.Pipeline",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description:
            "MMP.Herald.Pipeline.Kernel.LogPropertyCompact carries name, value, and capture mode — " +
            "format and visibility cannot be represented on the compact slot. " +
            "A caller that constructs a Format- or Visibility-bearing LogProperty and feeds it through the " +
            "compact path loses that axis silently. Route such properties through the " +
            "full LogProperty path.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(AxisOnCompactPath);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeObjectCreation, SyntaxKind.ObjectCreationExpression);
        context.RegisterSyntaxNodeAction(AnalyzeObjectCreation, SyntaxKind.ImplicitObjectCreationExpression);
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    // new LogPropertyCompact(...) — fires when an argument contains an
    // axis-bearing LogProperty construction.
    private static void AnalyzeObjectCreation(SyntaxNodeAnalysisContext context)
    {
        var node = context.Node;
        var symbol = context.SemanticModel.GetSymbolInfo(node, context.CancellationToken).Symbol;
        if (symbol is not IMethodSymbol method || method.MethodKind != MethodKind.Constructor) return;
        if (!IsCompactPathType(method.ContainingType)) return;

        var args = node switch
        {
            ObjectCreationExpressionSyntax oc => oc.ArgumentList?.Arguments,
            ImplicitObjectCreationExpressionSyntax ioc => ioc.ArgumentList.Arguments,
            _ => null,
        };
        if (args is null) return;

        ReportNonDefaultAxisArgs(context, args.Value);
    }

    // LogPropertyCompact.From(...) — fires when an argument contains an
    // axis-bearing LogProperty construction.
    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        var symbolInfo = context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken);
        if (symbolInfo.Symbol is not IMethodSymbol method) return;

        var container = method.ContainingType;
        if (container is null) return;
        if (!IsCompactPathType(container)) return;
        // Only fire on factories on LogPropertyCompact itself (e.g. From<T>).
        if (!method.IsStatic) return;

        ReportNonDefaultAxisArgs(context, invocation.ArgumentList.Arguments);
    }

    private static void ReportNonDefaultAxisArgs(
        SyntaxNodeAnalysisContext context,
        SeparatedSyntaxList<ArgumentSyntax> args)
    {
        foreach (var arg in args)
        {
            var axisName = DetectNonDefaultAxisInExpression(
                arg.Expression, context.SemanticModel, context.CancellationToken);
            if (axisName is null) continue;

            context.ReportDiagnostic(Diagnostic.Create(
                AxisOnCompactPath,
                arg.GetLocation(),
                axisName));
        }
    }

    // The expression itself or any descendant invocation/object-creation
    // node that constructs a non-default-axis LogProperty.
    private static string? DetectNonDefaultAxisInExpression(
        ExpressionSyntax expression,
        SemanticModel model,
        System.Threading.CancellationToken cancellationToken)
    {
        foreach (var node in expression.DescendantNodesAndSelf())
        {
            string? axis = node switch
            {
                InvocationExpressionSyntax inv => InspectInvocation(inv, model, cancellationToken),
                ObjectCreationExpressionSyntax oc => InspectLogPropertyCtor(oc, oc.ArgumentList, model, cancellationToken),
                ImplicitObjectCreationExpressionSyntax ioc =>
                    InspectLogPropertyCtor(ioc, ioc.ArgumentList, model, cancellationToken),
                _ => null,
            };
            if (axis is not null) return axis;
        }
        return null;
    }

    private static string? InspectInvocation(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        System.Threading.CancellationToken cancellationToken)
    {
        var symbolInfo = model.GetSymbolInfo(invocation, cancellationToken);
        if (symbolInfo.Symbol is not IMethodSymbol method) return null;
        if (!IsHeraldLogPropertyType(method.ContainingType)) return null;
        if (!method.IsStatic) return null;
        return method.Name switch
        {
            "Silent" => "Visibility = Silent",
            "Lazy"   => "Lazy factory",
            _ => null,
        };
    }

    private static string? InspectLogPropertyCtor(
        SyntaxNode node,
        BaseArgumentListSyntax? argList,
        SemanticModel model,
        System.Threading.CancellationToken cancellationToken)
    {
        if (argList is null) return null;
        var symbolInfo = model.GetSymbolInfo(node, cancellationToken);
        if (symbolInfo.Symbol is not IMethodSymbol method) return null;
        if (method.MethodKind != MethodKind.Constructor) return null;
        if (!IsHeraldLogPropertyType(method.ContainingType)) return null;

        // Walk the parameters and arguments together. An axis arg is
        // non-default when the caller passed something other than the
        // null/default literal.
        for (var i = 0; i < method.Parameters.Length; i++)
        {
            var p = method.Parameters[i];
            if (!IsAxisParameter(p.Name)) continue;

            ArgumentSyntax? argNode = FindArgumentForParameter(argList.Arguments, p.Name, i);
            if (argNode is null) continue;
            if (IsDefaultExpression(argNode.Expression)) continue;
            return MapAxisDisplay(p.Name);
        }
        return null;
    }

    private static bool IsAxisParameter(string name) =>
        name is "format" or "Format"
             or "visibility" or "Visibility";

    private static string MapAxisDisplay(string name) => name switch
    {
        "format" or "Format" => "Format",
        "visibility" or "Visibility" => "Visibility",
        _ => name,
    };

    private static ArgumentSyntax? FindArgumentForParameter(
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        string parameterName,
        int parameterIndex)
    {
        // Named first.
        foreach (var a in arguments)
        {
            if (a.NameColon is { } nc && nc.Name.Identifier.Text == parameterName)
            {
                return a;
            }
        }
        // Positional fallback when ALL preceding args are unnamed and the
        // arg-list reaches that index.
        if (parameterIndex >= arguments.Count) return null;
        var candidate = arguments[parameterIndex];
        return candidate.NameColon is null ? candidate : null;
    }

    private static bool IsDefaultExpression(ExpressionSyntax expression)
    {
        return expression switch
        {
            LiteralExpressionSyntax lit when lit.IsKind(SyntaxKind.NullLiteralExpression) => true,
            DefaultExpressionSyntax => true,
            LiteralExpressionSyntax lit when lit.IsKind(SyntaxKind.DefaultLiteralExpression) => true,
            _ => false,
        };
    }

    private static bool IsCompactPathType(INamedTypeSymbol type)
    {
        if (type is null) return false;
        if (type.Name != "LogPropertyCompact") return false;
        return IsHeraldKernelNamespace(type.ContainingNamespace);
    }

    private static bool IsHeraldLogPropertyType(INamedTypeSymbol type)
    {
        if (type is null) return false;
        if (type.Name != "LogProperty") return false;
        return IsHeraldTemplatingNamespace(type.ContainingNamespace);
    }

    private static bool IsHeraldKernelNamespace(INamespaceSymbol? ns)
    {
        if (ns is null) return false;
        if (ns.Name != "Kernel") return false;
        var pipeline = ns.ContainingNamespace;
        if (pipeline is null || pipeline.Name != "Pipeline") return false;
        var herald = pipeline.ContainingNamespace;
        if (herald is null || herald.Name != "Herald") return false;
        var mmp = herald.ContainingNamespace;
        return mmp is not null && mmp.Name == "MMP";
    }

    private static bool IsHeraldTemplatingNamespace(INamespaceSymbol? ns)
    {
        if (ns is null) return false;
        if (ns.Name != "Templating") return false;
        var herald = ns.ContainingNamespace;
        if (herald is null || herald.Name != "Herald") return false;
        var mmp = herald.ContainingNamespace;
        return mmp is not null && mmp.Name == "MMP";
    }
}

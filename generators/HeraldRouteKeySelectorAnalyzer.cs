#nullable enable

using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace MMP.Herald.Generators;

/// <summary>
/// HERALD050 — enforces the W7 routing key-selector producer-thread-eager
/// contract. Fires on a <c>LogEventBufferKeySelector</c> lambda (the argument
/// to <c>MapRouteBuilder.Build</c>) when its body reads ambient context that
/// the routing decision cannot safely observe off the producer thread.
///
/// <para>
/// Same hazard class as the ratified lazy-resolution PII fix
/// (<see cref="HeraldLazyClosureAnalyzer"/>, HERALD008-010): a routing key
/// pulled from <c>AsyncLocal&lt;T&gt;.Value</c>, <c>HttpContext</c>, or a
/// <c>[ThreadStatic]</c> field is read in a context that may differ from the
/// producer's. On an async drain the routing decision can run on a different
/// thread, and an ambient read there returns the wrong (or another tenant's)
/// value — silently mis-routing one tenant's events into another tenant's
/// sink. The selector must read the key off the buffer's own properties.
/// </para>
///
/// <para>
/// Detection reuses the same member-access predicates as the lazy-closure
/// analyzer, anchored on the selector argument instead of
/// <c>LogProperty.Lazy</c>. Keeping it a separate analyzer (rather than a sixth
/// branch in the lazy analyzer) holds each analyzer to one anchor — the
/// invocation it inspects — which keeps both simple to reason about.
/// </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HeraldRouteKeySelectorAnalyzer : DiagnosticAnalyzer
{
    public const string AmbientReadId = "HERALD050";

    private static readonly DiagnosticDescriptor AmbientRead = new(
        id: AmbientReadId,
        title: "Routing key selector reads ambient context",
        messageFormat:
            "A routing key selector reads ambient context (AsyncLocal / HttpContext / " +
            "[ThreadStatic]). The routing decision may run off the producer thread on an " +
            "async drain, where the ambient value differs — mis-routing the event. Read the " +
            "routing key from the buffer's own properties (e.g. buffer.TryGetStringSpan(...)).",
        category: "Herald.AsyncSafety",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(AmbientRead);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        var symbolInfo = context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken);
        if (symbolInfo.Symbol is not IMethodSymbol method) return;
        if (!IsRouteBuilderBuild(method)) return;
        if (invocation.ArgumentList.Arguments.Count < 1) return;

        var selectorArg = invocation.ArgumentList.Arguments[0];
        var lambdaBody = ExtractLambdaBody(selectorArg.Expression);
        if (lambdaBody is null) return; // method group / variable — out of syntactic scope

        InspectSelectorBody(context, lambdaBody);
    }

    private static void InspectSelectorBody(SyntaxNodeAnalysisContext context, SyntaxNode body)
    {
        foreach (var node in body.DescendantNodesAndSelf())
        {
            switch (node)
            {
                case MemberAccessExpressionSyntax m:
                    InspectMemberAccess(context, m);
                    break;
                case IdentifierNameSyntax id:
                    InspectIdentifier(context, id);
                    break;
            }
        }
    }

    private static void InspectMemberAccess(
        SyntaxNodeAnalysisContext context, MemberAccessExpressionSyntax memberAccess)
    {
        var symbol = context.SemanticModel.GetSymbolInfo(memberAccess, context.CancellationToken).Symbol;
        if (symbol is null) return;

        // AsyncLocal<T>.Value
        if (symbol is IPropertySymbol prop &&
            prop.Name == "Value" &&
            prop.ContainingType is INamedTypeSymbol t &&
            t.Name == "AsyncLocal" &&
            t.ContainingNamespace?.ToDisplayString() == "System.Threading")
        {
            Report(context, memberAccess);
            return;
        }

        // HttpContext / IHttpContextAccessor (namespace-anchored).
        if (symbol.ContainingType is INamedTypeSymbol httpType)
        {
            var typeNs = httpType.ContainingNamespace?.ToDisplayString() ?? "";
            if (typeNs.StartsWith("Microsoft.AspNetCore.Http", System.StringComparison.Ordinal) &&
                (httpType.Name == "HttpContext" || httpType.Name == "IHttpContextAccessor"))
            {
                Report(context, memberAccess);
            }
        }
    }

    private static void InspectIdentifier(SyntaxNodeAnalysisContext context, IdentifierNameSyntax id)
    {
        if (id.Parent is MemberAccessExpressionSyntax m && m.Name == id) return;

        if (context.SemanticModel.GetSymbolInfo(id, context.CancellationToken).Symbol
            is not IFieldSymbol field) return;

        var hasThreadStatic = field.GetAttributes().Any(a =>
            a.AttributeClass?.Name == "ThreadStaticAttribute" &&
            (a.AttributeClass.ContainingNamespace?.ToDisplayString() ?? "") == "System");
        if (hasThreadStatic) Report(context, id);
    }

    private static void Report(SyntaxNodeAnalysisContext context, SyntaxNode node) =>
        context.ReportDiagnostic(Diagnostic.Create(AmbientRead, node.GetLocation()));

    private static bool IsRouteBuilderBuild(IMethodSymbol method)
    {
        if (method.Name != "Build") return false;
        var container = method.ContainingType;
        if (container is null || container.Name != "MapRouteBuilder") return false;
        var ns = container.ContainingNamespace?.ToDisplayString() ?? "";
        return ns == "MMP.Herald.Routing.Map";
    }

    private static SyntaxNode? ExtractLambdaBody(ExpressionSyntax expression) =>
        expression switch
        {
            LambdaExpressionSyntax lambda => lambda.Body,
            ParenthesizedExpressionSyntax parens => ExtractLambdaBody(parens.Expression),
            _ => null,
        };
}

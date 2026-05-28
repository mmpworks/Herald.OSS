#nullable enable

using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace MMP.Herald.Generators;

/// <summary>
/// Roslyn DiagnosticAnalyzer family enforcing the async-sink lazy-closure
/// contract (HERALD008-HERALD013). Fires on the lambda body of a
/// <c>LogProperty.Lazy(...)</c> call when the closure captures one of
/// the unsafe shapes that the producer-thread eager-resolution pass (L1
/// / L2 of the 0.10.2 PII fix) exists to prevent.
///
/// <para>
/// The L1/L2 layers resolve every <c>Func&lt;object?&gt;</c> on the
/// producer thread before the envelope crosses the channel, so this
/// analyzer family is defense-in-depth: it catches the structural
/// shapes that would degrade those layers if a future refactor
/// disabled them, and it educates the author about the boundary at
/// authoring time rather than at incident time.
/// </para>
///
/// <para>
/// Suppression: <c>[HeraldDrainSafe(Reason = "…")]</c> on the enclosing
/// method, property, or field suppresses HERALD008-HERALD012. See
/// <see cref="HeraldDrainSafeSuppressor"/>.
/// </para>
///
/// <list type="bullet">
///   <item><b>HERALD008</b> Error  — <c>AsyncLocal&lt;T&gt;.Value</c> access in the closure.</item>
///   <item><b>HERALD009</b> Error  — <c>HttpContext</c> / <c>IHttpContextAccessor.HttpContext</c> access in the closure.</item>
///   <item><b>HERALD010</b> Error  — <c>[ThreadStatic]</c> field access in the closure.</item>
///   <item><b>HERALD011</b> Warning — mutable non-readonly reference-type field captured by the closure.</item>
///   <item><b>HERALD012</b> Error  — <c>ILogScopeProvider</c> method invocation in the closure.</item>
///   <item><b>HERALD013</b> Info   — local-lift suggestion when the closure captures a primitive or string that could be lifted out.</item>
/// </list>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HeraldLazyClosureAnalyzer : DiagnosticAnalyzer
{
    public const string AsyncLocalCaptureId = "HERALD008";
    public const string HttpContextCaptureId = "HERALD009";
    public const string ThreadStaticCaptureId = "HERALD010";
    public const string MutableFieldCaptureId = "HERALD011";
    public const string ScopeProviderCaptureId = "HERALD012";
    public const string LocalLiftSuggestionId = "HERALD013";

    private static readonly DiagnosticDescriptor AsyncLocalCapture = new(
        id: AsyncLocalCaptureId,
        title: "LogProperty.Lazy closure captures AsyncLocal<T>",
        messageFormat:
            "LogProperty.Lazy closure reads AsyncLocal<T>.Value — the async-sink drain thread " +
            "does not inherit the producer's async-flow state. Read the value on the producer " +
            "thread and pass the captured value in, or mark the method [HeraldDrainSafe].",
        category: "Herald.AsyncSafety",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor HttpContextCapture = new(
        id: HttpContextCaptureId,
        title: "LogProperty.Lazy closure captures HttpContext",
        messageFormat:
            "LogProperty.Lazy closure reads HttpContext / IHttpContextAccessor — the async-sink " +
            "drain thread runs outside the request scope. Read the value on the producer thread " +
            "and pass it in, or mark the method [HeraldDrainSafe].",
        category: "Herald.AsyncSafety",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor ThreadStaticCapture = new(
        id: ThreadStaticCaptureId,
        title: "LogProperty.Lazy closure captures a [ThreadStatic] field",
        messageFormat:
            "LogProperty.Lazy closure reads a [ThreadStatic] field — the async-sink drain " +
            "runs on a different thread where the field has a different value. Read it on " +
            "the producer thread and pass the value in, or mark the method [HeraldDrainSafe].",
        category: "Herald.AsyncSafety",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor MutableFieldCapture = new(
        id: MutableFieldCaptureId,
        title: "LogProperty.Lazy closure captures a mutable reference-type field",
        messageFormat:
            "LogProperty.Lazy closure captures the non-readonly reference-type field '{0}'. " +
            "The field may be reassigned between envelope construction and drain-thread " +
            "resolution. Use readonly, snapshot the value at the call site, or mark the " +
            "method [HeraldDrainSafe].",
        category: "Herald.AsyncSafety",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor ScopeProviderCapture = new(
        id: ScopeProviderCaptureId,
        title: "LogProperty.Lazy closure invokes an ILogScopeProvider method",
        messageFormat:
            "LogProperty.Lazy closure invokes ILogScopeProvider — scope state lives on the " +
            "producer's async flow. Read scope values on the producer thread and pass them " +
            "in, or mark the method [HeraldDrainSafe].",
        category: "Herald.AsyncSafety",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor LocalLiftSuggestion = new(
        id: LocalLiftSuggestionId,
        title: "LogProperty.Lazy closure is trivial — consider passing the value directly",
        messageFormat:
            "LogProperty.Lazy closure returns a primitive / string with no work. The lazy " +
            "shape pays a closure allocation for nothing — pass the value to a plain " +
            "LogProperty(name, value) instead.",
        category: "Herald.Performance",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(
            AsyncLocalCapture,
            HttpContextCapture,
            ThreadStaticCapture,
            MutableFieldCapture,
            ScopeProviderCapture,
            LocalLiftSuggestion);

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
        if (!IsLogPropertyLazy(method)) return;

        // The 2nd parameter (valueFactory) is the closure we inspect.
        if (method.Parameters.Length < 2) return;

        // Find the matching argument syntax for the factory parameter.
        var factoryArg = FindArgumentForParameter(invocation.ArgumentList.Arguments, method, factoryParameterIndex: 1);
        if (factoryArg is null) return;

        // The factory must be a lambda we can analyse. Method group / variable
        // capture cases are out of scope (the suppressor catches the
        // marked-safe ones; everything else is structurally unreachable for
        // syntactic analysis).
        var lambdaBody = ExtractLambdaBody(factoryArg.Expression);
        if (lambdaBody is null) return;

        InspectLambdaBody(context, lambdaBody);
    }

    private static void InspectLambdaBody(
        SyntaxNodeAnalysisContext context,
        SyntaxNode body)
    {
        // Walk every descendant in the lambda body looking for the named
        // patterns. Some patterns key off member-access syntax; some off
        // identifier resolution.
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

        // Local-lift suggestion: pure trivial body (return literal / return
        // captured-local) — the closure pays a delegate alloc for nothing.
        InspectForLocalLift(context, body);
    }

    private static void InspectMemberAccess(
        SyntaxNodeAnalysisContext context,
        MemberAccessExpressionSyntax memberAccess)
    {
        var symbolInfo = context.SemanticModel.GetSymbolInfo(memberAccess, context.CancellationToken);
        var symbol = symbolInfo.Symbol;
        if (symbol is null) return;

        // HERALD008 — AsyncLocal<T>.Value access.
        if (symbol is IPropertySymbol prop &&
            prop.Name == "Value" &&
            prop.ContainingType is INamedTypeSymbol t &&
            t.Name == "AsyncLocal" &&
            t.ContainingNamespace?.ToDisplayString() == "System.Threading")
        {
            context.ReportDiagnostic(Diagnostic.Create(AsyncLocalCapture, memberAccess.GetLocation()));
            return;
        }

        // HERALD009 — HttpContext / IHttpContextAccessor access. Detection
        // is namespace-anchored on Microsoft.AspNetCore.Http to avoid
        // false-firing on look-alike type names in unrelated codebases.
        if (symbol.ContainingType is INamedTypeSymbol httpType)
        {
            var typeNs = httpType.ContainingNamespace?.ToDisplayString() ?? "";
            if (typeNs.StartsWith("Microsoft.AspNetCore.Http", System.StringComparison.Ordinal) &&
                (httpType.Name == "HttpContext" || httpType.Name == "IHttpContextAccessor"))
            {
                context.ReportDiagnostic(Diagnostic.Create(HttpContextCapture, memberAccess.GetLocation()));
                return;
            }
        }

        // HERALD012 — ILogScopeProvider method call.
        if (symbol is IMethodSymbol scopeMethod &&
            scopeMethod.ContainingType is INamedTypeSymbol scopeType &&
            scopeType.Name == "ILogScopeProvider" &&
            (scopeType.ContainingNamespace?.ToDisplayString() ?? "").StartsWith("MMP.Herald", System.StringComparison.Ordinal))
        {
            context.ReportDiagnostic(Diagnostic.Create(ScopeProviderCapture, memberAccess.GetLocation()));
            return;
        }
    }

    private static void InspectIdentifier(
        SyntaxNodeAnalysisContext context,
        IdentifierNameSyntax id)
    {
        // Skip identifiers that are part of a member access — the outer
        // member-access inspector handles those. We want bare identifier
        // references to fields.
        if (id.Parent is MemberAccessExpressionSyntax m && m.Name == id) return;

        var symbolInfo = context.SemanticModel.GetSymbolInfo(id, context.CancellationToken);
        if (symbolInfo.Symbol is not IFieldSymbol field) return;

        // HERALD010 — [ThreadStatic] field read.
        var hasThreadStatic = field.GetAttributes().Any(a =>
            a.AttributeClass?.Name == "ThreadStaticAttribute" &&
            (a.AttributeClass.ContainingNamespace?.ToDisplayString() ?? "") == "System");
        if (hasThreadStatic)
        {
            context.ReportDiagnostic(Diagnostic.Create(ThreadStaticCapture, id.GetLocation()));
            return;
        }

        // HERALD011 — mutable reference-type instance/static field captured.
        // Skip readonly fields (operator-stable), value-type fields (the
        // closure captures the value, not the live field), and the implicit
        // `this` field on records.
        if (field.IsReadOnly) return;
        if (field.Type.IsValueType) return;
        if (field.IsConst) return;
        if (field.IsImplicitlyDeclared) return;

        context.ReportDiagnostic(Diagnostic.Create(MutableFieldCapture, id.GetLocation(), field.Name));
    }

    // Local-lift heuristic: trivial body is either:
    //   - lambda expression body: `() => some-literal` or `() => someLocal`
    //   - block body with one statement: `{ return some-literal; }` or
    //                                    `{ return someLocal; }`
    // Where some-literal is a literal or a string-interpolation-free
    // identifier reference. Fires once per lazy invocation, on the lambda.
    // The factory parameter expects object?, so a cast wrapping the
    // trivial value is normal — we unwrap casts and parentheses.
    private static void InspectForLocalLift(
        SyntaxNodeAnalysisContext context,
        SyntaxNode body)
    {
        ExpressionSyntax? returned = body switch
        {
            ExpressionSyntax e => e,
            BlockSyntax block when block.Statements.Count == 1 &&
                                   block.Statements[0] is ReturnStatementSyntax ret
                => ret.Expression,
            _ => null,
        };
        if (returned is null) return;

        var unwrapped = UnwrapCastsAndParens(returned);
        var isTrivial = unwrapped switch
        {
            LiteralExpressionSyntax => true,
            IdentifierNameSyntax id when IsTrivialIdentifier(context, id) => true,
            _ => false,
        };
        if (!isTrivial) return;

        context.ReportDiagnostic(Diagnostic.Create(LocalLiftSuggestion, body.GetLocation()));
    }

    private static ExpressionSyntax UnwrapCastsAndParens(ExpressionSyntax expression)
    {
        var current = expression;
        while (true)
        {
            switch (current)
            {
                case ParenthesizedExpressionSyntax parens:
                    current = parens.Expression;
                    break;
                case CastExpressionSyntax cast:
                    current = cast.Expression;
                    break;
                default:
                    return current;
            }
        }
    }

    private static bool IsTrivialIdentifier(
        SyntaxNodeAnalysisContext context,
        IdentifierNameSyntax id)
    {
        var symbol = context.SemanticModel.GetSymbolInfo(id, context.CancellationToken).Symbol;
        return symbol switch
        {
            ILocalSymbol => true,
            IParameterSymbol => true,
            _ => false,
        };
    }

    private static bool IsLogPropertyLazy(IMethodSymbol method)
    {
        if (!method.IsStatic) return false;
        if (method.Name != "Lazy") return false;
        var container = method.ContainingType;
        if (container is null) return false;
        if (container.Name != "LogProperty") return false;
        var ns = container.ContainingNamespace?.ToDisplayString() ?? "";
        return ns == "MMP.Herald.Templating";
    }

    // Match invocation argument to a method parameter, preferring named
    // arguments and falling back to positional.
    private static ArgumentSyntax? FindArgumentForParameter(
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        IMethodSymbol method,
        int factoryParameterIndex)
    {
        if (factoryParameterIndex >= method.Parameters.Length) return null;
        var p = method.Parameters[factoryParameterIndex];

        foreach (var a in arguments)
        {
            if (a.NameColon is { } nc && nc.Name.Identifier.Text == p.Name)
            {
                return a;
            }
        }
        if (factoryParameterIndex < arguments.Count)
        {
            var a = arguments[factoryParameterIndex];
            return a.NameColon is null ? a : null;
        }
        return null;
    }

    private static SyntaxNode? ExtractLambdaBody(ExpressionSyntax expression)
    {
        return expression switch
        {
            LambdaExpressionSyntax lambda => lambda.Body,
            ParenthesizedExpressionSyntax parens => ExtractLambdaBody(parens.Expression),
            _ => null,
        };
    }
}

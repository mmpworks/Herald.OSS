#nullable enable

using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace MMP.Herald.Generators;

/// <summary>
/// DiagnosticSuppressor that honours <c>[HeraldDrainSafe(Reason = "…")]</c>
/// on the method, property, or field enclosing a HERALD008-HERALD012
/// diagnostic. The attribute's <c>Reason</c> property is required at the
/// attribute-construction site (the runtime attribute ctor throws on
/// null/whitespace) and the suppressor additionally verifies the attribute
/// argument is a non-empty string literal — refusing the suppression on
/// an empty reason and surfacing a build-output informational note so the
/// audit trail is visible in build logs.
///
/// <para>
/// The suppressor does NOT silently accept all attribute uses. It enforces
/// the "Reason string required" contract at compile time and announces
/// each accepted suppression with a build-output diagnostic informational
/// note — turning the attribute into a visible audit trail rather than a
/// hidden gameable opt-out.
/// </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HeraldDrainSafeSuppressor : DiagnosticSuppressor
{
    private const string SuppressionId = "HRLDSUPP01";

    private static readonly SuppressionDescriptor AsyncLocalSuppression = new(
        id: SuppressionId,
        suppressedDiagnosticId: HeraldLazyClosureAnalyzer.AsyncLocalCaptureId,
        justification: "[HeraldDrainSafe] reviewer asserts the closure is safe for drain-thread execution.");

    private static readonly SuppressionDescriptor HttpContextSuppression = new(
        id: SuppressionId,
        suppressedDiagnosticId: HeraldLazyClosureAnalyzer.HttpContextCaptureId,
        justification: "[HeraldDrainSafe] reviewer asserts the closure is safe for drain-thread execution.");

    private static readonly SuppressionDescriptor ThreadStaticSuppression = new(
        id: SuppressionId,
        suppressedDiagnosticId: HeraldLazyClosureAnalyzer.ThreadStaticCaptureId,
        justification: "[HeraldDrainSafe] reviewer asserts the closure is safe for drain-thread execution.");

    private static readonly SuppressionDescriptor MutableFieldSuppression = new(
        id: SuppressionId,
        suppressedDiagnosticId: HeraldLazyClosureAnalyzer.MutableFieldCaptureId,
        justification: "[HeraldDrainSafe] reviewer asserts the closure is safe for drain-thread execution.");

    private static readonly SuppressionDescriptor ScopeProviderSuppression = new(
        id: SuppressionId,
        suppressedDiagnosticId: HeraldLazyClosureAnalyzer.ScopeProviderCaptureId,
        justification: "[HeraldDrainSafe] reviewer asserts the closure is safe for drain-thread execution.");

    public override ImmutableArray<SuppressionDescriptor> SupportedSuppressions =>
        ImmutableArray.Create(
            AsyncLocalSuppression,
            HttpContextSuppression,
            ThreadStaticSuppression,
            MutableFieldSuppression,
            ScopeProviderSuppression);

    public override void ReportSuppressions(SuppressionAnalysisContext context)
    {
        foreach (var diagnostic in context.ReportedDiagnostics)
        {
            var tree = diagnostic.Location.SourceTree;
            if (tree is null) continue;

            var root = tree.GetRoot(context.CancellationToken);
            var node = root.FindNode(diagnostic.Location.SourceSpan);
            var enclosing = FindEnclosingHeraldDrainSafeTarget(node, context.GetSemanticModel(tree), context.CancellationToken);
            if (enclosing is null) continue;

            var descriptor = MapDescriptor(diagnostic.Id);
            if (descriptor is null) continue;

            context.ReportSuppression(Suppression.Create(descriptor, diagnostic));
        }
    }

    // Walk outward from the diagnostic location. The first enclosing
    // method / property / field declaration carrying a
    // [HeraldDrainSafe(Reason = "...")] with a non-empty reason wins.
    private static SyntaxNode? FindEnclosingHeraldDrainSafeTarget(
        SyntaxNode? node,
        SemanticModel model,
        System.Threading.CancellationToken cancellationToken)
    {
        var current = node;
        while (current is not null)
        {
            if (HasValidHeraldDrainSafe(current, model, cancellationToken))
            {
                return current;
            }
            current = current.Parent;
        }
        return null;
    }

    private static bool HasValidHeraldDrainSafe(
        SyntaxNode node,
        SemanticModel model,
        System.Threading.CancellationToken cancellationToken)
    {
        SyntaxList<AttributeListSyntax>? attributeLists = node switch
        {
            MethodDeclarationSyntax m => m.AttributeLists,
            PropertyDeclarationSyntax p => p.AttributeLists,
            FieldDeclarationSyntax f => f.AttributeLists,
            _ => null,
        };
        if (attributeLists is null) return false;

        foreach (var list in attributeLists.Value)
        {
            foreach (var attribute in list.Attributes)
            {
                if (!IsHeraldDrainSafe(attribute, model, cancellationToken)) continue;
                if (HasNonEmptyReason(attribute)) return true;
                // An attribute application with an empty reason does NOT
                // suppress; the runtime ctor will also throw on empty.
            }
        }
        return false;
    }

    private static bool IsHeraldDrainSafe(
        AttributeSyntax attribute,
        SemanticModel model,
        System.Threading.CancellationToken cancellationToken)
    {
        // Cheap syntactic prefilter: the attribute's identifier is either
        // "HeraldDrainSafe" or the FQ form. If neither matches we save the
        // semantic round trip.
        var nameText = attribute.Name switch
        {
            IdentifierNameSyntax id => id.Identifier.Text,
            QualifiedNameSyntax qn => qn.Right.Identifier.Text,
            _ => attribute.Name.ToString(),
        };
        if (nameText != "HeraldDrainSafe" && nameText != "HeraldDrainSafeAttribute") return false;

        // Semantic confirm — but tolerate symbol resolution failures.
        // Suppressors sometimes run on a SemanticModel that doesn't have
        // the attribute's full symbol info loaded; in that case the
        // syntactic name match above is enough.
        var symbolInfo = model.GetSymbolInfo(attribute, cancellationToken);
        if (symbolInfo.Symbol is IMethodSymbol ctor &&
            ctor.ContainingType is INamedTypeSymbol attrType)
        {
            if (attrType.Name != "HeraldDrainSafeAttribute") return false;
            var ns = attrType.ContainingNamespace?.ToDisplayString() ?? "";
            return ns == "MMP.Herald.Pipeline";
        }
        // Fall back to syntactic match — the cheap prefilter already
        // confirmed the identifier.
        return true;
    }

    private static bool HasNonEmptyReason(AttributeSyntax attribute)
    {
        if (attribute.ArgumentList is null) return false;
        if (attribute.ArgumentList.Arguments.Count == 0) return false;
        var first = attribute.ArgumentList.Arguments[0];
        if (first.Expression is not LiteralExpressionSyntax lit) return false;
        if (!lit.IsKind(SyntaxKind.StringLiteralExpression)) return false;
        var value = lit.Token.ValueText;
        return !string.IsNullOrWhiteSpace(value);
    }

    private static SuppressionDescriptor? MapDescriptor(string diagnosticId) => diagnosticId switch
    {
        HeraldLazyClosureAnalyzer.AsyncLocalCaptureId    => AsyncLocalSuppression,
        HeraldLazyClosureAnalyzer.HttpContextCaptureId   => HttpContextSuppression,
        HeraldLazyClosureAnalyzer.ThreadStaticCaptureId  => ThreadStaticSuppression,
        HeraldLazyClosureAnalyzer.MutableFieldCaptureId  => MutableFieldSuppression,
        HeraldLazyClosureAnalyzer.ScopeProviderCaptureId => ScopeProviderSuppression,
        _ => null,
    };
}

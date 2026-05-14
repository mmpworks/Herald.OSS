#nullable enable

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace MMP.Herald.Generators;

/// <summary>
/// Analyzer that warns on per-call <c>new LogCategory(...)</c> allocations
/// inside method bodies. The Herald idiom is to hoist log categories to
/// static readonly fields and reuse them:
///
/// <code>
/// private static readonly LogCategory AppCategory = new("App");
///
/// public void DoWork()
/// {
///     _logger.Info(AppCategory, "starting work");
/// }
/// </code>
///
/// <para>
/// Allocating a fresh LogCategory on every log call pays ~24 B per line
/// for nothing — LogCategory is immutable, so the per-call instance is
/// always equivalent to a hoisted one. Static-field hoisting matches how
/// Serilog sub-loggers and NLog Logger instances are meant to be used.
/// </para>
///
/// <para>
/// The analyzer fires only when <c>new LogCategory(...)</c> appears
/// inside the body of a method, local function, or lambda — not in a
/// field initializer, property initializer, or constructor (those are
/// the hoisted patterns we want to encourage).
/// </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HeraldLogCategoryAnalyzer : DiagnosticAnalyzer
{
    public const string PerCallAllocationId = "HERALD006";

    private static readonly DiagnosticDescriptor PerCallAllocation = new(
        id: PerCallAllocationId,
        title: "LogCategory allocated per call",
        messageFormat: "new LogCategory(\"{0}\") inside a method body allocates ~24 B per call. Hoist the category to a static readonly field and reuse it.",
        category: "Herald.Performance",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description:
            "LogCategory is immutable. A per-call allocation matches every subsequent allocation " +
            "from the same site, so hoisting to a static readonly field costs nothing and saves the " +
            "per-call bytes. See docs/benchmarks for measured impact.",
        helpLinkUri: "https://herald.dev/docs/benchmarks#project-b-log-category-hoisting");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(PerCallAllocation);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeObjectCreation, SyntaxKind.ObjectCreationExpression);
    }

    private static void AnalyzeObjectCreation(SyntaxNodeAnalysisContext context)
    {
        var creation = (ObjectCreationExpressionSyntax)context.Node;

        // Match "new LogCategory(...)" by simple name. Users who fully qualify
        // (e.g. "MMP.Herald.Events.LogCategory") are handled by the walking
        // check below via the type's QualifiedName identifier. Aliased imports
        // are rare enough that we accept the false-negative on that shape.
        if (!IsLogCategoryTypeName(creation.Type)) return;

        // Field / property initializers are the idiomatic hoisted form — we
        // want to encourage them, not flag them. Likewise, constructors are
        // usually where static caches get set up. Only flag inside method
        // bodies, local functions, or lambdas / anonymous methods.
        if (!IsInsideExecutableCode(creation)) return;

        var argument = ExtractFirstStringArgument(creation);
        context.ReportDiagnostic(Diagnostic.Create(
            PerCallAllocation,
            creation.GetLocation(),
            argument ?? "..."));
    }

    private static bool IsLogCategoryTypeName(TypeSyntax type)
    {
        // The user can write any of:
        //   LogCategory
        //   Events.LogCategory
        //   MMP.Herald.Events.LogCategory
        //   MMP.Herald.Events.LogCategory<T> (not a thing today, but be robust)
        // The rightmost name segment is what we match on.
        return GetRightmostIdentifier(type) == "LogCategory";
    }

    private static string? GetRightmostIdentifier(TypeSyntax type) => type switch
    {
        IdentifierNameSyntax id => id.Identifier.Text,
        QualifiedNameSyntax qn => qn.Right.Identifier.Text,
        GenericNameSyntax gn => gn.Identifier.Text,
        AliasQualifiedNameSyntax aq => aq.Name.Identifier.Text,
        _ => null,
    };

    // True if the node is inside a method body, local function, lambda, or
    // anonymous method — i.e. code that runs on every call. False inside a
    // field/property initializer, constructor, or top-level declaration
    // (those are the "hoist" patterns we want users to adopt).
    private static bool IsInsideExecutableCode(SyntaxNode node)
    {
        for (var current = node.Parent; current is not null; current = current.Parent)
        {
            switch (current)
            {
                case MethodDeclarationSyntax:
                case LocalFunctionStatementSyntax:
                case ParenthesizedLambdaExpressionSyntax:
                case SimpleLambdaExpressionSyntax:
                case AnonymousMethodExpressionSyntax:
                case AccessorDeclarationSyntax:  // property getters/setters
                    return true;
                case FieldDeclarationSyntax:
                case PropertyDeclarationSyntax when current.ChildNodes().FirstOrDefaultIs<EqualsValueClauseSyntax>():
                case ConstructorDeclarationSyntax:
                    return false;
            }
        }
        return false;
    }

    private static string? ExtractFirstStringArgument(ObjectCreationExpressionSyntax creation)
    {
        var args = creation.ArgumentList?.Arguments;
        if (args is null or { Count: 0 }) return null;
        var first = args.Value[0].Expression;
        return first is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression)
            ? literal.Token.ValueText
            : null;
    }
}

internal static class SyntaxExtensions
{
    public static bool FirstOrDefaultIs<T>(this System.Collections.Generic.IEnumerable<SyntaxNode> nodes) where T : SyntaxNode
    {
        foreach (var node in nodes)
        {
            return node is T;
        }
        return false;
    }
}

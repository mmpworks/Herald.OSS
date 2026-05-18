// Copyright (c) 2026 MMPWorks LLC
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.
//
// HRLD0051 — when <HeraldNamingPolicyAssertion>Default</> is set on the
// consumer's assembly, the build-time promise is that no runtime naming-policy
// override is in play. The interceptor generator bakes a single Pascal lane
// per call site on that promise and skips the multi-policy dispatcher.
//
// This analyzer enforces the promise at build time. Any call to
// WithNamingPolicy(...) or InstallNamingPolicy(...) in an asserting assembly
// triggers HRLD0051 — a warning by default, escalated to error when
// HeraldStrictMode is set.
//
// Scope: local-assembly only. Cross-assembly transitivity is V2 territory —
// a library that asserts but calls into another asserting library is fine; a
// library that doesn't assert can call WithNamingPolicy() without tripping
// this analyzer, even if its caller asserts. Documented as a limitation.

#nullable enable

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace MMP.Herald.Generators;

/// <summary>
/// Analyzer that warns when an assembly asserting
/// <c>&lt;HeraldNamingPolicyAssertion&gt;Default&lt;/HeraldNamingPolicyAssertion&gt;</c>
/// calls a runtime naming-policy override API. The assertion commits the
/// build to single-lane Pascal-only interceptors; a runtime override
/// (<c>WithNamingPolicy</c> / <c>InstallNamingPolicy</c>) would silently
/// be ignored — the baked Pascal names win.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HeraldNamingPolicyAssertionAnalyzer : DiagnosticAnalyzer
{
    public const string OverrideCalledId = "HRLD0051";

    private static readonly DiagnosticDescriptor OverrideCalledRule = new(
        id: OverrideCalledId,
        title: "Runtime naming-policy override in an asserting assembly",
        messageFormat:
            "<HeraldNamingPolicyAssertion>Default</> asserts no runtime naming-policy override, " +
            "but '{0}' is called at this site. Either remove the assertion (revert to multi-policy " +
            "emit) or remove the call (preserve the build-time assumption).",
        category: "Herald.OSS",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description:
            "When the consumer asserts a single naming policy at build time, the interceptor " +
            "generator bakes only that policy's names into each call site. Any runtime override " +
            "via WithNamingPolicy or InstallNamingPolicy is silently ignored by intercepted call " +
            "sites — the baked names win. The analyzer surfaces the contradiction so the operator " +
            "can choose which contract to keep.",
        helpLinkUri: "https://github.com/mmpworks/Herald.OSS/blob/main/docs/diagnostics/HRLD-codes.md#hrld0051");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(OverrideCalledRule);

    // Method names that trip the analyzer when called in an asserting assembly.
    // Matched by simple name; the semantic-model verification below confirms
    // the receiver type lives in MMP.Herald to filter out same-named methods
    // from unrelated libraries.
    private static readonly string[] OverrideMethodNames =
    {
        "WithNamingPolicy",
        "InstallNamingPolicy",
    };

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(OnCompilationStart);
    }

    private static void OnCompilationStart(CompilationStartAnalysisContext context)
    {
        // Only run the analysis when the consumer has actually asserted. The
        // build-property read happens once per compilation — the per-call-site
        // syntax walk is gated on the assertion being present so an unasserted
        // build pays nothing.
        var assertion = TryGetBuildProperty(context.Options, "HeraldNamingPolicyAssertion");
        if (assertion is null) return;
        if (!assertion.Equals("Default", System.StringComparison.OrdinalIgnoreCase)) return;

        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        var methodName = GetInvokedMethodName(invocation);
        if (methodName is null) return;
        if (!IsOverrideMethodName(methodName)) return;

        // Semantic check: the method must live on a Herald type. Without
        // this, an unrelated WithNamingPolicy on a different library would
        // false-positive in the asserting assembly.
        var symbol = context.SemanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
        if (symbol is null) return;
        var containingNs = symbol.ContainingType?.ContainingNamespace?.ToDisplayString();
        if (containingNs is null) return;
        if (!containingNs.StartsWith("MMP.Herald", System.StringComparison.Ordinal)) return;

        context.ReportDiagnostic(Diagnostic.Create(
            OverrideCalledRule,
            invocation.GetLocation(),
            methodName));
    }

    private static string? GetInvokedMethodName(InvocationExpressionSyntax invocation) =>
        invocation.Expression switch
        {
            MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
            IdentifierNameSyntax id => id.Identifier.ValueText,
            _ => null,
        };

    private static bool IsOverrideMethodName(string name)
    {
        foreach (var candidate in OverrideMethodNames)
        {
            if (string.Equals(name, candidate, System.StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private static string? TryGetBuildProperty(AnalyzerOptions options, string propertyName)
    {
        return options.AnalyzerConfigOptionsProvider.GlobalOptions
            .TryGetValue("build_property." + propertyName, out var v)
            && !string.IsNullOrWhiteSpace(v)
            ? v.Trim()
            : null;
    }
}

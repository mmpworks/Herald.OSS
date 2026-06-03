// Copyright (c) 2026 MMPWorks LLC
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.
//
// HRLD0060 - flags use of the external-event-injection switch at build time.
//
// AllowExternalEventInjection() is the one deliberate, named opt-in that enables
// the Herald.OSS external event injection path. Enabling that path shifts the
// event-vetting burden - and the liability - from Herald to the application:
// an injected event bypasses redaction, factory time/scope/tenant stamping,
// enrichment, and template rendering (docs/legal/DISCLAIMERS.md section 2.2; ADR
// docs/design/external-event-injection-switch.md section 4.1).
//
// This analyzer is the build-time conspicuousness nudge: it puts a red squiggle
// on the switch call so the consent is read at the call site, not discovered at
// run time. It is one of four independent warning surfaces that make the consent
// conspicuous (DISCLAIMERS section 2.4): the runtime drop-notice, THIS analyzer,
// the AllowExternalEventInjection() XML-doc, and the disclaimer document itself.
//
// Two altitudes, two jobs, never conflated (ADR section 7.2):
//   - HeraldAllowExternalInjection (MSBuild property) is the ANALYZER consent.
//     It is assembly-wide and silences HRLD0060 across the assembly as the
//     deliberate acknowledgement. It is NOT runtime consent - suppressing the
//     analyzer does not turn injection on at runtime.
//   - AllowExternalEventInjection() (the builder method) is the per-pipeline
//     RUNTIME consent. Calling it does not suppress the analyzer.
// A team can legitimately want one without the other.
//
// HeraldStrictMode escalates the warning to an error for teams that want a hard
// build gate, mirroring the rest of the HRLD analyzer family.

#nullable enable

using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace MMP.Herald.Generators;

/// <summary>
/// Roslyn <see cref="DiagnosticAnalyzer"/> (HRLD0060) that warns at the call
/// site of <c>QuickLogBuilder.AllowExternalEventInjection()</c> - the deliberate
/// opt-in that enables the Herald.OSS external event injection path.
///
/// <para>
/// Enabling the path shifts the protection burden and the liability to the
/// application: injected events bypass redaction, factory stamping, enrichment,
/// and template rendering. The diagnostic makes that consent conspicuous at
/// compile time. It is one of four documented warning surfaces (the runtime
/// drop-notice, this analyzer, the method XML-doc, and
/// <c>docs/legal/DISCLAIMERS.md</c>).
/// </para>
///
/// <para>
/// Consent / suppression. The blessed compile-time acknowledgement is the
/// assembly-wide MSBuild property
/// <c>&lt;HeraldAllowExternalInjection&gt;true&lt;/HeraldAllowExternalInjection&gt;</c>,
/// matching the established <c>HeraldStrictMode</c> / naming-policy property
/// pattern. When it is set truthy, the analyzer registers no work and stays
/// silent across the assembly. This is analyzer consent only - it does not
/// enable injection at runtime (that is the builder method job).
/// </para>
///
/// <para>
/// Strict mode. Default severity is Warning;
/// <c>&lt;HeraldStrictMode&gt;true&lt;/HeraldStrictMode&gt;</c> escalates it to
/// Error so a team that wants a hard build gate gets one.
/// </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HeraldExternalInjectionAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "HRLD0060";

    private const string SwitchMethodName = "AllowExternalEventInjection";

    private const string ConsentProperty = "HeraldAllowExternalInjection";
    private const string StrictModeProperty = "HeraldStrictMode";

    // Shared text for the two severity variants. Kept as constants so the
    // release-tracking analyzer (RS2002) resolves the descriptors below from
    // their literal id/category/severity, while the message stays DRY.
    private const string Title =
        "External event injection is enabled - protection burden shifts to the application";

    private const string Message =
        "AllowExternalEventInjection() enables the external event injection path. " +
        "Injected events bypass redaction, factory stamping, enrichment, and template rendering, " +
        "and your application - not Herald - becomes responsible for vetting their content. " +
        "To acknowledge this deliberately and silence the warning, set " +
        "<HeraldAllowExternalInjection>true</HeraldAllowExternalInjection> in the project; " +
        "see docs/legal/DISCLAIMERS.md.";

    private const string Description =
        "Calling AllowExternalEventInjection() opts the pipeline into accepting externally " +
        "constructed (hand-built) log events. Events injected through that path do not pass " +
        "through the Herald standard ingest pipeline: redaction processing, factory time/scope/" +
        "tenant stamping, enrichment, and template rendering do not run. Because redaction does " +
        "not run, the application is responsible for vetting injected content (PII, secrets, and " +
        "credentials are illustrative, not exhaustive). The analyzer surfaces the consent at the " +
        "call site so it is read together with the disclosure. Acknowledge it assembly-wide with " +
        "<HeraldAllowExternalInjection>true</HeraldAllowExternalInjection>; the runtime opt-in is " +
        "the AllowExternalEventInjection() call itself, which the property does not affect.";

    private const string HelpLink =
        "https://github.com/mmpworks/Herald.OSS/blob/main/docs/legal/DISCLAIMERS.md#2-external-event-injection--scope-and-what-it-bypasses";

    // One descriptor (HRLD0060, Warning) - matches the AnalyzerReleases manifest
    // exactly, so the release-tracking analyzer stays clean. HeraldStrictMode
    // escalation is applied at report time via the effective-severity overload of
    // Diagnostic.Create, not by a second same-id descriptor (which RS2001 forbids).
    private static readonly DiagnosticDescriptor SwitchUsed = new(
        id: DiagnosticId,
        title: Title,
        messageFormat: Message,
        category: "Herald.OSS",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: Description,
        helpLinkUri: HelpLink);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(SwitchUsed);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(OnCompilationStart);
    }

    private static void OnCompilationStart(CompilationStartAnalysisContext context)
    {
        if (IsTrue(TryGetBuildProperty(context.Options, ConsentProperty))) return;

        // HeraldStrictMode escalates the warning to an error at report time.
        var strictMode = IsTrue(TryGetBuildProperty(context.Options, StrictModeProperty));
        var severity = strictMode ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning;

        context.RegisterSyntaxNodeAction(
            ctx => AnalyzeInvocation(ctx, severity), SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context, DiagnosticSeverity severity)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        var methodName = GetInvokedSimpleName(invocation.Expression);
        if (methodName is null || !string.Equals(methodName, SwitchMethodName, StringComparison.Ordinal)) return;

        var symbol = context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol;
        if (symbol is not IMethodSymbol method) return;
        if (!string.Equals(method.Name, SwitchMethodName, StringComparison.Ordinal)) return;
        if (!IsQuickLogBuilder(method.ContainingType)) return;

        // Report at the effective severity (Warning, or Error under strict mode).
        // The effective-severity overload reuses the single HRLD0060 descriptor.
        context.ReportDiagnostic(Diagnostic.Create(
            SwitchUsed,
            invocation.GetLocation(),
            severity,
            additionalLocations: null,
            properties: null));
    }

    private static string? GetInvokedSimpleName(ExpressionSyntax expression) => expression switch
    {
        MemberAccessExpressionSyntax member => member.Name.Identifier.Text,
        IdentifierNameSyntax identifier => identifier.Identifier.Text,
        MemberBindingExpressionSyntax binding => binding.Name.Identifier.Text,
        _ => null,
    };

    private static bool IsQuickLogBuilder(INamedTypeSymbol? type)
    {
        if (type is null) return false;
        if (type.Name != "QuickLogBuilder") return false;
        return IsNamespace(type.ContainingNamespace, "Quick", "Herald", "MMP");
    }

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

    private static string? TryGetBuildProperty(AnalyzerOptions options, string propertyName)
    {
        return options.AnalyzerConfigOptionsProvider.GlobalOptions
            .TryGetValue("build_property." + propertyName, out var v)
            && !string.IsNullOrWhiteSpace(v)
            ? v.Trim()
            : null;
    }

    private static bool IsTrue(string? value) =>
        value is not null && value.Equals("true", StringComparison.OrdinalIgnoreCase);
}

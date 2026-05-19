#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using MMP.Herald.Pipeline;

namespace MMP.Herald.Generators;

/// <summary>
/// Roslyn DiagnosticAnalyzer that detects Herald pipeline strategy anti-patterns at compile time.
/// Complements the runtime StrategyValidator (in Addons/QualityChecks) with IDE squiggles.
///
/// Detected anti-patterns:
///   HERALD001 - WithPipelineStrategy called with steps in a known anti-pattern order
///   HERALD002 - Duplicate pipeline steps in a strategy definition
///   HERALD003 - FlightRecorder placed before Filtering (recorder can't buffer filtered events)
///   HERALD004 - No sinks configured (Build() called without any WithConsoleSink/WithFileSink/etc.)
///   HERALD005 - CircuitBreaker without a failure sink configured
///
/// Works by inspecting fluent builder call chains in the syntax tree.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HeraldStrategyAnalyzer : DiagnosticAnalyzer
{
    // ── Diagnostic descriptors ──────────────────────────────────────────

    private static readonly DiagnosticDescriptor RenderingBeforeAsync = new(
        id: "HERALD001",
        title: "Rendering before Async in pipeline strategy",
        messageFormat: "Pipeline strategy places rendering before async. Template rendering will occur on the caller thread instead of the worker thread, adding latency to the game loop.",
        category: "Herald.Strategy",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: "https://herald.dev/docs/chapter-06-internals#pipeline-strategy");

    private static readonly DiagnosticDescriptor DuplicateStep = new(
        id: "HERALD002",
        title: "Duplicate pipeline step",
        messageFormat: "Pipeline strategy contains duplicate step '{0}'. Each step should appear at most once.",
        category: "Herald.Strategy",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor FlightRecorderBeforeFiltering = new(
        id: "HERALD003",
        title: "FlightRecorder before Filtering",
        messageFormat: "FlightRecorder is placed before Filtering. The recorder cannot buffer below-minimum events because they haven't been classified yet. Place Filtering before FlightRecorder in your custom pipeline strategy.",
        category: "Herald.Strategy",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor NoSinksConfigured = new(
        id: "HERALD004",
        title: "No sinks configured before Build()",
        messageFormat: "QuickLogBuilder.Build() called without any sink configured. Log events will be silently discarded. Add at least one sink (WithConsoleSink, WithFileSink, WithCustomSinkProvider, etc.).",
        category: "Herald.Configuration",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor FilteringAfterBatching = new(
        id: "HERALD005",
        title: "Filtering after Batching without FlightRecorder",
        messageFormat: "Filtering is placed after Batching without a FlightRecorder. Events are batched before being filtered, wasting buffer capacity on events that will be discarded.",
        category: "Herald.Strategy",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(
            RenderingBeforeAsync,
            DuplicateStep,
            FlightRecorderBeforeFiltering,
            NoSinksConfigured,
            FilteringAfterBatching);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    // Fully-qualified name of the QuickLogBuilder type the HERALD004 check
    // binds against. Symbol-binding the .Build() target (vs. name-matching
    // ".Build" globally) prevents the analyzer from firing on unrelated
    // fluent builders in consumer code — e.g. test helpers like
    // LogEventBuilder.Build() in Modules/Server/tests/Helpers/.
    private const string QuickLogBuilderFullName = "MMP.Herald.Quick.QuickLogBuilder";

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        // Check for .Build() calls on what looks like a QuickLogBuilder chain
        if (!IsMethodCall(invocation, "Build"))
            return;

        // Walk the fluent chain backwards to find all method calls
        var chain = CollectFluentChain(invocation);
        var methodNames = chain.Select(GetMethodName).Where(n => n != null).ToList();

        // ── HERALD004: No sinks ──
        // Two filters combine to define "this is a top-level user pipeline
        // construction without a sink":
        //   1. The fluent chain starts with `Create()` (pre-0.6.0 heuristic).
        //      Pass-through methods inside Herald.OSS that call _builder.Build()
        //      from inside a wrapper method don't have Create() in their immediate
        //      chain and stay silent — the wrapper's caller is the one we want
        //      to flag, not the wrapper itself.
        //   2. The .Build() symbol resolves to MMP.Herald.Quick.QuickLogBuilder.Build
        //      (added 0.6.0). Eliminates false positives on consumer-side fluent
        //      builders that share the Create()/Build() shape — most notably
        //      LogEventBuilder in Modules/Server/tests/Helpers/.
        // Either filter alone leaks: filter (1) alone fires on consumer
        // LogEventBuilder; filter (2) alone fires on Herald.OSS internal
        // pass-through methods. Both together capture the intent.
        var hasCreate = methodNames.Any(m => string.Equals(m, "Create", StringComparison.Ordinal));
        if (hasCreate && IsQuickLogBuilderBuild(invocation, context.SemanticModel))
        {
            var hasSink = methodNames.Any(m =>
                m!.StartsWith("WithConsoleSink", StringComparison.Ordinal) ||
                m.StartsWith("WithFileSink", StringComparison.Ordinal) ||
                m.StartsWith("WithCustomSinkProvider", StringComparison.Ordinal) ||
                m.StartsWith("WithChannelSink", StringComparison.Ordinal) ||
                m.StartsWith("WithHttpJsonSink", StringComparison.Ordinal) ||
                m.StartsWith("WithOtlpJsonSink", StringComparison.Ordinal) ||
                m.StartsWith("WithOtlpProtobufSink", StringComparison.Ordinal) ||
                m.StartsWith("WithTcpJsonLineSink", StringComparison.Ordinal));

            if (!hasSink)
            {
                context.ReportDiagnostic(Diagnostic.Create(NoSinksConfigured, invocation.GetLocation()));
            }
        }

        // ── Strategy analysis: look for WithPipelineStrategy or strategy step arguments ──
        AnalyzeStrategySteps(context, chain);
    }

    /// <summary>
    /// True when <paramref name="invocation"/> resolves to a method named
    /// <c>Build</c> declared on <c>MMP.Herald.Quick.QuickLogBuilder</c>.
    /// Returns false when the symbol cannot be resolved (no Herald.OSS
    /// reference in the compilation) — the analyzer fails-silent rather
    /// than fail-loud on unrelated codebases.
    /// </summary>
    private static bool IsQuickLogBuilderBuild(InvocationExpressionSyntax invocation, SemanticModel semanticModel)
    {
        var symbolInfo = semanticModel.GetSymbolInfo(invocation);
        var methodSymbol = symbolInfo.Symbol as IMethodSymbol
            ?? symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();
        if (methodSymbol is null) return false;

        var containingType = methodSymbol.ContainingType;
        if (containingType is null) return false;

        // Compare the fully-qualified name of the containing type. Format
        // without global:: prefix and without type parameters — keeps the
        // comparison stable across nested-type renames inside QuickLogBuilder
        // (none today; defensive against future split).
        var fqn = containingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        // FullyQualifiedFormat prepends "global::"; strip it for the compare.
        const string globalPrefix = "global::";
        if (fqn.StartsWith(globalPrefix, StringComparison.Ordinal))
        {
            fqn = fqn.Substring(globalPrefix.Length);
        }
        return string.Equals(fqn, QuickLogBuilderFullName, StringComparison.Ordinal);
    }

    private static void AnalyzeStrategySteps(SyntaxNodeAnalysisContext context,
        List<InvocationExpressionSyntax> chain)
    {
        // Look for string literals that match known step names in strategy-related calls
        foreach (var call in chain)
        {
            var methodName = GetMethodName(call);
            if (!string.Equals(methodName, "WithPipelineStrategy", StringComparison.Ordinal) &&
                !string.Equals(methodName, "FromNames", StringComparison.Ordinal) &&
                !string.Equals(methodName, "Custom", StringComparison.Ordinal))
                continue;

            var steps = ExtractStringArguments(call);
            if (steps.Count < 2) continue;

            // ── HERALD002: Duplicate steps ──
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var step in steps)
            {
                if (!seen.Add(step.Value))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        DuplicateStep, step.Location, step.Value));
                }
            }

            // Step names come from MMP.Herald.Pipeline.PipelineStepNames, a
            // shared source file linked from Core so a rename in Core breaks
            // this analyzer at compile time instead of silently regressing
            // the anti-pattern checks.

            // ── HERALD001: Rendering before Async ──
            var renderingIdx = IndexOf(steps, PipelineStepNames.Rendering);
            var asyncIdx = IndexOf(steps, PipelineStepNames.Async);
            if (renderingIdx >= 0 && asyncIdx >= 0 && renderingIdx < asyncIdx)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    RenderingBeforeAsync, call.GetLocation()));
            }

            // ── HERALD003: FlightRecorder before Filtering ──
            var flightIdx = IndexOf(steps, PipelineStepNames.FlightRecorder);
            var filterIdx = IndexOf(steps, PipelineStepNames.Filtering);
            if (flightIdx >= 0 && filterIdx >= 0 && flightIdx < filterIdx)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    FlightRecorderBeforeFiltering, call.GetLocation()));
            }

            // ── HERALD005: Filtering after Batching without FlightRecorder ──
            var batchIdx = IndexOf(steps, PipelineStepNames.Batching);
            if (batchIdx >= 0 && filterIdx > batchIdx && flightIdx < 0)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    FilteringAfterBatching, call.GetLocation()));
            }
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static bool IsMethodCall(InvocationExpressionSyntax invocation, string methodName)
    {
        return invocation.Expression switch
        {
            MemberAccessExpressionSyntax member =>
                string.Equals(member.Name.Identifier.Text, methodName, StringComparison.Ordinal),
            IdentifierNameSyntax id =>
                string.Equals(id.Identifier.Text, methodName, StringComparison.Ordinal),
            _ => false
        };
    }

    private static string? GetMethodName(InvocationExpressionSyntax invocation)
    {
        return invocation.Expression switch
        {
            MemberAccessExpressionSyntax member => member.Name.Identifier.Text,
            IdentifierNameSyntax id => id.Identifier.Text,
            _ => null
        };
    }

    /// <summary>
    /// Walk the fluent method chain backwards from a terminal call (e.g., .Build())
    /// collecting all InvocationExpressionSyntax nodes.
    /// </summary>
    private static List<InvocationExpressionSyntax> CollectFluentChain(InvocationExpressionSyntax terminal)
    {
        var chain = new List<InvocationExpressionSyntax> { terminal };
        var current = terminal.Expression;

        while (current is MemberAccessExpressionSyntax member &&
               member.Expression is InvocationExpressionSyntax parent)
        {
            chain.Add(parent);
            current = parent.Expression;
        }

        return chain;
    }

    private struct StepArg
    {
        public string Value;
        public Location Location;
    }

    private static List<StepArg> ExtractStringArguments(InvocationExpressionSyntax call)
    {
        var result = new List<StepArg>();
        foreach (var arg in call.ArgumentList.Arguments)
        {
            if (arg.Expression is LiteralExpressionSyntax literal &&
                literal.IsKind(SyntaxKind.StringLiteralExpression))
            {
                result.Add(new StepArg
                {
                    Value = literal.Token.ValueText,
                    Location = literal.GetLocation()
                });
            }
            // Also check collection expressions and array initializers
            else if (arg.Expression is ImplicitArrayCreationExpressionSyntax arr)
            {
                foreach (var elem in arr.Initializer.Expressions)
                {
                    if (elem is LiteralExpressionSyntax lit &&
                        lit.IsKind(SyntaxKind.StringLiteralExpression))
                    {
                        result.Add(new StepArg
                        {
                            Value = lit.Token.ValueText,
                            Location = lit.GetLocation()
                        });
                    }
                }
            }
        }
        return result;
    }

    private static int IndexOf(List<StepArg> steps, string name)
    {
        for (var i = 0; i < steps.Count; i++)
        {
            if (string.Equals(steps[i].Value, name, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }
}
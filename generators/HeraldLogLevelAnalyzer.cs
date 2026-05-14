#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using MMP.Herald.Levels;

namespace MMP.Herald.Generators;

/// <summary>
/// Analyzer that warns when <c>[HeraldLog(Level = "...")]</c> carries a
/// string that does not match a known log-level key. A typo like
/// <c>"infro"</c> previously compiled only to fail at
/// <see cref="HeraldLogGenerator"/> emission because the generated body
/// references <c>MMP.Herald.Levels.KnownLogLevels.Infro</c> — a field that
/// does not exist. This analyzer surfaces the mistake as an IDE squiggle
/// at edit time, before the generator runs.
///
/// <para>
/// The set of accepted keys comes from <see cref="KnownLogLevelKeys"/>,
/// a shared source file linked from Core. A rename in Core breaks this
/// analyzer at compile time so the two cannot drift.
/// </para>
///
/// <para>
/// Custom user-registered levels (via
/// <c>ILogLevelRegistry.RegisterCustomLevel</c>) are runtime-only and
/// therefore invisible to the analyzer. Consumers who rely on a custom
/// level suppress HERALD007 on the specific call site with
/// <c>#pragma warning disable HERALD007</c>.
/// </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HeraldLogLevelAnalyzer : DiagnosticAnalyzer
{
    public const string UnknownLevelId = "HERALD007";

    private static readonly DiagnosticDescriptor UnknownLevel = new(
        id: UnknownLevelId,
        title: "[HeraldLog] Level string does not match a known log level",
        messageFormat: "[HeraldLog(Level = \"{0}\")] is not a known built-in level. Built-ins: {1}. Suppress HERALD007 if this is a custom-registered level.",
        category: "Herald.Levels",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "HeraldLog attribute Level strings are resolved at compile time to KnownLogLevels.<Key> fields. A typo would fail the generator's emission; this analyzer surfaces the mistake at edit time.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(UnknownLevel);

    // Short-name + full-name so the analyzer fires whether callers write
    // [HeraldLog(...)] or [HeraldLogAttribute(...)].
    private const string AttributeShortName = "HeraldLog";
    private const string AttributeFullName = "HeraldLogAttribute";

    // Populated from the shared KnownLogLevelKeys file. The list is ordered
    // the same way it appears in the source for a stable, readable message.
    private static readonly HashSet<string> _knownKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        KnownLogLevelKeys.Trace,
        KnownLogLevelKeys.Debug,
        KnownLogLevelKeys.Info,
        KnownLogLevelKeys.Warn,
        KnownLogLevelKeys.Error,
        KnownLogLevelKeys.Notice,
        KnownLogLevelKeys.Success,
        KnownLogLevelKeys.Critical,
        KnownLogLevelKeys.Security,
        KnownLogLevelKeys.Metric,
    };

    private static readonly string _knownKeysMessageList = string.Join(
        ", ",
        KnownLogLevelKeys.Trace, KnownLogLevelKeys.Debug, KnownLogLevelKeys.Info,
        KnownLogLevelKeys.Warn, KnownLogLevelKeys.Error, KnownLogLevelKeys.Notice,
        KnownLogLevelKeys.Success, KnownLogLevelKeys.Critical, KnownLogLevelKeys.Security,
        KnownLogLevelKeys.Metric);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeAttribute, SyntaxKind.Attribute);
    }

    private static void AnalyzeAttribute(SyntaxNodeAnalysisContext context)
    {
        var attribute = (AttributeSyntax)context.Node;

        // Cheap structural filter first — most attributes in a codebase are
        // not HeraldLog. Match the short name or the explicit Attribute
        // suffix before anything more expensive.
        var name = attribute.Name is QualifiedNameSyntax qualified
            ? qualified.Right.Identifier.Text
            : (attribute.Name as IdentifierNameSyntax)?.Identifier.Text;

        if (!string.Equals(name, AttributeShortName, StringComparison.Ordinal) &&
            !string.Equals(name, AttributeFullName, StringComparison.Ordinal))
        {
            return;
        }

        if (attribute.ArgumentList is null) return;

        foreach (var arg in attribute.ArgumentList.Arguments)
        {
            if (!IsLevelArgument(arg)) continue;

            if (arg.Expression is not LiteralExpressionSyntax literal ||
                !literal.IsKind(SyntaxKind.StringLiteralExpression))
            {
                // Non-literal (constant reference, interpolated string, etc.)
                // is outside this analyzer's reach — the generator will handle
                // or fail on it. Skip without a diagnostic.
                return;
            }

            var value = literal.Token.ValueText;
            if (string.IsNullOrEmpty(value)) return;

            if (!_knownKeys.Contains(value))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    UnknownLevel, literal.GetLocation(), value, _knownKeysMessageList));
            }
            return;
        }
    }

    // The [HeraldLog] attribute has Level as one of several named arguments.
    // Accept the named form (`Level = "..."`) — the generator also requires
    // a named form because Level is a property on the attribute, not a
    // positional constructor parameter.
    private static bool IsLevelArgument(AttributeArgumentSyntax arg)
    {
        return arg.NameEquals is { } ne &&
            string.Equals(ne.Name.Identifier.Text, "Level", StringComparison.Ordinal);
    }
}

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace MMP.Herald.Output.Rendering;

/// <summary>
/// Build-time validation for processor chains.
/// Detects known antipatterns before the pipeline runs.
///
/// Usage:
///   var issues = PipelineValidation.Validate(processors);
///   if (issues.Count > 0) { /* log warnings or throw */ }
///
///   // Or fail-fast:
///   PipelineValidation.ThrowIfInvalid(processors);
/// </summary>
public static class PipelineValidation
{
    /// <summary>
    /// Validate a processor chain for known antipatterns.
    /// Returns an empty list if the chain is valid.
    /// </summary>
    public static IReadOnlyList<PipelineValidationIssue> Validate(
        IReadOnlyList<ILogOutputProcessor> processors)
    {
        ArgumentNullException.ThrowIfNull(processors);

        var issues = new List<PipelineValidationIssue>();

        CheckForDuplicates(processors, issues);
        CheckRedactionOrder(processors, issues);

        return issues;
    }

    /// <summary>
    /// Throws InvalidOperationException if the chain has any issues.
    /// </summary>
    public static void ThrowIfInvalid(IReadOnlyList<ILogOutputProcessor> processors)
    {
        var issues = Validate(processors);

        if (issues.Count > 0)
        {
            var message = string.Join("; ", issues.Select(static i => i.Description));
            throw new InvalidOperationException(
                $"Processor chain validation failed: {message}");
        }
    }

    private static void CheckForDuplicates(
        IReadOnlyList<ILogOutputProcessor> processors,
        List<PipelineValidationIssue> issues)
    {
        var seen = new HashSet<Type>();

        foreach (var processor in processors)
        {
            var type = processor.GetType();
            if (!seen.Add(type))
            {
                issues.Add(new PipelineValidationIssue(
                    PipelineValidationSeverity.Warning,
                    $"Duplicate processor type '{type.Name}' in chain. This is usually unintentional."));
            }
        }
    }

    private static void CheckRedactionOrder(
        IReadOnlyList<ILogOutputProcessor> processors,
        List<PipelineValidationIssue> issues)
    {
        // Redaction should run last so it catches all rendered values,
        // including those added by earlier processors.
        var redactionIndex = -1;
        var lastNonRedactionIndex = -1;

        for (var i = 0; i < processors.Count; i++)
        {
            if (processors[i] is RedactionProcessor)
            {
                redactionIndex = i;
            }
            else
            {
                lastNonRedactionIndex = i;
            }
        }

        if (redactionIndex >= 0 && lastNonRedactionIndex > redactionIndex)
        {
            issues.Add(new PipelineValidationIssue(
                PipelineValidationSeverity.Warning,
                "RedactionProcessor should be the last processor in the chain so it " +
                "catches values added by earlier processors."));
        }
    }
}

public sealed record PipelineValidationIssue(
    PipelineValidationSeverity Severity,
    string Description);

/// <summary>
/// Severity of a pipeline validation issue.
/// An enum rather than a sealed record because severity is a closed, fixed set
/// of values with no associated data -- enum gives zero allocation, switch-friendly
/// comparison, and clearer intent than a record wrapping a string.
/// </summary>
public enum PipelineValidationSeverity
{
    /// <summary>Non-blocking antipattern that may cause unexpected behavior.</summary>
    Warning,

    /// <summary>Blocking issue that prevents the pipeline from working correctly.</summary>
    Error
}

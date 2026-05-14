#nullable enable

using System;
using System.Collections.Generic;

namespace MMP.Herald.Quick;

/// <summary>
/// Validates a BuilderInspection snapshot for configuration issues.
/// Separated from QuickLogBuilder to honor single responsibility -
/// the builder configures, the validator checks.
///
/// Usage:
///   var inspection = builder.Inspect();
///   var result = QuickLogBuilderValidator.Validate(inspection);
///   if (result.HasCritical) { /* handle */ }
///
///   // Or with file path validation:
///   var result = QuickLogBuilderValidator.Validate(inspection, filePath: "logs/game.log");
/// </summary>
public static class QuickLogBuilderValidator
{
    /// <summary>
    /// Validate a builder inspection snapshot.
    /// </summary>
    public static ValidationResult Validate(BuilderInspection inspection, string? filePath = null) {
        var issues = new List<ValidationIssue>();

        ValidateSinks(inspection, issues);
        ValidateFilePath(filePath, issues);
        ValidateChannels(inspection, issues);
        ValidateDeferredRendering(inspection, issues);

        return new ValidationResult(issues);
    }

    // Deferred rendering only pays off when every configured sink consumes
    // structured events (JSON file, OTLP, network transports, null sink).
    // A console or text-file sink still needs the rendered message, so the
    // deferred path has to inflate the event at the sink boundary — more
    // work than just rendering inline would have been. Warn the user when
    // they've enabled the optimisation alongside a sink that will defeat it.
    private static void ValidateDeferredRendering(BuilderInspection inspection, List<ValidationIssue> issues) {
        if (!inspection.DeferRendering) return;

        var renderingSinks = new List<string>();
        if (inspection.HasConsoleSink) renderingSinks.Add("console");
        // The built-in text_file kind renders text; the json_file and
        // protobuf_file kinds consume structured events.
        if (inspection.HasFileSink && string.Equals(inspection.FileKind, "text_file", StringComparison.OrdinalIgnoreCase))
        {
            renderingSinks.Add("text file");
        }

        if (renderingSinks.Count == 0) return;

        var list = string.Join(", ", renderingSinks);
        issues.Add(new ValidationIssue(
            $"Deferred rendering is enabled but {list} sink(s) still need the rendered message. "
            + "Events will be re-parsed at the sink boundary, which is slower than inline rendering. "
            + "Either disable WithDeferredRendering() or use structured-only sinks (json_file, OTLP, webhooks, null).",
            ValidationSeverity.Warning));
    }

    private static void ValidateSinks(BuilderInspection inspection, List<ValidationIssue> issues) {
        if (!inspection.HasAnySink)
        {
            issues.Add(new ValidationIssue(
                "No sinks configured. Add at least one sink (console, file, or channel).",
                ValidationSeverity.Critical));
        }
    }

    private static void ValidateFilePath(string? filePath, List<ValidationIssue> issues) {
        if (filePath is null) return;

        try
        {
            var directory = System.IO.Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !System.IO.Directory.Exists(directory))
            {
                issues.Add(new ValidationIssue(
                    $"File sink directory does not exist: {directory}",
                    ValidationSeverity.Warning));
            }
        }
        catch (System.IO.IOException ex)
        {
            issues.Add(new ValidationIssue(
                $"File sink path validation failed: {ex.Message}",
                ValidationSeverity.Warning));
        }
    }

    private static void ValidateChannels(BuilderInspection inspection, List<ValidationIssue> issues) {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in inspection.ChannelNames)
        {
            if (!seen.Add(name))
            {
                issues.Add(new ValidationIssue(
                    $"Duplicate channel name: '{name}'.",
                    ValidationSeverity.Warning));
            }
        }
    }
}

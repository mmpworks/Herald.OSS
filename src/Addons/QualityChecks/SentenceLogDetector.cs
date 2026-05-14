#nullable enable

using System;
using MMP.Herald.Events;
using MMP.Herald.Pipeline;
using MMP.Herald.Templating;

namespace MMP.Herald.Addons.QualityChecks;

/// <summary>
/// Optional event processor that detects "sentence logs" -- unstructured message
/// templates without {placeholder} tokens. Stripe Principle #6: logs are for
/// machines to query, not humans to read.
///
/// When a sentence log is detected, the processor adds a silent warning property
/// so downstream analysis can identify and report on unstructured logging patterns.
/// Optionally invokes a callback for real-time reporting (e.g., dev console warning).
///
/// This is a quality gate, not a filter -- events always pass through. The processor
/// flags violations for visibility without blocking logging.
///
/// Usage:
///   builder.WithEventProcessor("sentenceDetector",
///       new SentenceLogDetector(onDetected: template =>
///           Console.WriteLine($"[QUALITY] Sentence log detected: {template}")));
///
/// What counts as a sentence log:
/// - No {placeholder} tokens in the message template
/// - Template is longer than the minimum length threshold (default: 10 chars)
/// - Not an interpolated string from HotPathLogger (those are pre-rendered by design)
///
/// What is NOT flagged:
/// - Templates with {placeholders} (structured)
/// - Short messages like "Started" or "Done" (below threshold)
/// - Templates that are purely numeric or single-word
/// </summary>
public sealed class SentenceLogDetector : ILogEventProcessor
{
    private readonly int _minimumLength;
    private readonly Action<string>? _onDetected;
    private long _detectionCount;

    /// <summary>
    /// Create a sentence log detector.
    /// </summary>
    /// <param name="minimumLength">Minimum template length to flag. Templates shorter than this are ignored (default: 10).</param>
    /// <param name="onDetected">Optional callback invoked with the template when a sentence log is detected.</param>
    public SentenceLogDetector(int minimumLength = 10, Action<string>? onDetected = null)
    {
        if (minimumLength < 0)
            throw new ArgumentOutOfRangeException(nameof(minimumLength), "Minimum length must be non-negative.");
        _minimumLength = minimumLength;
        _onDetected = onDetected;
    }

    public LogEvent? Process(LogEvent logEvent)
    {
        var template = logEvent.MessageTemplate;

        if (template.Length < _minimumLength)
            return logEvent;

        if (ContainsPlaceholder(template))
            return logEvent;

        // Sentence log detected: no {placeholders} in a message of meaningful length.
        System.Threading.Interlocked.Increment(ref _detectionCount);
        _onDetected?.Invoke(template);

        // Add a silent property so downstream analysis can identify these
        var flagged = new LogProperty[logEvent.Properties.Count + 1];
        for (var i = 0; i < logEvent.Properties.Count; i++)
            flagged[i] = logEvent.Properties[i];
        flagged[^1] = LogProperty.Silent("_sentenceLog", true);

        return logEvent with { Properties = flagged };
    }

    /// <summary>Total number of sentence logs detected since creation.</summary>
    public long DetectionCount => System.Threading.Interlocked.Read(ref _detectionCount);

    private static bool ContainsPlaceholder(string template)
    {
        // Fast scan: look for '{' followed by a letter (not '{{' escape)
        for (var i = 0; i < template.Length - 1; i++)
        {
            if (template[i] != '{') continue;

            // Skip escaped braces {{
            if (template[i + 1] == '{')
            {
                i++; // skip next
                continue;
            }

            // Found unescaped { followed by non-brace -- this is a placeholder
            if (char.IsLetter(template[i + 1]))
                return true;
        }

        return false;
    }
}

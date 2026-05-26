#nullable enable

using System;
using System.Collections.Generic;
using MMP.Herald.Events;
using MMP.Herald.Templating;

namespace MMP.Herald.Pipeline;

/// <summary>
/// Pipeline decorator that renders deferred log events on the current thread.
/// Placed after AsyncLogger so rendering runs on the worker thread, not the caller.
///
/// Events without the deferred sentinel pass through unchanged.
/// Events with the sentinel get their message template rendered and lazy
/// properties resolved, then the sentinel is removed.
/// </summary>
public sealed class RenderingLogger : ILogger, IComponentMetadata
{
    private readonly ILogger _next;
    private readonly MessageTemplateParser _parser;

    public RenderingLogger(ILogger next, MessageTemplateParser parser) {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _parser = parser ?? throw new ArgumentNullException(nameof(parser));
    }

    public void Log(LogEvent logEvent) {
        ArgumentNullException.ThrowIfNull(logEvent);

        if (!IsDeferred(logEvent))
        {
            _next.Log(logEvent);
            return;
        }

        // Remove the deferred sentinel from context.
        // Use a filtering wrapper to avoid copying the entire dictionary.
        var cleanContext = new SentinelFilteringContext(logEvent.Context);

        LogEvent renderedEvent;
        try
        {
            var rendered = _parser.Render(logEvent.MessageTemplate, logEvent.Properties);
            renderedEvent = logEvent with
            {
                Message = rendered.Message,
                Properties = rendered.Properties,
                Context = cleanContext
            };
        }
        catch (Exception ex)
        {
            renderedEvent = logEvent with
            {
                Message = $"[Template error: {ex.Message}] {logEvent.MessageTemplate}",
                Context = cleanContext
            };
        }

        _next.Log(renderedEvent);
    }

    private static bool IsDeferred(LogEvent logEvent) {
        return logEvent.Context.TryGetValue(DeferredLogEventFactory.DeferredSentinel, out var val)
            && val is "true";
    }

    /// <summary>
    /// Lightweight read-only dictionary wrapper that hides the deferred sentinel key
    /// without copying the entire underlying dictionary. Zero allocation beyond the
    /// wrapper object itself (~24 bytes vs ~200+ bytes for a dictionary copy).
    /// </summary>
    private sealed class SentinelFilteringContext : IReadOnlyDictionary<string, object?>
    {
        private readonly IReadOnlyDictionary<string, object?> _inner;

        public SentinelFilteringContext(IReadOnlyDictionary<string, object?> inner) => _inner = inner;

        public object? this[string key] =>
            key == DeferredLogEventFactory.DeferredSentinel
                ? throw new KeyNotFoundException(key) : _inner[key];

        public IEnumerable<string> Keys
        {
            get
            {
                foreach (var k in _inner.Keys)
                    if (k != DeferredLogEventFactory.DeferredSentinel) yield return k;
            }
        }

        public IEnumerable<object?> Values
        {
            get
            {
                foreach (var kvp in _inner)
                    if (kvp.Key != DeferredLogEventFactory.DeferredSentinel) yield return kvp.Value;
            }
        }

        public int Count => _inner.Count - (_inner.ContainsKey(DeferredLogEventFactory.DeferredSentinel) ? 1 : 0);

        public bool ContainsKey(string key) =>
            key != DeferredLogEventFactory.DeferredSentinel && _inner.ContainsKey(key);

        public bool TryGetValue(string key, out object? value)
        {
            if (key == DeferredLogEventFactory.DeferredSentinel) { value = null; return false; }
            return _inner.TryGetValue(key, out value);
        }

        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator()
        {
            foreach (var kvp in _inner)
                if (kvp.Key != DeferredLogEventFactory.DeferredSentinel) yield return kvp;
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    // -- IComponentMetadata (single source of truth) --
    internal static readonly Configuration.PipelineStepRules StepRules = new(
        PreferAfter: ["async"]);

    string IComponentMetadata.ComponentName => "rendering";
    string IComponentMetadata.DisplayName => "Template Rendering";
    string IComponentMetadata.Description => "Renders deferred message templates into formatted output strings.";
    string IComponentMetadata.Help => "Resolves {placeholder} tokens on the worker thread. Only active when DeferRendering is enabled. Place after AsyncLogger.";
    VendorInfo IComponentMetadata.Vendor => VendorInfo.MMP;
    Configuration.PipelineStepRules IComponentMetadata.Rules => StepRules;
    // Rendering has no operator-tunable config knobs (the template parser + naming
    // policy are pipeline-wide, not per-step), so the schema is intentionally empty.
    // It is still registered (see ComponentSchemaKindRegistry) so the dashboard renders
    // "no configurable options" rather than a "schema not loaded" placeholder.
    internal static readonly System.Collections.Generic.IReadOnlyList<Routing.SinkConfigField> DefaultSchema = [];

    System.Collections.Generic.IReadOnlyList<Routing.SinkConfigField> IComponentMetadata.ConfigurationSchema => DefaultSchema;
}

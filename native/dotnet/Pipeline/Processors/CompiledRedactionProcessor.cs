#nullable enable

using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using MMP.Herald.Events;
using MMP.Herald.Output.Rendering;
using MMP.Herald.Templating;

namespace MMP.Herald.Pipeline.Processors;

/// <summary>
/// Event-level redaction processor that pre-compiles all rules at construction time.
/// Unlike RedactionProcessor (which operates on rendered output fragments), this
/// processor transforms source property values before any sink sees them.
///
/// <para>
/// <b>Three rule shapes.</b>
/// </para>
/// <list type="bullet">
///   <item><description>
///     <b>Exact-name rules</b> live in a case-insensitive dictionary
///     (<c>O(1)</c> lookup per property). The dominant cost on the fast path.
///   </description></item>
///   <item><description>
///     <b>Pattern rules</b> (<see cref="RedactionPatternKind.GlobPattern"/> /
///     <see cref="RedactionPatternKind.RegexPattern"/>) live in a separate list
///     walked only when the exact-name dictionary misses. Configs that use only
///     exact names pay nothing for pattern support.
///   </description></item>
///   <item><description>
///     <b>Event-action rules</b> (<see cref="RedactionEventAction.DropEvent"/> /
///     <see cref="RedactionEventAction.ReplaceMessage"/>) run after per-property
///     redaction. Their predicate evaluates against the already-redacted event,
///     so a rule that drops on a masked value sees the masked value.
///   </description></item>
/// </list>
///
/// <para>Regex patterns are compiled with <c>RegexOptions.Compiled</c> for fast
/// per-event execution. Property name matching is case-insensitive. Glob patterns
/// translate to anchored regex (<c>*</c> → <c>.*</c>, <c>?</c> → <c>.</c>) at
/// construction time.</para>
///
/// <para>Usage:</para>
/// <code>
///   builder.WithEventProcessor(new CompiledRedactionProcessor([
///       new("password", RedactionMode.Remove),
///       new("email",    RedactionMode.Mask, ValuePattern: @"[^@]+@.+"),
///       new("ssn",      RedactionMode.Hash),
///       new("*_token",  RedactionMode.Mask,
///           PatternKind: RedactionPatternKind.GlobPattern),
///       new("",         RedactionMode.Remove,
///           When: LogEventQuery.Parse("category:test_synthetic").Matches,
///           EventAction: RedactionEventAction.DropEvent)
///   ]));
/// </code>
/// </summary>
public sealed class CompiledRedactionProcessor : ILogEventProcessor
{
    // O(1) lookup for exact-name rules — the common case and the fast path.
    private readonly Dictionary<string, CompiledRule> _ruleLookup;

    // Pattern (glob/regex) rules walked only when the exact-name lookup misses.
    private readonly List<CompiledRule> _patternRules;

    // Event-action rules applied after per-property redaction. Evaluated in
    // registration order; the first matching predicate wins.
    private readonly List<CompiledRule> _eventActionRules;

    // Shared parser — parse cache is a ConcurrentDictionary, so one instance
    // handles every re-render. Used only when a property was actually redacted;
    // no allocation on the fast path.
    private readonly MessageTemplateParser _templateParser = new();

    public CompiledRedactionProcessor(IReadOnlyList<CompiledRedactionRule> rules) {
        ArgumentNullException.ThrowIfNull(rules);

        _ruleLookup = new Dictionary<string, CompiledRule>(StringComparer.OrdinalIgnoreCase);
        _patternRules = new List<CompiledRule>();
        _eventActionRules = new List<CompiledRule>();

        foreach (var rule in rules)
        {
            var compiled = Compile(rule);

            if (rule.EventAction != RedactionEventAction.None)
            {
                _eventActionRules.Add(compiled);
            }
            else if (rule.PatternKind == RedactionPatternKind.ExactName)
            {
                _ruleLookup.TryAdd(rule.PropertyNamePattern, compiled);
            }
            else
            {
                _patternRules.Add(compiled);
            }
        }
    }

    public LogEvent? Process(LogEvent logEvent) {
        // Stage 1 — per-property redaction. Exact-name rules drive the fast
        // path; pattern rules only fire on dictionary miss.
        var afterProperties = ApplyPropertyRedaction(logEvent);

        // Stage 2 — event-level actions. Predicates here see the already-
        // redacted properties, so a rule like
        //   `dropEvent when ssn:"MASKED"` chains with an earlier
        //   `mask ssn ...` rule.
        return ApplyEventActions(afterProperties);
    }

    // --- Stage 1: per-property redaction ---------------------------------

    private LogEvent ApplyPropertyRedaction(LogEvent logEvent) {
        if (_ruleLookup.Count == 0 && _patternRules.Count == 0)
        {
            return logEvent;
        }

        var properties = logEvent.Properties;
        if (properties.Count == 0)
        {
            return logEvent;
        }

        // Cognitive complexity note: the no-match fast path stays
        // allocation-free by deferring the property-list allocation until
        // the first rule actually fires. When a rule does fire, the list
        // is back-filled with the properties already walked so the order
        // of the rewritten event matches the original.
        List<LogProperty>? newProperties = null;

        for (var i = 0; i < properties.Count; i++)
        {
            var property = properties[i];
            var matchedRule = FindMatchingRule(property.Name);

            if (matchedRule is null)
            {
                newProperties?.Add(property);
                continue;
            }

            // Per-event scope check. If the rule carries a When predicate and
            // the event fails it, the rule is a no-op for this event and the
            // property passes through untouched.
            if (matchedRule.When is not null && !matchedRule.When(logEvent))
            {
                newProperties?.Add(property);
                continue;
            }

            var resolvedValue = property.ResolvedValue?.ToString() ?? "null";

            if (matchedRule.ValueRegex is not null && !matchedRule.ValueRegex.IsMatch(resolvedValue))
            {
                newProperties?.Add(property);
                continue;
            }

            // First match — allocate the rewritten list and back-fill the
            // properties already walked unchanged.
            if (newProperties is null)
            {
                newProperties = new List<LogProperty>(properties.Count);
                for (var j = 0; j < i; j++)
                {
                    newProperties.Add(properties[j]);
                }
            }

            var redactedValue = ApplyRedaction(resolvedValue, matchedRule);
            newProperties.Add(new LogProperty(
                property.Name,
                redactedValue,
                property.CaptureMode,
                property.Format,
                property.Visibility));
        }

        if (newProperties is null)
        {
            return logEvent;
        }

        // Re-render Message from the original MessageTemplate against the
        // redacted property set. Without this, a template like
        // "Login for {email}" whose `email` property was redacted would
        // still show the original email in Message, because the event
        // factory rendered Message before this processor ran. Sinks
        // serialize Message verbatim, so the leak reaches disk, network,
        // and downstream indexers.
        //
        // Parse is cached (MessageTemplateParser._parseCache is a
        // ConcurrentDictionary) so the real cost here is one render against
        // the redacted property list. No change to MessageTemplate.
        string newMessage;
        try
        {
            var rendered = _templateParser.Render(logEvent.MessageTemplate, newProperties);
            newMessage = rendered.Message;
        }
        catch
        {
            // Defensive: a malformed template should never crash the
            // redaction pipeline. Fall back to the original Message string;
            // redaction of properties still applied, which is the important
            // half.
            newMessage = logEvent.Message;
        }

        return logEvent with
        {
            Properties = newProperties,
            Message = newMessage,
        };
    }

    // Exact-name dictionary first (O(1)); pattern-rule walk only on miss.
    private CompiledRule? FindMatchingRule(string propertyName) {
        if (_ruleLookup.TryGetValue(propertyName, out var rule))
        {
            return rule;
        }

        // Cognitive complexity note: small linear walk by design — pattern
        // rules are rare and rarely outnumber a handful, so a Dictionary
        // doesn't earn its keep here. NameRegex on each pattern rule is
        // pre-compiled at construction.
        foreach (var pr in _patternRules)
        {
            if (pr.NameRegex is not null && pr.NameRegex.IsMatch(propertyName))
            {
                return pr;
            }
        }
        return null;
    }

    private static string ApplyRedaction(string value, CompiledRule rule) =>
        Output.Rendering.RedactionHelper.Apply(value, rule.Mode, rule.MaskChar, rule.VisibleChars);

    // --- Stage 2: event-level actions ------------------------------------

    private LogEvent? ApplyEventActions(LogEvent logEvent) {
        if (_eventActionRules.Count == 0)
        {
            return logEvent;
        }

        foreach (var rule in _eventActionRules)
        {
            // Event-action rules without a When are a configuration error.
            // The parser rejects them; the in-code constructor on
            // CompiledRedactionRule rejects them too. Defending here keeps
            // the contract honest if a third path ever adds one.
            if (rule.When is null) continue;
            if (!rule.When(logEvent)) continue;

            switch (rule.EventAction)
            {
                case RedactionEventAction.DropEvent:
                    return null;

                case RedactionEventAction.ReplaceMessage:
                    return logEvent with { Message = rule.ReplaceMessageText ?? logEvent.Message };
            }
        }

        return logEvent;
    }

    // --- Compilation helpers ---------------------------------------------

    private static CompiledRule Compile(CompiledRedactionRule rule) {
        var valueRegex = CompileValueRegex(rule.ValuePattern);
        var nameRegex = CompileNameRegex(rule.PropertyNamePattern, rule.PatternKind);

        return new CompiledRule(
            rule.PropertyNamePattern,
            rule.Mode,
            valueRegex,
            nameRegex,
            rule.MaskChar,
            rule.VisibleChars,
            rule.When,
            rule.EventAction,
            rule.ReplaceMessageText);
    }

    private static Regex? CompileValueRegex(string? valuePattern) {
        if (string.IsNullOrWhiteSpace(valuePattern)) return null;
        return new Regex(
            valuePattern,
            RegexOptions.Compiled | RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));
    }

    private static Regex? CompileNameRegex(string pattern, RedactionPatternKind kind) => kind switch
    {
        RedactionPatternKind.ExactName => null,
        RedactionPatternKind.GlobPattern => new Regex(
            GlobToRegex(pattern),
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1)),
        RedactionPatternKind.RegexPattern => new Regex(
            pattern,
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1)),
        _ => null,
    };

    // Translate a glob (* any run, ? one char) to anchored regex. Other
    // regex metacharacters are escaped so a property name like "user.name"
    // doesn't accidentally use the dot as "any char" — globs operate on
    // literal property names, not regex syntax.
    private static string GlobToRegex(string glob) {
        var sb = new StringBuilder(glob.Length + 4);
        sb.Append('^');
        foreach (var c in glob)
        {
            switch (c)
            {
                case '*': sb.Append(".*"); break;
                case '?': sb.Append('.'); break;
                case '.':
                case '+':
                case '(':
                case ')':
                case '[':
                case ']':
                case '{':
                case '}':
                case '|':
                case '\\':
                case '^':
                case '$':
                    sb.Append('\\').Append(c);
                    break;
                default:
                    sb.Append(c);
                    break;
            }
        }
        sb.Append('$');
        return sb.ToString();
    }

    private sealed record CompiledRule(
        string PropertyNamePattern,
        RedactionMode Mode,
        Regex? ValueRegex,
        Regex? NameRegex,
        char MaskChar,
        int VisibleChars,
        Predicate<LogEvent>? When,
        RedactionEventAction EventAction,
        string? ReplaceMessageText);
}

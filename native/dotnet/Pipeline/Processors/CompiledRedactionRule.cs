#nullable enable

using System;
using MMP.Herald.Events;
using MMP.Herald.Output.Rendering;

namespace MMP.Herald.Pipeline.Processors;

/// <summary>
/// How <see cref="CompiledRedactionRule.PropertyNamePattern"/> is interpreted.
/// Default <see cref="ExactName"/> preserves the legacy literal-string behaviour
/// (case-insensitive O(1) dictionary match). Pattern kinds are evaluated only
/// when the exact-name lookup misses, so configs that use only exact names
/// keep their fast path.
/// </summary>
public enum RedactionPatternKind
{
    /// <summary>Literal property name. Matched case-insensitively.</summary>
    ExactName = 0,

    /// <summary>Glob expression — <c>*</c> matches any run, <c>?</c> matches one char.</summary>
    GlobPattern = 1,

    /// <summary>Regular expression. Compiled with <c>RegexOptions.Compiled</c>.</summary>
    RegexPattern = 2,
}

/// <summary>
/// Whole-event action a redaction rule may take after per-property redaction
/// runs. Default <see cref="None"/> means "this is a per-property rule" and
/// the per-property <see cref="CompiledRedactionRule.Mode"/> applies.
/// <para>
/// Event actions only fire when the rule's <see cref="CompiledRedactionRule.When"/>
/// predicate matches. The grammar parser rejects event-action rules without a
/// <c>when</c> clause — "always drop every event" is almost never what an
/// operator means; better to fail at parse time than vanish events at runtime.
/// </para>
/// </summary>
public enum RedactionEventAction
{
    /// <summary>No event-level action; the rule operates per property.</summary>
    None = 0,

    /// <summary>Drop the event entirely. Pipeline records a <c>redaction</c> drop.</summary>
    DropEvent = 1,

    /// <summary>Replace <see cref="LogEvent.Message"/> with the rule's literal string.</summary>
    ReplaceMessage = 2,
}

/// <summary>
/// A redaction rule that is compiled at processor construction time for fast per-event execution.
/// <see cref="PropertyNamePattern"/> matches by <see cref="PatternKind"/> — exact name
/// (default, case-insensitive), glob, or regex. <see cref="ValuePattern"/>, when set,
/// is a regex that must match the property value for redaction to apply.
/// <see cref="When"/>, when set, is a per-event predicate that must return true for the
/// rule to apply. The canonical way to supply one is the method group on a pre-parsed
/// <c>LogEventQuery</c> — e.g. <c>When: LogEventQuery.Parse("category:auth").Matches</c> —
/// which parses the DSL once at rule-construction time and evaluates the compiled AST
/// on every event. Any <see cref="Predicate{T}"/> of <see cref="LogEvent"/> works, so
/// callers who prefer a handwritten check are free to pass one.
/// <para>
/// <b>Event-level actions.</b> When <see cref="EventAction"/> is
/// <see cref="RedactionEventAction.DropEvent"/> or
/// <see cref="RedactionEventAction.ReplaceMessage"/>, the rule fires after per-property
/// redaction runs and applies once to the whole event. The grammar parser enforces
/// that event-action rules carry a <see cref="When"/> predicate; the processor honours
/// the same invariant by ignoring event-action rules whose <c>When</c> is null.
/// </para>
/// </summary>
public sealed record CompiledRedactionRule(
    string PropertyNamePattern,
    RedactionMode Mode,
    string? ValuePattern = null,
    char MaskChar = '*',
    int VisibleChars = 0,
    Predicate<LogEvent>? When = null,
    RedactionPatternKind PatternKind = RedactionPatternKind.ExactName,
    RedactionEventAction EventAction = RedactionEventAction.None,
    string? ReplaceMessageText = null);

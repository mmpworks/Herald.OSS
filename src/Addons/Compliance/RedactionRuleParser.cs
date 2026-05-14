#nullable enable

using System;
using System.Linq;
using MMP.Herald.Addons.Query;
using MMP.Herald.Events;
using MMP.Herald.Output.Rendering;
using MMP.Herald.Pipeline.Processors;
using Superpower;
using Superpower.Parsers;

namespace MMP.Herald.Addons.Compliance;

/// <summary>
/// Parses a redaction-rule DSL string into a <see cref="CompiledRedactionRule"/>.
/// Enterprise-only sugar over the in-code rule constructor that Community and
/// Pro callers use directly.
///
/// <para>Grammar (keywords case-insensitive, whitespace insignificant):</para>
/// <code>
/// rule    := propertyRule | eventRule
///
/// propertyRule := propertyAction field format* scope?
/// propertyAction := 'mask' | 'hash' | 'remove'
/// field   := identifier ('.' identifier)*       // exact name (case-insensitive)
///         |  'glob'  quotedString               // glob pattern: * any run, ? one char
///         |  'regex' quotedString               // .NET regex
/// format  := 'keepSuffix'   integer
///         |  'maskChar'     quotedString
///         |  'valuePattern' quotedString
///
/// eventRule := eventAction scope                // scope is REQUIRED here
/// eventAction := 'dropEvent'
///             |  'replaceMessage' quotedString
///
/// scope   := 'when' queryExpression
/// </code>
///
/// <para>Examples:</para>
/// <code>
/// "remove password"
/// "hash ssn when category:compliance"
/// "mask credit_card keepSuffix 4 when category:payment"
/// "mask glob \"*_token\""                              // any property whose name ends in _token
/// "remove regex \"^temp_\\\\w+$\""                     // any temp_foo / temp_bar / etc.
/// "hash glob \"*.password\" when level:audit"          // credentials.password, db.password
/// "dropEvent when category:test_synthetic"             // never emit synthetic traffic
/// "replaceMessage \"REDACTED\" when level:critical AND category:payment"
/// </code>
///
/// The rule head is tokenized and parsed with the same Superpower infrastructure
/// that powers the Query DSL; the scope clause is handed verbatim to
/// <see cref="LogEventQuery.Parse(string)"/>. Two small grammars, one shared
/// parser library.
///
/// <para>
/// <b>Why event verbs require <c>when</c>.</b> A rule like
/// <c>dropEvent</c> with no predicate matches every event — almost always a
/// configuration mistake. Better to fail at parse time than vanish events at
/// runtime. Operators who really want unconditional drop can write
/// <c>dropEvent when level:trace</c> (or whatever predicate the situation
/// calls for) explicitly.
/// </para>
/// </summary>
public static class RedactionRuleParser
{
    // --- Per-property action keywords -----------------------------------

    private static readonly TokenListParser<QueryToken, RedactionMode> PropertyAction =
        Token.EqualToValueIgnoreCase(QueryToken.Identifier, "mask").Select(_ => RedactionMode.Mask)
            .Or(Token.EqualToValueIgnoreCase(QueryToken.Identifier, "hash").Select(_ => RedactionMode.Hash))
            .Or(Token.EqualToValueIgnoreCase(QueryToken.Identifier, "remove").Select(_ => RedactionMode.Remove));

    // --- Field selectors ------------------------------------------------
    //
    // Three forms: literal dotted-identifier path (exact match, the original),
    // glob "*pattern", and regex "pattern". Pattern forms wrap the quoted
    // string contents and tag the kind so the processor knows which matcher
    // to compile and apply.

    private static readonly TokenListParser<QueryToken, FieldSelector> ExactField =
        from head in Token.EqualTo(QueryToken.Identifier)
        from tail in Token.EqualTo(QueryToken.Dot)
            .IgnoreThen(Token.EqualTo(QueryToken.Identifier)).Many()
        select new FieldSelector(
            RedactionPatternKind.ExactName,
            string.Join(".",
                new[] { head.ToStringValue() }.Concat(tail.Select(t => t.ToStringValue()))));

    private static readonly TokenListParser<QueryToken, FieldSelector> GlobField =
        (from _ in Token.EqualToValueIgnoreCase(QueryToken.Identifier, "glob")
         from s in Token.EqualTo(QueryToken.String)
         select new FieldSelector(RedactionPatternKind.GlobPattern, Unquote(s.ToStringValue()))).Try();

    private static readonly TokenListParser<QueryToken, FieldSelector> RegexField =
        (from _ in Token.EqualToValueIgnoreCase(QueryToken.Identifier, "regex")
         from s in Token.EqualTo(QueryToken.String)
         select new FieldSelector(RedactionPatternKind.RegexPattern, Unquote(s.ToStringValue()))).Try();

    private static readonly TokenListParser<QueryToken, FieldSelector> Field =
        GlobField.Or(RegexField).Or(ExactField);

    // --- Format options --------------------------------------------------
    //
    // Each option parser produces an Action<Options> that applies its effect
    // to an accumulator. That lets options appear in any order and makes it
    // trivial to add a new option later without touching the composition layer.

    private sealed class Options
    {
        public int VisibleChars { get; set; }
        public char MaskChar { get; set; } = '*';
        public string? ValuePattern { get; set; }
    }

    private static readonly TokenListParser<QueryToken, Action<Options>> KeepSuffixOpt =
        (from _ in Token.EqualToValueIgnoreCase(QueryToken.Identifier, "keepSuffix")
         from n in Token.EqualTo(QueryToken.Number)
         select (Action<Options>)(opts => opts.VisibleChars = int.Parse(n.ToStringValue()))).Try();

    private static readonly TokenListParser<QueryToken, Action<Options>> MaskCharOpt =
        (from _ in Token.EqualToValueIgnoreCase(QueryToken.Identifier, "maskChar")
         from s in Token.EqualTo(QueryToken.String)
         select (Action<Options>)(opts => opts.MaskChar = FirstCharOf(s.ToStringValue()))).Try();

    private static readonly TokenListParser<QueryToken, Action<Options>> ValuePatternOpt =
        (from _ in Token.EqualToValueIgnoreCase(QueryToken.Identifier, "valuePattern")
         from s in Token.EqualTo(QueryToken.String)
         select (Action<Options>)(opts => opts.ValuePattern = Unquote(s.ToStringValue()))).Try();

    private static readonly TokenListParser<QueryToken, Action<Options>> AnyOption =
        KeepSuffixOpt.Or(MaskCharOpt).Or(ValuePatternOpt);

    // --- Rule-head grammars ---------------------------------------------
    //
    // Two alternatives. Event-action heads are tried first because their
    // keywords (dropEvent, replaceMessage) would otherwise be eaten by the
    // ExactField parser as identifiers, leaving no path back. Property-rule
    // heads are the more common shape — running them second is fine because
    // event verbs are uncommon and the .Try() backtracks cheaply on a
    // single-token mismatch.

    private static readonly TokenListParser<QueryToken, ParsedHead> PropertyRuleHead =
        from action in PropertyAction
        from field in Field
        from options in AnyOption.Many()
        select ComposeProperty(action, field, options);

    private static readonly TokenListParser<QueryToken, ParsedHead> DropEventHead =
        Token.EqualToValueIgnoreCase(QueryToken.Identifier, "dropEvent")
            .Select(_ => ParsedHead.ForDrop());

    private static readonly TokenListParser<QueryToken, ParsedHead> ReplaceMessageHead =
        from _ in Token.EqualToValueIgnoreCase(QueryToken.Identifier, "replaceMessage")
        from s in Token.EqualTo(QueryToken.String)
        select ParsedHead.ForReplaceMessage(Unquote(s.ToStringValue()));

    private static readonly TokenListParser<QueryToken, ParsedHead> EventRuleHead =
        DropEventHead.Try().Or(ReplaceMessageHead);

    private static readonly TokenListParser<QueryToken, ParsedHead> HeadGrammar =
        EventRuleHead.Try().Or(PropertyRuleHead).AtEnd();

    // --- Internal carriers ----------------------------------------------

    private sealed record FieldSelector(RedactionPatternKind Kind, string Pattern);

    private sealed record ParsedHead(
        RedactionMode Mode,
        string Field,
        RedactionPatternKind FieldKind,
        int VisibleChars,
        char MaskChar,
        string? ValuePattern,
        RedactionEventAction EventAction,
        string? ReplaceMessageText)
    {
        // Event-action heads carry no per-property data. Mode/Field/FieldKind
        // are placeholders here — the processor checks EventAction first and
        // skips per-property matching when an event verb is present.
        public static ParsedHead ForDrop() =>
            new(RedactionMode.Remove, Field: "", RedactionPatternKind.ExactName,
                VisibleChars: 0, MaskChar: '*', ValuePattern: null,
                RedactionEventAction.DropEvent, ReplaceMessageText: null);

        public static ParsedHead ForReplaceMessage(string text) =>
            new(RedactionMode.Remove, Field: "", RedactionPatternKind.ExactName,
                VisibleChars: 0, MaskChar: '*', ValuePattern: null,
                RedactionEventAction.ReplaceMessage, ReplaceMessageText: text);
    }

    private static ParsedHead ComposeProperty(
        RedactionMode mode,
        FieldSelector field,
        Action<Options>[] options) {
        var opts = new Options();
        foreach (var apply in options) apply(opts);
        return new ParsedHead(
            mode,
            field.Pattern,
            field.Kind,
            opts.VisibleChars,
            opts.MaskChar,
            opts.ValuePattern,
            RedactionEventAction.None,
            ReplaceMessageText: null);
    }

    // --- Public entry ----------------------------------------------------

    /// <summary>
    /// Parse a rule-DSL string into a compiled rule. Throws
    /// <see cref="RedactionRuleParseException"/> on any failure in either the
    /// rule head or the scope predicate.
    /// </summary>
    public static CompiledRedactionRule Parse(string rule) {
        HeraldEditionGate.Require(HeraldEdition.Enterprise, "Redaction Rule DSL");
        ArgumentException.ThrowIfNullOrWhiteSpace(rule);

        var (head, predicateText) = SplitAtWhen(rule);

        var tokens = QueryTokenizer.Instance.TryTokenize(head);
        if (!tokens.HasValue)
            throw new RedactionRuleParseException(
                $"Could not tokenize rule '{rule}': {tokens.ErrorMessage ?? "tokenizer failure"}");

        var parsed = HeadGrammar.TryParse(tokens.Value);
        if (!parsed.HasValue)
            throw new RedactionRuleParseException(
                $"Invalid rule head in '{rule}': {parsed.ErrorMessage ?? "parser failure"}");

        Predicate<LogEvent>? when = null;
        if (predicateText is not null) {
            if (!LogEventQuery.TryParse(predicateText, out var query, out var predicateError))
                throw new RedactionRuleParseException(
                    $"Invalid predicate in rule '{rule}': {predicateError}");
            when = query!.Matches;
        }

        var ph = parsed.Value;

        // Event-action rules without a predicate match every event — almost
        // certainly a configuration mistake. Reject at parse time so the
        // failure is visible at pipeline build, not at runtime when events
        // start vanishing.
        if (ph.EventAction != RedactionEventAction.None && when is null)
        {
            throw new RedactionRuleParseException(
                $"Rule '{rule}' uses an event-level action ({DescribeEventAction(ph.EventAction)}) " +
                "but has no 'when' clause. Event-level actions require a predicate so they don't " +
                "match every event.");
        }

        return new CompiledRedactionRule(
            PropertyNamePattern: ph.Field,
            Mode: ph.Mode,
            ValuePattern: ph.ValuePattern,
            MaskChar: ph.MaskChar,
            VisibleChars: ph.VisibleChars,
            When: when,
            PatternKind: ph.FieldKind,
            EventAction: ph.EventAction,
            ReplaceMessageText: ph.ReplaceMessageText);
    }

    private static string DescribeEventAction(RedactionEventAction action) => action switch
    {
        RedactionEventAction.DropEvent => "dropEvent",
        RedactionEventAction.ReplaceMessage => "replaceMessage",
        _ => action.ToString(),
    };

    // --- Input splitting -------------------------------------------------
    //
    // The 'when' keyword is the boundary between this grammar and the Query
    // DSL. The split is lexical rather than grammatical because the two
    // parsers already have their own tokenizers with different expected
    // symbols; splitting first, parsing twice is simpler than weaving the
    // token streams together.

    private static (string Head, string? Predicate) SplitAtWhen(string rule) {
        var idx = FindWhenKeyword(rule);
        if (idx < 0) return (rule.Trim(), null);

        var head = rule[..idx].Trim();
        var tail = rule[(idx + "when".Length)..].Trim();
        return (head, tail.Length == 0 ? null : tail);
    }

    // Find the first 'when' that is a standalone word outside any quoted
    // string. Checks word boundaries on both sides so values like a field
    // named 'whenever' or a string literal 'when' are left alone.
    private static int FindWhenKeyword(string rule) {
        const string kw = "when";
        var span = rule.AsSpan();
        var inQuote = false;

        for (var i = 0; i < span.Length; i++) {
            if (span[i] == '"' && (i == 0 || span[i - 1] != '\\')) {
                inQuote = !inQuote;
                continue;
            }
            if (inQuote) continue;

            if (i + kw.Length > span.Length) break;
            if (i > 0 && !char.IsWhiteSpace(span[i - 1])) continue;
            if (!span.Slice(i, kw.Length).Equals(kw.AsSpan(), StringComparison.OrdinalIgnoreCase))
                continue;
            if (i + kw.Length < span.Length && !char.IsWhiteSpace(span[i + kw.Length])) continue;

            return i;
        }
        return -1;
    }

    // --- Small text helpers ---------------------------------------------

    private static string Unquote(string raw) {
        if (raw.Length >= 2 && raw[0] == '"' && raw[^1] == '"')
            return raw[1..^1];
        return raw;
    }

    private static char FirstCharOf(string raw) {
        var unquoted = Unquote(raw);
        return unquoted.Length > 0 ? unquoted[0] : '*';
    }
}

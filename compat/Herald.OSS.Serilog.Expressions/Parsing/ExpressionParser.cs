#nullable enable

using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Herald.OSS.Serilog.Expressions.Ast;
using Herald.OSS.Serilog.Expressions.Eval;
using Superpower;
using Superpower.Model;
using Superpower.Parsers;

namespace Herald.OSS.Serilog.Expressions.Parsing;

/// <summary>
/// Superpower parser for the Serilog expression DSL. Produces a value-producing
/// <see cref="ExpressionNode"/> AST.
///
/// <para>
/// Precedence, loosest to tightest:
/// <code>
/// ternary    := or ('?' or ':' or)?
/// or         := and ('or' and)*
/// and        := not ('and' not)*
/// not        := 'not' not | comparison
/// comparison := coalesce ( (= != &lt; &lt;= &gt; &gt;= ['ci']) coalesce
///                        | ('not'? 'like') coalesce
///                        | 'in' array
///                        | 'is' 'not'? 'null' )?
/// coalesce   := additive ('??' additive)*
/// additive   := multiplicative (('+' | '-') multiplicative)*
/// multiplic. := unary (('*' | '/' | '%') unary)*
/// unary      := '-' unary | postfix
/// postfix    := primary ('[' ternary ']')*
/// primary    := literal | array | accessor | function-call | property | '(' ternary ')'
/// </code>
/// NOT &gt; AND &gt; OR mirrors the Query DSL; the value-producing tiers are
/// net-new. Built-in function names resolve to delegates here so an unknown
/// function fails at config, never per event.
/// </para>
/// </summary>
internal static class ExpressionParser
{
    // --- keyword matchers (Identifier tokens recognised by text) -----------

    private static TokenListParser<ExpressionToken, Token<ExpressionToken>> Kw(string word) =>
        Token.EqualToValueIgnoreCase(ExpressionToken.Identifier, word);

    private static readonly TokenListParser<ExpressionToken, Token<ExpressionToken>> AndKw = Kw("and");
    private static readonly TokenListParser<ExpressionToken, Token<ExpressionToken>> OrKw = Kw("or");
    private static readonly TokenListParser<ExpressionToken, Token<ExpressionToken>> NotKw = Kw("not");
    private static readonly TokenListParser<ExpressionToken, Token<ExpressionToken>> LikeKw = Kw("like");
    private static readonly TokenListParser<ExpressionToken, Token<ExpressionToken>> InKw = Kw("in");
    private static readonly TokenListParser<ExpressionToken, Token<ExpressionToken>> IsKw = Kw("is");
    private static readonly TokenListParser<ExpressionToken, Token<ExpressionToken>> NullKw = Kw("null");
    private static readonly TokenListParser<ExpressionToken, Token<ExpressionToken>> CiKw = Kw("ci");

    // --- literals ----------------------------------------------------------

    private static readonly TokenListParser<ExpressionToken, ExpressionNode> StringLiteral =
        Token.EqualTo(ExpressionToken.String)
            .Select(t => (ExpressionNode)new LiteralNode(Unquote(t.ToStringValue())));

    private static readonly TokenListParser<ExpressionToken, ExpressionNode> NumberLiteral =
        Token.EqualTo(ExpressionToken.Number)
            .Select(t => (ExpressionNode)new LiteralNode(
                double.Parse(t.ToStringValue(), NumberStyles.Float, CultureInfo.InvariantCulture)));

    private static readonly TokenListParser<ExpressionToken, ExpressionNode> TrueLiteral =
        Kw("true").Select(_ => (ExpressionNode)new LiteralNode(true));
    private static readonly TokenListParser<ExpressionToken, ExpressionNode> FalseLiteral =
        Kw("false").Select(_ => (ExpressionNode)new LiteralNode(false));
    private static readonly TokenListParser<ExpressionToken, ExpressionNode> NullLiteral =
        NullKw.Select(_ => (ExpressionNode)new LiteralNode(null));

    // --- accessor: '@Level' etc. -------------------------------------------

    private static readonly TokenListParser<ExpressionToken, ExpressionNode> Accessor =
        from _at in Token.EqualTo(ExpressionToken.At)
        from name in Token.EqualTo(ExpressionToken.Identifier)
        select MapAccessor(name.ToStringValue());

    // --- property path: bare 'Name' or dotted 'a.b.c' ----------------------

    private static readonly TokenListParser<ExpressionToken, ExpressionNode> PropertyPath =
        from head in Token.EqualTo(ExpressionToken.Identifier)
        from tail in Token.EqualTo(ExpressionToken.Dot)
            .IgnoreThen(Token.EqualTo(ExpressionToken.Identifier)).Many()
        select (ExpressionNode)new PropertyNode(
            new[] { head.ToStringValue() }.Concat(tail.Select(t => t.ToStringValue())).ToArray());

    // --- array literal: '[' (ternary (',' ternary)*)? ']' ------------------

    private static readonly TokenListParser<ExpressionToken, ExpressionNode> ArrayLiteral =
        from _lb in Token.EqualTo(ExpressionToken.LBracket)
        from items in Superpower.Parse.Ref(() => Ternary!)
            .ManyDelimitedBy(Token.EqualTo(ExpressionToken.Comma))
        from _rb in Token.EqualTo(ExpressionToken.RBracket)
        select (ExpressionNode)new ArrayNode(items);

    // --- function call: Name '(' args ')' ----------------------------------
    // Resolved at parse time. Unknown function name → parse failure (config-time).

    private static readonly TokenListParser<ExpressionToken, ExpressionNode> FunctionCall =
        (from name in Token.EqualTo(ExpressionToken.Identifier)
         from _lp in Token.EqualTo(ExpressionToken.LParen)
         from args in Superpower.Parse.Ref(() => Ternary!)
             .ManyDelimitedBy(Token.EqualTo(ExpressionToken.Comma))
         from _rp in Token.EqualTo(ExpressionToken.RParen)
         select BuildFunctionCall(name.ToStringValue(), args))
        .Where(node => node is not null)
        .Select(node => node!);

    // --- primary -----------------------------------------------------------

    private static readonly TokenListParser<ExpressionToken, ExpressionNode> Parenthesised =
        from _lp in Token.EqualTo(ExpressionToken.LParen)
        from inner in Superpower.Parse.Ref(() => Ternary!)
        from _rp in Token.EqualTo(ExpressionToken.RParen)
        select inner;

    private static readonly TokenListParser<ExpressionToken, ExpressionNode> Primary =
        StringLiteral
            .Or(NumberLiteral)
            .Or(TrueLiteral)
            .Or(FalseLiteral)
            .Or(NullLiteral)
            .Or(Accessor)
            .Or(ArrayLiteral)
            .Or(Parenthesised)
            // FunctionCall must precede PropertyPath: both start with an
            // Identifier, the '(' disambiguates, Try backtracks on a bare name.
            .Or(FunctionCall.Try())
            .Or(PropertyPath);

    // --- postfix: primary ('[' index ']')* ---------------------------------

    private static readonly TokenListParser<ExpressionToken, ExpressionNode> Postfix =
        from target in Primary
        from indexers in (
            from _lb in Token.EqualTo(ExpressionToken.LBracket)
            from index in Superpower.Parse.Ref(() => Ternary!)
            from _rb in Token.EqualTo(ExpressionToken.RBracket)
            select index).Many()
        select indexers.Aggregate(target, (acc, idx) => new IndexerNode(acc, idx));

    // --- unary minus -------------------------------------------------------

    private static readonly TokenListParser<ExpressionToken, ExpressionNode> Unary =
        (from _m in Token.EqualTo(ExpressionToken.Minus)
         from operand in Superpower.Parse.Ref(() => Unary!)
         select (ExpressionNode)new NegateNode(operand))
        .Or(Postfix);

    // --- multiplicative / additive (left-folded) ---------------------------

    private static readonly TokenListParser<ExpressionToken, ExpressionNode> Multiplicative =
        Superpower.Parse.Chain(
            Token.EqualTo(ExpressionToken.Star).Select(_ => BinaryOperator.Multiply)
                .Or(Token.EqualTo(ExpressionToken.Slash).Select(_ => BinaryOperator.Divide))
                .Or(Token.EqualTo(ExpressionToken.Percent).Select(_ => BinaryOperator.Modulo)),
            Unary,
            (op, l, r) => (ExpressionNode)new BinaryNode(op, l, r));

    private static readonly TokenListParser<ExpressionToken, ExpressionNode> Additive =
        Superpower.Parse.Chain(
            Token.EqualTo(ExpressionToken.Plus).Select(_ => BinaryOperator.Add)
                .Or(Token.EqualTo(ExpressionToken.Minus).Select(_ => BinaryOperator.Subtract)),
            Multiplicative,
            (op, l, r) => (ExpressionNode)new BinaryNode(op, l, r));

    // --- coalesce (??) -----------------------------------------------------

    private static readonly TokenListParser<ExpressionToken, ExpressionNode> Coalesce =
        Superpower.Parse.Chain(
            Token.EqualTo(ExpressionToken.Coalesce),
            Additive,
            (_, l, r) => (ExpressionNode)new CoalesceNode(l, r));

    // --- comparison / like / in / is null ----------------------------------
    // A single optional operator after a coalesce operand.

    private static readonly TokenListParser<ExpressionToken, ExpressionNode> Comparison =
        from left in Coalesce
        from rest in ComparisonTail(left).OptionalOrDefault(left)
        select rest;

    private static TokenListParser<ExpressionToken, ExpressionNode> ComparisonTail(ExpressionNode left) =>
        // x is [not] null
        (from _is in IsKw
         from neg in NotKw.Optional()
         from _null in NullKw
         select (ExpressionNode)new IsNullNode(left, neg.HasValue))
        // x [not] like 'pat' [ci]
        .Or(from neg in NotKw.Optional()
            from _like in LikeKw
            from pat in Coalesce
            from ci in CiKw.Optional()
            select BuildLike(left, pat, neg.HasValue, ci.HasValue))
        // x in [ ... ]
        .Or(from _in in InKw
            from arr in ArrayLiteral
            from ci in CiKw.Optional()
            select (ExpressionNode)new InNode(left, ((ArrayNode)arr).Elements, ci.HasValue))
        // x <op> y [ci]
        .Or(from op in ComparisonOp
            from right in Coalesce
            from ci in CiKw.Optional()
            select (ExpressionNode)new BinaryNode(op, left, right, ci.HasValue));

    private static readonly TokenListParser<ExpressionToken, BinaryOperator> ComparisonOp =
        Token.EqualTo(ExpressionToken.Equals).Select(_ => BinaryOperator.Equal)
            .Or(Token.EqualTo(ExpressionToken.NotEquals).Select(_ => BinaryOperator.NotEqual))
            .Or(Token.EqualTo(ExpressionToken.GreaterEq).Select(_ => BinaryOperator.GreaterOrEqual))
            .Or(Token.EqualTo(ExpressionToken.Greater).Select(_ => BinaryOperator.Greater))
            .Or(Token.EqualTo(ExpressionToken.LessEq).Select(_ => BinaryOperator.LessOrEqual))
            .Or(Token.EqualTo(ExpressionToken.Less).Select(_ => BinaryOperator.Less));

    // --- not / and / or ----------------------------------------------------

    private static readonly TokenListParser<ExpressionToken, ExpressionNode> NotExpr =
        (from _not in NotKw
         from inner in Superpower.Parse.Ref(() => NotExpr!)
         select (ExpressionNode)new NotNode(inner))
        .Or(Comparison);

    private static readonly TokenListParser<ExpressionToken, ExpressionNode> AndExpr =
        Superpower.Parse.Chain(AndKw, NotExpr, (_, l, r) => (ExpressionNode)new BinaryNode(BinaryOperator.And, l, r));

    private static readonly TokenListParser<ExpressionToken, ExpressionNode> OrExpr =
        Superpower.Parse.Chain(OrKw, AndExpr, (_, l, r) => (ExpressionNode)new BinaryNode(BinaryOperator.Or, l, r));

    // --- ternary (loosest) -------------------------------------------------

    private static readonly TokenListParser<ExpressionToken, ExpressionNode> Ternary =
        from condition in OrExpr
        from branches in (
            from _q in Token.EqualTo(ExpressionToken.Question)
            from whenTrue in Superpower.Parse.Ref(() => Ternary!)
            from _c in Token.EqualTo(ExpressionToken.Colon)
            from whenFalse in Superpower.Parse.Ref(() => Ternary!)
            select (whenTrue, whenFalse)).Optional()
        select branches is { } b
            ? new ConditionalNode(condition, b.whenTrue, b.whenFalse)
            : condition;

    private static readonly TokenListParser<ExpressionToken, ExpressionNode> Grammar = Ternary.AtEnd();

    /// <summary>
    /// Parse an expression string to its AST. Throws
    /// <see cref="ExpressionParseException"/> on tokenizer or parser failure
    /// (including unknown function names) so misconfiguration fails at build
    /// time, not at first log call.
    /// </summary>
    public static ExpressionNode Parse(string input)
    {
        var tokens = ExpressionTokenizer.Instance.TryTokenize(input);
        if (!tokens.HasValue)
            throw new ExpressionParseException(tokens.ToString() ?? "tokenizer failure");

        var result = Grammar.TryParse(tokens.Value);
        if (!result.HasValue)
            throw new ExpressionParseException(result.ToString() ?? "parser failure");

        return result.Value;
    }

    // --- helpers -----------------------------------------------------------

    private static ExpressionNode MapAccessor(string name) => name.ToLowerInvariant() switch
    {
        "l" or "level" => new BuiltinAccessorNode(BuiltinAccessor.Level),
        "m" or "message" => new BuiltinAccessorNode(BuiltinAccessor.Message),
        "mt" or "messagetemplate" => new BuiltinAccessorNode(BuiltinAccessor.MessageTemplate),
        "t" or "timestamp" => new BuiltinAccessorNode(BuiltinAccessor.Timestamp),
        "x" or "exception" => new BuiltinAccessorNode(BuiltinAccessor.Exception),
        "p" or "properties" => new BuiltinAccessorNode(BuiltinAccessor.Properties),
        // An unknown '@name' is a config-time error — surfaces as parse failure
        // because the grammar has no other production that consumes it.
        _ => throw new ExpressionParseException($"unknown built-in accessor '@{name}'"),
    };

    private static ExpressionNode BuildLike(ExpressionNode target, ExpressionNode pattern, bool negated, bool ci)
    {
        // The pattern must be a string literal so the matcher compiles once at
        // config. A non-literal pattern is rejected loud rather than silently
        // recompiled per event.
        if (pattern is not LiteralNode { Value: string pat })
            throw new ExpressionParseException("the right side of 'like' must be a string literal");
        return new LikeNode(target, new LikeMatcher(pat, ci), negated);
    }

    // Resolve a function name to a node, or null when unknown so the Where()
    // filter rejects it and parsing fails loud.
    private static ExpressionNode? BuildFunctionCall(string name, IReadOnlyList<ExpressionNode> args) =>
        BuiltinFunctions.TryResolve(name, out var fn)
            ? new FunctionCallNode(name, fn, args)
            : null;

    // Unquote a single- or double-quoted literal. Single-quoted strings use ''
    // to embed a quote (Serilog); double-quoted use C-style backslash escapes.
    private static string Unquote(string raw)
    {
        if (raw.Length < 2) return raw;
        var quote = raw[0];
        if (quote != '\'' && quote != '"') return raw;
        if (raw[raw.Length - 1] != quote) return raw;

        var inner = raw.Substring(1, raw.Length - 2);
        var sb = new StringBuilder(inner.Length);
        for (var i = 0; i < inner.Length; i++)
        {
            var c = inner[i];
            if (quote == '\'' && c == '\'' && i + 1 < inner.Length && inner[i + 1] == '\'')
            {
                sb.Append('\'');
                i++;
                continue;
            }
            if (quote == '"' && c == '\\' && i + 1 < inner.Length)
            {
                var next = inner[++i];
                sb.Append(next switch
                {
                    'n' => '\n',
                    't' => '\t',
                    'r' => '\r',
                    '"' => '"',
                    '\\' => '\\',
                    _ => next,
                });
                continue;
            }
            sb.Append(c);
        }
        return sb.ToString();
    }
}

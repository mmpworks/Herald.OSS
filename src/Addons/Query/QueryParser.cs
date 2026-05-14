#nullable enable

using System.Collections.Generic;
using System.Linq;
using System.Text;
using Superpower;
using Superpower.Model;
using Superpower.Parsers;

namespace MMP.Herald.Addons.Query;

/// <summary>
/// Superpower-based parser for the Herald query DSL.
///
/// <para>
/// Grammar:
/// <code>
/// query    := or
/// or       := and ( OR and )*
/// and      := unary ( AND unary )*
/// unary    := NOT unary | atom
/// atom     := '(' or ')' | term
/// term     := field op value
/// field    := identifier ( '.' identifier )*
/// op       := ':' | '=' | '!=' | '~' | '>' | '>=' | '<' | '<='
/// value    := quoted-string | number | identifier
/// </code>
/// Keywords (AND, OR, NOT) are case-insensitive. Boolean precedence runs
/// NOT &gt; AND &gt; OR, matching how operators read naturally.
/// </para>
/// </summary>
internal static class QueryParser
{
    // Keyword matchers: an Identifier token whose text equals the keyword.
    private static readonly TokenListParser<QueryToken, Token<QueryToken>> AndKw =
        Token.EqualToValueIgnoreCase(QueryToken.Identifier, "and");
    private static readonly TokenListParser<QueryToken, Token<QueryToken>> OrKw =
        Token.EqualToValueIgnoreCase(QueryToken.Identifier, "or");
    private static readonly TokenListParser<QueryToken, Token<QueryToken>> NotKw =
        Token.EqualToValueIgnoreCase(QueryToken.Identifier, "not");

    // Field: identifier ( '.' identifier )*. Keywords are not excluded — users
    // may legitimately name a property "and" or "or" and the term-level parser
    // reaches here only after a higher level failed to consume an operator,
    // which means the current position is a bareword field name.
    private static readonly TokenListParser<QueryToken, IReadOnlyList<string>> FieldPath =
        from head in Token.EqualTo(QueryToken.Identifier)
        from tail in Token.EqualTo(QueryToken.Dot)
            .IgnoreThen(Token.EqualTo(QueryToken.Identifier)).Many()
        select (IReadOnlyList<string>)new[] { head.ToStringValue() }
            .Concat(tail.Select(t => t.ToStringValue()))
            .ToArray();

    // Value: quoted string, number, or bareword.
    private static readonly TokenListParser<QueryToken, QueryValue> Value =
        Token.EqualTo(QueryToken.String)
            .Select(t => new QueryValue(UnquoteString(t.ToStringValue()), true))
        .Or(Token.EqualTo(QueryToken.Number)
            .Select(t => new QueryValue(t.ToStringValue(), false)))
        .Or(Token.EqualTo(QueryToken.Identifier)
            .Select(t => new QueryValue(t.ToStringValue(), false)));

    // Comparison operator. Longest operator forms are already distinct token
    // kinds, so order-by-length handling is not needed here.
    private static readonly TokenListParser<QueryToken, QueryComparisonOperator> Op =
        Token.EqualTo(QueryToken.Colon).Select(_ => QueryComparisonOperator.Match)
            .Or(Token.EqualTo(QueryToken.Equals).Select(_ => QueryComparisonOperator.Equals))
            .Or(Token.EqualTo(QueryToken.NotEquals).Select(_ => QueryComparisonOperator.NotEquals))
            .Or(Token.EqualTo(QueryToken.Tilde).Select(_ => QueryComparisonOperator.Regex))
            .Or(Token.EqualTo(QueryToken.GreaterEq).Select(_ => QueryComparisonOperator.GreaterOrEqual))
            .Or(Token.EqualTo(QueryToken.Greater).Select(_ => QueryComparisonOperator.Greater))
            .Or(Token.EqualTo(QueryToken.LessEq).Select(_ => QueryComparisonOperator.LessOrEqual))
            .Or(Token.EqualTo(QueryToken.Less).Select(_ => QueryComparisonOperator.Less));

    private static readonly TokenListParser<QueryToken, QueryExpression> Term =
        from path in FieldPath
        from op in Op
        from value in Value
        select (QueryExpression)new TermExpression(path, op, value);

    // Parenthesised sub-expression or bare term. The reference to OrExpr is
    // resolved lazily so mutual recursion works despite static field ordering.
    private static readonly TokenListParser<QueryToken, QueryExpression> ParenOrTerm =
        (from _lp in Token.EqualTo(QueryToken.LParen)
         from inner in Superpower.Parse.Ref(() => OrExpr!)
         from _rp in Token.EqualTo(QueryToken.RParen)
         select inner)
        .Or(Term);

    // NOT is right-associative — `NOT NOT x` is legal.
    private static readonly TokenListParser<QueryToken, QueryExpression> Unary =
        (from _not in NotKw
         from inner in Superpower.Parse.Ref(() => Unary!)
         select (QueryExpression)new NotExpression(inner))
        .Or(ParenOrTerm);

    // AND binds tighter than OR. Parse.Chain folds left-to-right.
    private static readonly TokenListParser<QueryToken, QueryExpression> AndExpr =
        Superpower.Parse.Chain(AndKw, Unary, (_, l, r) => (QueryExpression)new AndExpression(l, r));

    private static readonly TokenListParser<QueryToken, QueryExpression> OrExpr =
        Superpower.Parse.Chain(OrKw, AndExpr, (_, l, r) => (QueryExpression)new OrExpression(l, r));

    private static readonly TokenListParser<QueryToken, QueryExpression> Grammar =
        OrExpr.AtEnd();

    /// <summary>
    /// Parse a query string into an AST, throwing <see cref="QueryParseException"/>
    /// on tokenizer or parser failure with a human-readable diagnostic.
    /// </summary>
    public static QueryExpression Parse(string input)
    {
        var tokens = QueryTokenizer.Instance.TryTokenize(input);
        if (!tokens.HasValue)
            throw new QueryParseException(tokens.ToString() ?? "tokenizer failure");

        var result = Grammar.TryParse(tokens.Value);
        if (!result.HasValue)
            throw new QueryParseException(result.ToString() ?? "parser failure");

        return result.Value;
    }

    // Unquote a double-quoted string, expanding the common C-style escapes.
    // Kept inline — the surface area is tiny and a dedicated helper class
    // would add indirection without benefit.
    private static string UnquoteString(string raw)
    {
        if (raw.Length < 2 || raw[0] != '"' || raw[raw.Length - 1] != '"')
            return raw;

        var inner = raw.Substring(1, raw.Length - 2);
        var sb = new StringBuilder(inner.Length);
        for (var i = 0; i < inner.Length; i++)
        {
            var c = inner[i];
            if (c == '\\' && i + 1 < inner.Length)
            {
                var next = inner[++i];
                sb.Append(next switch
                {
                    'n' => '\n',
                    't' => '\t',
                    'r' => '\r',
                    '"' => '"',
                    '\\' => '\\',
                    _ => next
                });
                continue;
            }
            sb.Append(c);
        }
        return sb.ToString();
    }
}

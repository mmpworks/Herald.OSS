#nullable enable

using Superpower;
using Superpower.Model;
using Superpower.Parsers;
using Superpower.Tokenizers;

namespace Herald.OSS.Serilog.Expressions.Parsing;

/// <summary>
/// Superpower tokenizer for the Serilog expression DSL.
///
/// <para>
/// Same combinator foundation as the Query DSL tokenizer, wider token set.
/// Two notable differences from the Query tokenizer: Serilog strings are
/// <b>single-quoted</b> with a doubled-quote (<c>''</c>) escape, and the
/// <c>@</c> accessor sigil is its own token so the parser can pair it with the
/// following built-in name.
/// </para>
/// </summary>
internal static class ExpressionTokenizer
{
    // Single-quoted string with '' as the embedded-quote escape — Serilog's
    // convention. A doubled quote inside the literal is two chars to skip; a
    // lone quote closes the string. Span.MatchedBy captures the whole run
    // including both delimiters so the token text round-trips to Unquote().
    private static readonly TextParser<char> EscapedOrInnerChar =
        Span.EqualTo("''").Value('\'')
            .Try()
            .Or(Character.ExceptIn('\''));

    private static readonly TextParser<TextSpan> SingleQuotedString =
        Span.MatchedBy(
            Character.EqualTo('\'')
                .IgnoreThen(EscapedOrInnerChar.Many())
                .IgnoreThen(Character.EqualTo('\'')));

    // Bareword: letter/underscore head, then letters/digits/underscore. No
    // hyphen here (unlike the Query DSL) — '-' is the arithmetic minus operator
    // in an expression language, so a bareword cannot swallow it.
    private static readonly TextParser<TextSpan> IdentifierParser =
        Span.MatchedBy(
            Character.Letter.Or(Character.EqualTo('_'))
                .IgnoreThen(
                    Character.LetterOrDigit
                        .Or(Character.EqualTo('_'))
                        .Many()));

    // Order matters: multi-char operators before their single-char prefixes,
    // quoted strings before identifiers, '??' before any single '?'.
    public static Tokenizer<ExpressionToken> Instance { get; } =
        new TokenizerBuilder<ExpressionToken>()
            .Ignore(Span.WhiteSpace)
            .Match(Character.EqualTo('('), ExpressionToken.LParen)
            .Match(Character.EqualTo(')'), ExpressionToken.RParen)
            .Match(Character.EqualTo('['), ExpressionToken.LBracket)
            .Match(Character.EqualTo(']'), ExpressionToken.RBracket)
            .Match(Character.EqualTo(','), ExpressionToken.Comma)
            .Match(Character.EqualTo('@'), ExpressionToken.At)
            .Match(Span.EqualTo("??"), ExpressionToken.Coalesce)
            .Match(Span.EqualTo("!="), ExpressionToken.NotEquals)
            .Match(Span.EqualTo("<>"), ExpressionToken.NotEquals)
            .Match(Span.EqualTo(">="), ExpressionToken.GreaterEq)
            .Match(Span.EqualTo("<="), ExpressionToken.LessEq)
            .Match(Character.EqualTo('='), ExpressionToken.Equals)
            .Match(Character.EqualTo('>'), ExpressionToken.Greater)
            .Match(Character.EqualTo('<'), ExpressionToken.Less)
            .Match(Character.EqualTo('+'), ExpressionToken.Plus)
            .Match(Character.EqualTo('-'), ExpressionToken.Minus)
            .Match(Character.EqualTo('*'), ExpressionToken.Star)
            .Match(Character.EqualTo('/'), ExpressionToken.Slash)
            .Match(Character.EqualTo('%'), ExpressionToken.Percent)
            .Match(Character.EqualTo('?'), ExpressionToken.Question)
            .Match(Character.EqualTo(':'), ExpressionToken.Colon)
            // '.' must come after numbers so a decimal literal isn't split.
            .Match(Numerics.DecimalDouble, ExpressionToken.Number)
            .Match(Character.EqualTo('.'), ExpressionToken.Dot)
            .Match(SingleQuotedString, ExpressionToken.String)
            .Match(QuotedString.CStyle, ExpressionToken.String)
            .Match(IdentifierParser, ExpressionToken.Identifier)
            .Build();
}

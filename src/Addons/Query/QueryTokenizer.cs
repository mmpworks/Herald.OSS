#nullable enable

using Superpower;
using Superpower.Model;
using Superpower.Parsers;
using Superpower.Tokenizers;

namespace MMP.Herald.Addons.Query;

/// <summary>
/// Superpower-based tokenizer for the Herald query DSL.
///
/// <para>
/// Keywords (AND / OR / NOT) are emitted as <see cref="QueryToken.Identifier"/>
/// and recognized by text in <see cref="QueryParser"/>. That keeps the
/// tokenizer small and lets a user quote "and" when they mean the literal
/// string.
/// </para>
/// </summary>
internal static class QueryTokenizer
{
    // Bareword: letter/underscore head, then letters/digits/underscore/hyphen.
    // Hyphen is allowed because categories and level keys commonly contain it
    // (e.g. `combat-ai`, `auth-login`).
    private static readonly TextParser<TextSpan> IdentifierParser =
        Span.MatchedBy(
            Character.Letter.Or(Character.EqualTo('_'))
                .IgnoreThen(
                    Character.LetterOrDigit
                        .Or(Character.EqualTo('_'))
                        .Or(Character.EqualTo('-'))
                        .Many()));

    // Order matters: multi-character operators (!=, >=, <=) must be matched
    // before single-character ones, and quoted strings before identifiers.
    public static Tokenizer<QueryToken> Instance { get; } =
        new TokenizerBuilder<QueryToken>()
            .Ignore(Span.WhiteSpace)
            .Match(Character.EqualTo('('), QueryToken.LParen)
            .Match(Character.EqualTo(')'), QueryToken.RParen)
            .Match(Character.EqualTo('.'), QueryToken.Dot)
            .Match(Character.EqualTo(':'), QueryToken.Colon)
            .Match(Character.EqualTo('~'), QueryToken.Tilde)
            .Match(Span.EqualTo("!="), QueryToken.NotEquals)
            .Match(Span.EqualTo(">="), QueryToken.GreaterEq)
            .Match(Span.EqualTo("<="), QueryToken.LessEq)
            .Match(Character.EqualTo('='), QueryToken.Equals)
            .Match(Character.EqualTo('>'), QueryToken.Greater)
            .Match(Character.EqualTo('<'), QueryToken.Less)
            .Match(QuotedString.CStyle, QueryToken.String)
            .Match(Numerics.DecimalDouble, QueryToken.Number)
            .Match(IdentifierParser, QueryToken.Identifier)
            .Build();
}

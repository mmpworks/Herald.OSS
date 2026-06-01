#nullable enable

namespace Herald.OSS.Serilog.Expressions.Parsing;

/// <summary>
/// Token kinds for the Serilog expression DSL.
///
/// <para>
/// Wider than the Query DSL's <c>QueryToken</c> set: the expression language is
/// value-producing, so it carries array/grouping punctuation, arithmetic
/// operators, the ternary <c>?</c>/<c>:</c>, the <c>??</c> coalesce, and the
/// <c>@</c>-prefixed built-in accessors. Keywords (<c>and</c>/<c>or</c>/
/// <c>not</c>/<c>like</c>/<c>in</c>/<c>is</c>/<c>null</c>/<c>true</c>/
/// <c>false</c>/<c>ci</c>) are tokenized as <see cref="Identifier"/> and
/// recognised by text in the parser, matching the Query DSL convention.
/// </para>
/// </summary>
public enum ExpressionToken
{
    None,

    // Grouping / structure
    LParen,
    RParen,
    LBracket,
    RBracket,
    Comma,
    Dot,

    // Accessor sigil — '@Level', '@Properties', etc. The accessor name follows
    // as an Identifier the parser pairs with this sigil.
    At,

    // Comparison
    Equals,        // '='
    NotEquals,     // '!=' or '<>'
    Greater,       // '>'
    GreaterEq,     // '>='
    Less,          // '<'
    LessEq,        // '<='

    // Arithmetic
    Plus,          // '+'
    Minus,         // '-'
    Star,          // '*'
    Slash,         // '/'
    Percent,       // '%'

    // Ternary / coalesce
    Question,      // '?'
    Colon,         // ':'
    Coalesce,      // '??'

    // Terminals
    String,        // single- or double-quoted
    Number,        // 123 or 12.5
    Identifier,    // bareword; also carries keywords (and/or/not/like/in/is/null/true/false/ci)
}

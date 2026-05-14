#nullable enable

namespace MMP.Herald.Addons.Query;

/// <summary>
/// Token kinds for the Herald query DSL.
///
/// <para>
/// Keywords (AND, OR, NOT) are tokenized as <see cref="Identifier"/> and
/// recognized by text in the parser. This keeps the tokenizer small and
/// lets keywords work as values too when quoted.
/// </para>
/// </summary>
public enum QueryToken
{
    None,

    // Structural
    LParen,
    RParen,
    Dot,

    // Binary operators between field and value
    Colon,        // ':'  — natural match (substring for strings, eq for numbers)
    Equals,       // '='  — strict equals
    NotEquals,    // '!=' — strict not equals
    Tilde,        // '~'  — regex match
    Greater,      // '>'
    GreaterEq,    // '>='
    Less,         // '<'
    LessEq,       // '<='

    // Terminals
    String,       // quoted "…"
    Number,       // 123 or 12.5
    Identifier,   // bareword; also carries AND / OR / NOT keywords
}

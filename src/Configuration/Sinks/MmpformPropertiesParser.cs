// Copyright (c) 2026 MMP LLC
// Licensed under the MIT License. See LICENSE in the project root.
#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;

namespace MMP.Herald.Configuration.Sinks;

/// <summary>
/// Reads the <c>__properties = [ … ]</c> block out of a sink's mmpform
/// text and returns the contract dictionary the rest of Herald uses to
/// fill JSON config defaults and to validate that no property silently
/// goes missing in transit.
///
/// <para>This parser is intentionally narrow. It only recognises the
/// <c>__properties</c> block; the rest of the mmpform grammar — layouts,
/// containers, controls, fragments — is the dashboard renderer's
/// concern. Keeping the parser narrow lets Core read the contract
/// without pulling in the full FormBuilderDSL surface.</para>
///
/// <para><b>Why Core gets to see this.</b> Core does not interpret
/// property values. The parser turns the contract text into a typed
/// dictionary; QuickLogBuilder uses it to fill missing keys with
/// defaults so the JSON it emits is complete; the management API uses
/// it to merge defaults with current values when publishing to the
/// dashboard. Sinks themselves still decide what each property means.
/// </para>
/// </summary>
public static class MmpformPropertiesParser
{
    /// <summary>
    /// Returns the parsed <c>__properties</c> entries in declaration
    /// order, or an empty list when the text contains no
    /// <c>__properties</c> block. Throws
    /// <see cref="InvalidOperationException"/> when the block is
    /// present but malformed — the contract is mandatory once it
    /// appears, so a broken block is a hard error rather than a silent
    /// fallback to "no properties".
    /// </summary>
    public static IReadOnlyList<MmpformPropertyDefinition> Parse(string? mmpformText)
    {
        if (string.IsNullOrWhiteSpace(mmpformText))
            return Array.Empty<MmpformPropertyDefinition>();

        var blockStart = FindBlockStart(mmpformText);
        if (blockStart < 0)
            return Array.Empty<MmpformPropertyDefinition>();

        var lexer = new Lexer(mmpformText, blockStart);
        return ParseEntries(lexer);
    }

    /// <summary>
    /// Convenience: pivot the parser output into a name-keyed lookup so
    /// callers that only need defaults (the publish/round-trip paths)
    /// do not have to write the loop themselves. Order is not preserved
    /// in the returned dictionary; callers that need the original order
    /// should use <see cref="Parse"/> directly.
    /// </summary>
    public static IReadOnlyDictionary<string, MmpformPropertyDefinition> ParseAsDictionary(string? mmpformText)
    {
        var list = Parse(mmpformText);
        var map = new Dictionary<string, MmpformPropertyDefinition>(list.Count, StringComparer.Ordinal);
        foreach (var entry in list)
            map[entry.Name] = entry;
        return map;
    }

    // ── Block discovery ──────────────────────────────────────────────

    // Locate the `__properties = [` opening. Skip lines that are blank
    // or comments (lines whose first non-space char is `#`); anything
    // else is left to the parser to handle. Returns the offset of the
    // `[` token, or -1 when the document does not declare the block.
    private static int FindBlockStart(string text)
    {
        var i = 0;
        while (i < text.Length)
        {
            // Skip whitespace.
            while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
            if (i >= text.Length) break;

            // Skip comment lines.
            if (text[i] == '#')
            {
                while (i < text.Length && text[i] != '\n') i++;
                continue;
            }

            // Look for the literal `__properties` identifier.
            const string Marker = "__properties";
            if (i + Marker.Length <= text.Length
                && string.CompareOrdinal(text, i, Marker, 0, Marker.Length) == 0
                && (i + Marker.Length == text.Length || !IsIdentChar(text[i + Marker.Length])))
            {
                i += Marker.Length;
                while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
                if (i < text.Length && text[i] == '=') i++;
                while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
                if (i < text.Length && text[i] == '[') return i;
                throw new InvalidOperationException("__properties block must open with '['.");
            }

            // Not the marker — advance past the current line and keep
            // searching. The mmpform allows other top-level decls
            // before / after __properties, so we don't fail here.
            while (i < text.Length && text[i] != '\n') i++;
        }
        return -1;
    }

    // ── Entry parsing ────────────────────────────────────────────────

    private static IReadOnlyList<MmpformPropertyDefinition> ParseEntries(Lexer lex)
    {
        var entries = new List<MmpformPropertyDefinition>();
        lex.Expect('[');
        if (lex.Peek() == ']')
        {
            lex.Advance();
            return entries;
        }

        while (true)
        {
            entries.Add(ParseSingleEntry(lex));
            lex.SkipTrivia();
            if (lex.Peek() == ',')
            {
                lex.Advance();
                lex.SkipTrivia();
                if (lex.Peek() == ']') break;            // trailing comma — fine
                continue;
            }
            break;
        }

        lex.Expect(']');
        return entries;
    }

    // "name" = { type: "T", default: V }
    private static MmpformPropertyDefinition ParseSingleEntry(Lexer lex)
    {
        lex.SkipTrivia();
        var name = lex.ReadString();
        lex.SkipTrivia();
        lex.Expect('=');
        lex.SkipTrivia();
        lex.Expect('{');

        string? type = null;
        var hasDefault = false;
        object? defaultValue = null;

        while (true)
        {
            lex.SkipTrivia();
            if (lex.Peek() == '}')
            {
                lex.Advance();
                break;
            }

            var field = lex.ReadIdent();
            lex.SkipTrivia();
            lex.Expect(':');
            lex.SkipTrivia();
            var raw = ParseLiteral(lex);
            if (string.Equals(field, "type", StringComparison.Ordinal))
                type = raw as string ?? throw new InvalidOperationException(
                    $"__properties['{name}'].type must be a string literal.");
            else if (string.Equals(field, "default", StringComparison.Ordinal))
            {
                hasDefault = true;
                defaultValue = raw;
            }
            // Unknown field keys are ignored — leaves room for the
            // mmpform spec to grow without breaking older parsers.

            lex.SkipTrivia();
            if (lex.Peek() == ',')
            {
                lex.Advance();
                continue;
            }
            lex.Expect('}');
            break;
        }

        if (type is null)
            throw new InvalidOperationException($"__properties['{name}'] is missing a 'type' field.");

        if (!hasDefault) defaultValue = ZeroForType(type);
        else defaultValue = CoerceToDeclaredType(name, type, defaultValue);

        return new MmpformPropertyDefinition(name, type, defaultValue);
    }

    // Parses one literal: quoted string, number, true/false, null, or
    // [ list of literals ]. Mirrors the JS parser's _parsePropertyValue
    // shape so saved files round-trip identically.
    private static object? ParseLiteral(Lexer lex)
    {
        lex.SkipTrivia();
        var c = lex.Peek();
        if (c == '"') return lex.ReadString();
        if (c == '[')
        {
            lex.Advance();
            var list = new List<object?>();
            lex.SkipTrivia();
            if (lex.Peek() == ']')
            {
                lex.Advance();
                return list;
            }
            while (true)
            {
                list.Add(ParseLiteral(lex));
                lex.SkipTrivia();
                if (lex.Peek() == ',')
                {
                    lex.Advance();
                    lex.SkipTrivia();
                    if (lex.Peek() == ']') break;
                    continue;
                }
                break;
            }
            lex.Expect(']');
            return list;
        }
        // Identifier literal — true / false / null only.
        if (IsIdentStart(c))
        {
            var ident = lex.ReadIdent();
            return ident switch
            {
                "true" => true,
                "false" => false,
                "null" => null,
                _ => throw new InvalidOperationException(
                    $"Unexpected identifier '{ident}' in __properties literal.")
            };
        }
        // Number literal — supports optional sign, decimals, scientific.
        if (c == '-' || c == '+' || (c >= '0' && c <= '9'))
            return lex.ReadNumber();

        throw new InvalidOperationException(
            $"Unexpected character '{c}' while parsing __properties literal.");
    }

    // ── Type coercion ────────────────────────────────────────────────

    // The mmpform allows authors to write numeric defaults as quoted
    // strings (e.g. `default: "50"`) — the renderer treats it as text.
    // For the C# contract we coerce to the declared type so consumers
    // do not each repeat the parse. Bool / string defaults pass through
    // unchanged. Empty strings on numeric types map to null so a sink
    // can detect "no default set" without sentinel constants.
    private static object? CoerceToDeclaredType(string name, string type, object? raw)
    {
        switch (type)
        {
            case "int":
                return raw switch
                {
                    null => null,
                    long l => l,
                    double d => (long)d,
                    string s when string.IsNullOrEmpty(s) => null,
                    string s when long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) => n,
                    _ => throw new InvalidOperationException(
                        $"__properties['{name}'].default cannot be coerced to int (value: {raw}).")
                };
            case "float":
                return raw switch
                {
                    null => null,
                    double d => d,
                    long l => (double)l,
                    string s when string.IsNullOrEmpty(s) => null,
                    string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) => n,
                    _ => throw new InvalidOperationException(
                        $"__properties['{name}'].default cannot be coerced to float (value: {raw}).")
                };
            case "bool":
                return raw switch
                {
                    bool b => b,
                    string s when bool.TryParse(s, out var b) => b,
                    _ => throw new InvalidOperationException(
                        $"__properties['{name}'].default cannot be coerced to bool (value: {raw}).")
                };
            case "string":
            case "string[]":
                return raw;
            default:
                // Unknown type — keep the raw literal. Forward-compatible
                // with future mmpform type tags.
                return raw;
        }
    }

    private static object? ZeroForType(string type) => type switch
    {
        "int"      => 0L,
        "float"    => 0.0,
        "bool"     => false,
        "string[]" => string.Empty,
        _          => string.Empty,
    };

    // ── Lexer ────────────────────────────────────────────────────────

    private static bool IsIdentStart(char c) =>
        c == '_' || (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z');

    private static bool IsIdentChar(char c) =>
        IsIdentStart(c) || (c >= '0' && c <= '9');

    private sealed class Lexer
    {
        private readonly string _text;
        private int _pos;

        public Lexer(string text, int startPos)
        {
            _text = text;
            _pos = startPos;
        }

        public char Peek()
        {
            SkipTrivia();
            return _pos < _text.Length ? _text[_pos] : '\0';
        }

        public void Advance() => _pos++;

        public void Expect(char c)
        {
            SkipTrivia();
            if (_pos >= _text.Length || _text[_pos] != c)
                throw new InvalidOperationException(
                    $"Expected '{c}' at offset {_pos} in __properties block.");
            _pos++;
        }

        public void SkipTrivia()
        {
            while (_pos < _text.Length)
            {
                var c = _text[_pos];
                if (char.IsWhiteSpace(c)) { _pos++; continue; }
                if (c == '#')
                {
                    while (_pos < _text.Length && _text[_pos] != '\n') _pos++;
                    continue;
                }
                break;
            }
        }

        public string ReadString()
        {
            SkipTrivia();
            if (_pos >= _text.Length || _text[_pos] != '"')
                throw new InvalidOperationException(
                    $"Expected '\"' at offset {_pos} in __properties block.");
            _pos++;

            var sb = new System.Text.StringBuilder();
            while (_pos < _text.Length)
            {
                var c = _text[_pos];
                if (c == '\\' && _pos + 1 < _text.Length)
                {
                    var next = _text[_pos + 1];
                    sb.Append(next switch
                    {
                        '"'  => '"',
                        '\\' => '\\',
                        'n'  => '\n',
                        'r'  => '\r',
                        't'  => '\t',
                        _    => next,
                    });
                    _pos += 2;
                    continue;
                }
                if (c == '"')
                {
                    _pos++;
                    return sb.ToString();
                }
                sb.Append(c);
                _pos++;
            }
            throw new InvalidOperationException("Unterminated string literal in __properties block.");
        }

        public string ReadIdent()
        {
            SkipTrivia();
            var start = _pos;
            if (_pos >= _text.Length || !IsIdentStart(_text[_pos]))
                throw new InvalidOperationException(
                    $"Expected identifier at offset {_pos} in __properties block.");
            while (_pos < _text.Length && IsIdentChar(_text[_pos])) _pos++;
            return _text.Substring(start, _pos - start);
        }

        public object ReadNumber()
        {
            SkipTrivia();
            var start = _pos;
            if (_pos < _text.Length && (_text[_pos] == '-' || _text[_pos] == '+')) _pos++;
            var hasDot = false;
            var hasExp = false;
            while (_pos < _text.Length)
            {
                var c = _text[_pos];
                if (c >= '0' && c <= '9') { _pos++; continue; }
                if (c == '.' && !hasDot) { hasDot = true; _pos++; continue; }
                if ((c == 'e' || c == 'E') && !hasExp)
                {
                    hasExp = true;
                    _pos++;
                    if (_pos < _text.Length && (_text[_pos] == '-' || _text[_pos] == '+')) _pos++;
                    continue;
                }
                break;
            }
            var slice = _text.Substring(start, _pos - start);
            if (!hasDot && !hasExp
                && long.TryParse(slice, NumberStyles.Integer, CultureInfo.InvariantCulture, out var lv))
                return lv;
            if (double.TryParse(slice, NumberStyles.Float, CultureInfo.InvariantCulture, out var dv))
                return dv;
            throw new InvalidOperationException($"Invalid number literal '{slice}' in __properties block.");
        }
    }
}

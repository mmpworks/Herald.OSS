// Copyright (c) 2026 MMPWorks LLC
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.
#nullable enable

using System.Collections.Generic;

namespace MMP.Herald.Addons.ManagementApi;

/// <summary>
/// Validates and sanitizes HTTP header name/value pairs flowing in
/// from operator-supplied JSON config bodies (HTTP/OTLP/Elasticsearch/
/// Webhook sinks). Closes the standard CRLF / header-injection vector
/// where an attacker-controlled <c>headers</c> entry contains either
/// (a) a name with characters outside the RFC 7230 token grammar,
/// which downstream HTTP clients reject in surprising ways, or
/// (b) a value with embedded CR / LF that splits a single header
/// into two requests on the wire.
///
/// <para><b>Policy.</b> Bad entries are dropped silently. The header
/// surface is operator-supplied configuration; an invalid header
/// should never crash sink wiring or fail the boot of a pipeline that
/// is otherwise valid. The drop is the same shape the existing
/// readers already use for non-string values.</para>
///
/// <para><b>Why a separate type.</b> Two code paths consume the same
/// shape: <see cref="HeraldManagementApi"/>'s <c>ReadHeadersProperty</c>
/// reads from a raw <see cref="System.Text.Json.JsonElement"/>;
/// <see cref="NetworkSinkDispatch.ReadHeaders"/> reads from the bag
/// the JSON converter has already projected to
/// <c>Dictionary&lt;string,object?&gt;</c>. Both produce a
/// <c>Dictionary&lt;string,string&gt;</c> in the end. This helper is
/// the single place that decides whether each pair is safe to forward.</para>
/// </summary>
internal static class HttpHeaderSanitizer
{
    /// <summary>
    /// True when <paramref name="name"/> is a valid RFC 7230 token —
    /// the set HTTP/1.1 allows for header field names. The set is
    /// ASCII letters, digits, and the marks
    /// <c>! # $ % &amp; ' * + - . ^ _ ` | ~</c>. Anything else
    /// (whitespace, CR/LF, control chars, non-ASCII) fails.
    /// </summary>
    public static bool IsValidHeaderName(string? name)
    {
        if (string.IsNullOrEmpty(name)) return false;

        // Cognitive note: scan once, return on first invalid byte.
        // The token set is small enough that a switch on the char's
        // numeric range is the simplest readable form.
        foreach (var ch in name!)
        {
            if (!IsTokenChar(ch)) return false;
        }
        return true;
    }

    /// <summary>
    /// True when <paramref name="value"/> contains no CR, LF, or NUL
    /// — the three bytes that turn a single header into two requests
    /// on the wire (CRLF injection) or terminate the value early on
    /// some HTTP clients (NUL).
    /// </summary>
    public static bool IsSafeHeaderValue(string? value)
    {
        if (value is null) return false;

        foreach (var ch in value)
        {
            if (ch == '\r' || ch == '\n' || ch == '\0') return false;
        }
        return true;
    }

    /// <summary>
    /// Filter a header dictionary down to the entries that pass both
    /// <see cref="IsValidHeaderName"/> and <see cref="IsSafeHeaderValue"/>.
    /// Returns <c>null</c> when the result is empty so callers that
    /// branch on "any headers?" see the pre-existing shape exactly.
    /// </summary>
    public static IReadOnlyDictionary<string, string>? Sanitize(
        IReadOnlyDictionary<string, string>? headers)
    {
        if (headers is null || headers.Count == 0) return null;

        var safe = new Dictionary<string, string>(System.StringComparer.Ordinal);
        foreach (var (name, value) in headers)
        {
            if (!IsValidHeaderName(name)) continue;
            if (!IsSafeHeaderValue(value)) continue;
            safe[name] = value;
        }
        return safe.Count == 0 ? null : safe;
    }

    private static bool IsTokenChar(char ch)
    {
        // ALPHA / DIGIT
        if ((ch >= 'a' && ch <= 'z')
         || (ch >= 'A' && ch <= 'Z')
         || (ch >= '0' && ch <= '9')) return true;

        // RFC 7230 token marks. Single switch over a small constant
        // set keeps Cognitive Complexity below the radar; a regex
        // would be more flexible but pays a per-check cost we don't
        // need here.
        switch (ch)
        {
            case '!':
            case '#':
            case '$':
            case '%':
            case '&':
            case '\'':
            case '*':
            case '+':
            case '-':
            case '.':
            case '^':
            case '_':
            case '`':
            case '|':
            case '~':
                return true;
            default:
                return false;
        }
    }
}

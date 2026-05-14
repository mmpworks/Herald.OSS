#nullable enable

using System;
using System.Collections.Generic;
using System.Text;

namespace MMP.Herald.Templating;

/// <summary>
/// Shared, stateless tokenizer used by every <see cref="ITemplateParseStrategy"/>
/// on a cold parse. Caching is a strategy concern; this class only turns a
/// template string into a token tree.
///
/// Cold-path strategy:
///   1. Pre-size the token list from a SIMD '{'-count pass.
///   2. SIMD scan (IndexOfAny) jumps from brace to brace instead of walking
///      char by char.
///   3. Escape-free text runs are taken as direct slices of the template —
///      no StringBuilder, no per-char append.
///   4. On '{{' or '}}', we promote to a StringBuilder lazily to splice the
///      literal brace between slices. Buffer is released after each text
///      segment so escape-free runs later in the template stay allocation-free.
///   5. <see cref="ParsePropertyToken"/> operates on <see cref="ReadOnlySpan{Char}"/>;
///      only the final Name, optional Format, and RawText escape to the heap.
/// </summary>
internal static class TemplateTokenizer
{
    public static MessageTemplate Tokenize(string template) {
        var span = template.AsSpan();

        // Over-counts by the second '{' of each '{{' escape; List<T> tolerates
        // the slack and the Count call itself is SIMD-fast.
        var openCount = span.Count('{');
        var tokens = new List<MessageTemplateToken>(capacity: openCount * 2 + 1);

        // textStart: start of the current text segment (used by the slice path).
        // pendingStart: when a buffer is in use, the next slice to be flushed.
        var textStart = 0;
        var pendingStart = 0;
        var index = 0;
        StringBuilder? textBuffer = null;

        while (index < template.Length)
        {
            var relativeHit = span[index..].IndexOfAny('{', '}');
            if (relativeHit < 0) break;

            var hit = index + relativeHit;
            var ch = template[hit];
            var isEscape = hit + 1 < template.Length && template[hit + 1] == ch;

            if (isEscape)
            {
                textBuffer = AppendEscape(textBuffer, template, textStart, pendingStart, hit);
                pendingStart = hit + 2;
                index = hit + 2;
                continue;
            }

            if (ch == '}')
            {
                // Orphan '}' inside text — preserve original behavior (literal char).
                index = hit + 1;
                continue;
            }

            // Real property: flush accumulated text, parse, reset segment state.
            FlushText(tokens, template, textStart, hit, textBuffer, pendingStart);
            textBuffer = null;

            var propertyIndex = hit;
            tokens.Add(ParsePropertyToken(template, ref propertyIndex));

            index = propertyIndex;
            textStart = propertyIndex;
            pendingStart = propertyIndex;
        }

        FlushText(tokens, template, textStart, template.Length, textBuffer, pendingStart);

        return new MessageTemplate(template, tokens);
    }

    private static StringBuilder AppendEscape(
        StringBuilder? textBuffer,
        string template,
        int textStart,
        int pendingStart,
        int hit) {
        if (textBuffer is null)
        {
            // First escape in this segment. Seed the buffer with everything up
            // to and including the first literal brace.
            var buffer = new StringBuilder();
            buffer.Append(template, textStart, hit + 1 - textStart);
            return buffer;
        }

        textBuffer.Append(template, pendingStart, hit + 1 - pendingStart);
        return textBuffer;
    }

    private static void FlushText(
        List<MessageTemplateToken> tokens,
        string template,
        int textStart,
        int textEnd,
        StringBuilder? textBuffer,
        int pendingStart) {
        if (textBuffer is not null)
        {
            if (pendingStart < textEnd)
                textBuffer.Append(template, pendingStart, textEnd - pendingStart);
            if (textBuffer.Length > 0)
                tokens.Add(new MessageTemplateToken.Text(textBuffer.ToString()));
            return;
        }

        if (textStart < textEnd)
            tokens.Add(new MessageTemplateToken.Text(template[textStart..textEnd]));
    }

    private static MessageTemplateToken.Property ParsePropertyToken(
        string template, ref int index) {
        var start = index;
        var contentStart = index + 1;
        var end = template.IndexOf('}', contentStart);

        if (end < 0)
            throw new InvalidOperationException(
                $"Unclosed message template property starting at index {index}.");

        // Work on spans; only materialize strings for the fields stored on the
        // token (Name, optional Format, RawText).
        var content = template.AsSpan(contentStart, end - contentStart).Trim();

        if (content.IsEmpty)
            throw new InvalidOperationException(
                $"Empty message template property at index {index}.");

        var captureMode = LogPropertyCaptureMode.Default;
        var nameAndFormat = content;

        if (content[0] == '@')
        {
            captureMode = LogPropertyCaptureMode.Destructure;
            nameAndFormat = content[1..];
        }
        else if (content[0] == '$')
        {
            captureMode = LogPropertyCaptureMode.Stringify;
            nameAndFormat = content[1..];
        }

        var formatSepIndex = nameAndFormat.IndexOf(':');
        var nameSpan = formatSepIndex >= 0
            ? nameAndFormat[..formatSepIndex].Trim()
            : nameAndFormat.Trim();
        var formatSpan = formatSepIndex >= 0
            ? nameAndFormat[(formatSepIndex + 1)..].Trim()
            : ReadOnlySpan<char>.Empty;

        if (nameSpan.IsEmpty)
            throw new InvalidOperationException(
                $"Empty message template property at index {index}.");

        index = end + 1;

        return new MessageTemplateToken.Property(
            Name: nameSpan.ToString(),
            CaptureMode: captureMode,
            Format: formatSpan.IsEmpty ? null : formatSpan.ToString(),
            RawText: template[start..(end + 1)]);
    }
}

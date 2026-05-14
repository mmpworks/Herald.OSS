#nullable enable

using System;
using System.Security.Cryptography;
using System.Text;

namespace MMP.Herald.Output.Rendering;

/// <summary>
/// Shared redaction logic used by both RedactionProcessor (output pipeline)
/// and CompiledRedactionProcessor (event pipeline). Eliminates duplication
/// of ApplyRedaction, MaskValue, and HashValue across the two processors.
/// </summary>
internal static class RedactionHelper
{
    public static string Apply(string value, RedactionMode mode, char maskChar, int visibleChars) {
        return mode.Value switch
        {
            "Remove" => "[REDACTED]",
            "Mask" => MaskValue(value, maskChar, visibleChars),
            "Hash" => HashValue(value),
            _ => value
        };
    }

    // Mask: stackalloc a char span sized to the input, fill the leading
    // (length - visibleChars) slots with maskChar, copy the trailing
    // visibleChars from the input, then one final `new string(span)` alloc.
    // Replaces the prior 2-3-alloc concat path (new string × N maskChar +
    // value[^visibleChars..] substring + concat).
    public static string MaskValue(string value, char maskChar, int visibleChars) {
        if (value.Length <= visibleChars)
            return new string(maskChar, value.Length);

        var total = value.Length;
        Span<char> buffer = total <= StackallocCharThreshold
            ? stackalloc char[total]
            : new char[total];
        var maskedLen = total - visibleChars;
        buffer[..maskedLen].Fill(maskChar);
        value.AsSpan(maskedLen).CopyTo(buffer[maskedLen..]);
        return new string(buffer);
    }

    // Hash: SHA-256 → first 8 bytes → 16 lowercase hex chars, prefixed
    // with "sha256:". Single allocation for the final string.
    //
    // Stackalloc everywhere intermediate. The prior path was 6 allocations
    // per call (UTF8 byte[], 32-B digest array, Convert.ToHexString string,
    // [..16] substring, ToLowerInvariant copy, final interpolated string).
    public static string HashValue(string value) {
        var byteCount = Encoding.UTF8.GetByteCount(value);
        Span<byte> inputBytes = byteCount <= StackallocByteThreshold
            ? stackalloc byte[byteCount]
            : new byte[byteCount];
        Encoding.UTF8.GetBytes(value, inputBytes);

        Span<byte> digest = stackalloc byte[Sha256DigestSize];
        SHA256.HashData(inputBytes, digest);

        // "sha256:" (7) + 16 lowercase hex chars = 23 chars total.
        Span<char> chars = stackalloc char[HashStringLength];
        chars[0] = 's'; chars[1] = 'h'; chars[2] = 'a';
        chars[3] = '2'; chars[4] = '5'; chars[5] = '6';
        chars[6] = ':';
        var hex = chars[7..];
        for (var i = 0; i < HashVisibleBytes; i++)
        {
            var b = digest[i];
            hex[i * 2] = HexChar(b >> 4);
            hex[i * 2 + 1] = HexChar(b & 0xF);
        }
        return new string(chars);
    }

    // 1024 bytes is enough for any realistic sensitive value (passwords,
    // tokens, identifiers). Inputs above the threshold fall back to heap.
    private const int StackallocByteThreshold = 1024;
    private const int StackallocCharThreshold = 512;
    private const int Sha256DigestSize = 32;
    private const int HashVisibleBytes = 8;
    private const int HashStringLength = 7 + HashVisibleBytes * 2; // "sha256:" + 16 hex chars

    private static char HexChar(int nibble) =>
        (char)(nibble < 10 ? '0' + nibble : 'a' + nibble - 10);
}

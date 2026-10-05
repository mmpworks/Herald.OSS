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
    // Minimum HMAC key length for KeyedHash rules.
    public const int MinimumHashKeyBytes = 16;

    public static string Apply(string value, RedactionMode mode, char maskChar, int visibleChars, byte[]? hashKey = null) {
        return mode.Value switch
        {
            "Remove" => "[REDACTED]",
            "Mask" => MaskValue(value, maskChar, visibleChars),
            "Hash" => HashValue(value),
            // Fail closed: a keyed rule without a usable key never falls back to an unkeyed hash.
            // Processor constructors reject such rules first (RequireUsableKey).
            "KeyedHash" => hashKey is { Length: >= MinimumHashKeyBytes } ? KeyedHashValue(value, hashKey) : "[REDACTED]",
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

    /// <summary>
    /// Throws when <paramref name="mode"/> is <see cref="RedactionMode.KeyedHash"/> and
    /// <paramref name="hashKey"/> is missing or shorter than <see cref="MinimumHashKeyBytes"/>.
    /// Every processor that accepts rules calls this at construction.
    /// </summary>
    public static void RequireUsableKey(RedactionMode mode, byte[]? hashKey, string ruleName) {
        if (mode.Value != "KeyedHash") return;
        if (hashKey is { Length: >= MinimumHashKeyBytes }) return;
        throw new ArgumentException(
            $"Redaction rule '{ruleName}' uses KeyedHash but its HashKey is missing or shorter than " +
            $"{MinimumHashKeyBytes} bytes.", nameof(hashKey));
    }

    /// <summary>
    /// A processor-owned copy of a rule key. Processors call this at construction so a caller that
    /// clears or reuses its array afterwards cannot change the key (a cleared array would still pass
    /// the length check, as an all-zero key).
    /// </summary>
    public static byte[]? OwnedCopy(byte[]? hashKey) => hashKey is null ? null : (byte[])hashKey.Clone();

    // KeyedHash: HMAC-SHA256 -> first 16 bytes -> 32 lowercase hex chars,
    // prefixed with "hmac-sha256:". Same stackalloc discipline as HashValue.
    public static string KeyedHashValue(string value, byte[] hashKey) {
        var byteCount = Encoding.UTF8.GetByteCount(value);
        Span<byte> inputBytes = byteCount <= StackallocByteThreshold
            ? stackalloc byte[byteCount]
            : new byte[byteCount];
        Encoding.UTF8.GetBytes(value, inputBytes);

        Span<byte> digest = stackalloc byte[Sha256DigestSize];
        HMACSHA256.HashData(hashKey, inputBytes, digest);

        Span<char> chars = stackalloc char[KeyedHashStringLength];
        KeyedHashPrefix.AsSpan().CopyTo(chars);
        var hex = chars[KeyedHashPrefix.Length..];
        for (var i = 0; i < KeyedHashVisibleBytes; i++)
        {
            var b = digest[i];
            hex[i * 2] = HexChar(b >> 4);
            hex[i * 2 + 1] = HexChar(b & 0xF);
        }
        return new string(chars);
    }

    private const string KeyedHashPrefix = "hmac-sha256:";
    private const int KeyedHashVisibleBytes = 16;
    private const int KeyedHashStringLength = 12 + KeyedHashVisibleBytes * 2; // "hmac-sha256:" + 32 hex chars

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

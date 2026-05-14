#nullable enable

using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using MMP.Herald.Events;
using MMP.Herald.Pipeline;
using MMP.Herald.Templating;

namespace MMP.Herald.Addons.Compliance;

/// <summary>
/// Tamper-evident logging decorator that chains events with HMAC-SHA256.
/// Each event's hash includes the previous event's hash, creating a chain
/// that breaks if any entry is altered, deleted, or reordered.
///
/// Required for PCI-DSS Requirement 10 (tamper-proof audit trails) and SOC2.
///
/// The chain hash is added to the event context as "_hmacChain" before
/// forwarding, with the canonical-form version at "_hmacVersion". A separate
/// verification utility (<see cref="VerifyChain(IReadOnlyList{LogEvent}, ReadOnlySpan{byte}, string)"/>)
/// can replay the chain and detect tampering.
///
/// Canonical encoding (v1): version-prefixed, length-delimited UTF-8 fields.
/// Covers template, message, timestamp, level, category, event id, causedBy,
/// ordered properties, and sorted context (excluding the chain-hash field
/// itself). This avoids the separator-collision and missing-field problems
/// of an ad hoc "|"-joined form.
/// </summary>
public sealed class HmacChainLogger : ILogger, MMP.Herald.Pipeline.IComponentMetadata, IDisposable
{
    /// <summary>
    /// Current canonical encoding version. Bump when the encoding changes
    /// so old and new chains remain distinguishable.
    /// </summary>
    public const string CurrentVersion = "v1";

    /// <summary>
    /// Context key that carries the canonical version alongside
    /// <see cref="ComplianceContextKeys.HmacChain"/>.
    /// </summary>
    public const string VersionContextKey = "_hmacVersion";

    /// <summary>
    /// Initial "previous hash" at the start of any chain.
    /// </summary>
    public const string Genesis = "genesis";

    private readonly ILogger _inner;
    private readonly byte[] _key;
    private readonly object _sync = new();
    private string _previousHash = Genesis;
    private bool _disposed;

    public HmacChainLogger(ILogger inner, ReadOnlySpan<byte> hmacKey) {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));

        if (hmacKey.Length < 16)
        {
            throw new ArgumentException("HMAC key must be at least 16 bytes.", nameof(hmacKey));
        }

        // Defensive copy: callers mutating the source array after construction
        // must not affect future HMAC computations.
        _key = hmacKey.ToArray();
    }

    public HmacChainLogger(ILogger inner, byte[] hmacKey)
        : this(inner, new ReadOnlySpan<byte>(
            hmacKey ?? throw new ArgumentNullException(nameof(hmacKey)))) { }

    public HmacChainLogger(ILogger inner, string hmacKeyBase64)
        : this(inner, Convert.FromBase64String(hmacKeyBase64 ??
            throw new ArgumentNullException(nameof(hmacKeyBase64)))) { }

    public void Log(LogEvent logEvent) {
        ArgumentNullException.ThrowIfNull(logEvent);
        ThrowIfDisposed();

        string chainHash;

        lock (_sync)
        {
            var canonical = BuildCanonicalV1(logEvent, _previousHash);
            chainHash = ComputeHmac(canonical, _key);
            _previousHash = chainHash;
        }

        var chainedContext = new Dictionary<string, object?>(
            logEvent.Context, StringComparer.Ordinal)
        {
            [ComplianceContextKeys.HmacChain] = chainHash,
            [VersionContextKey] = CurrentVersion,
        };

        var chainedEvent = logEvent with { Context = chainedContext };
        _inner.Log(chainedEvent);
    }

    /// <summary>
    /// Verify a sequence of chained events. Returns the 0-based index of the
    /// first broken event, or -1 if the chain is intact.
    ///
    /// The events must carry the chain hash in
    /// <see cref="ComplianceContextKeys.HmacChain"/>. Canonical encoding is
    /// selected by the <see cref="VersionContextKey"/> value; v1 (current)
    /// is the only supported version today.
    /// </summary>
    public static int VerifyChain(
        IReadOnlyList<LogEvent> events,
        ReadOnlySpan<byte> key,
        string initialPrevious = Genesis) {
        ArgumentNullException.ThrowIfNull(events);

        // Local owning copy so the HMAC static calls can use a byte[].
        // Zeroed in the finally block.
        var keyCopy = key.ToArray();
        var previousHash = initialPrevious;

        try
        {
            for (var i = 0; i < events.Count; i++)
            {
                var ev = events[i];

                if (!ev.Context.TryGetValue(ComplianceContextKeys.HmacChain, out var stored)
                    || stored is not string storedHash)
                {
                    return i;
                }

                var version = CurrentVersion;
                if (ev.Context.TryGetValue(VersionContextKey, out var storedVersion)
                    && storedVersion is string vs)
                {
                    version = vs;
                }

                if (!string.Equals(version, CurrentVersion, StringComparison.Ordinal))
                {
                    // Unknown version - cannot verify. Callers with legacy
                    // chains use the obsolete overload below.
                    return i;
                }

                var canonical = BuildCanonicalV1(ev, previousHash);
                var expected = ComputeHmac(canonical, keyCopy);

                // Constant-time compare on the HMAC bytes. The stored hash
                // is not itself secret, but a timing-side-channel
                // comparison against an attacker-mutated event would leak
                // byte-position information over many probes. P3-C
                // closes that with CryptographicOperations.FixedTimeEquals.
                if (!HashesEqualFixedTime(expected, storedHash))
                {
                    return i;
                }

                previousHash = storedHash;
            }

            return -1;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(keyCopy);
        }
    }

    /// <summary>
    /// Legacy verify using the ambiguous pre-v1 canonical form
    /// (pipe-joined string, not length-prefixed, does not cover properties or
    /// context). Retained only to audit pre-v1 chain data. Do not use for new
    /// chains.
    /// </summary>
    [Obsolete("Uses the legacy ambiguous canonical form. Use VerifyChain(IReadOnlyList<LogEvent>, ReadOnlySpan<byte>, string) for v1 chains.")]
    public static int VerifyChain(
        IReadOnlyList<(string EventContent, string ChainHash)> entries,
        byte[] hmacKey) {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(hmacKey);

        var previousHash = Genesis;

        for (var i = 0; i < entries.Count; i++)
        {
            var combined = Encoding.UTF8.GetBytes(previousHash + "|" + entries[i].EventContent);
            var expected = Convert.ToHexString(HMACSHA256.HashData(hmacKey, combined)).ToLowerInvariant();

            if (!HashesEqualFixedTime(expected, entries[i].ChainHash))
            {
                return i;
            }

            previousHash = entries[i].ChainHash;
        }

        return -1;
    }

    public void Dispose() {
        if (_disposed) return;
        _disposed = true;
        CryptographicOperations.ZeroMemory(_key);
    }

    // -- Canonical encoding ----------------------------------------------------
    //
    // v1 layout (all integers are big-endian int32 byte lengths):
    //   [len][version-utf8] [len][prev-utf8]
    //   [len][timeUtc-iso-utf8] [len][level-key] [len][category]
    //   [len][messageTemplate] [len][message]
    //   [len][eventIdInt-utf8] [len][eventIdName-utf8] [len][causedBy-utf8]
    //   [propCount:int32] { [len][propName] [len][propValueToString] }*
    //   [ctxCount:int32]  { [len][key]      [len][valueToString]      }*
    //                       keys sorted ordinal; _hmacChain/_hmacVersion excluded.

    private static byte[] BuildCanonicalV1(LogEvent ev, string previousHash) {
        var writer = new ArrayBufferWriter<byte>();

        WriteField(writer, CurrentVersion);
        WriteField(writer, previousHash);
        WriteField(writer, ev.TimeUtc.ToString("O", CultureInfo.InvariantCulture));
        WriteField(writer, ev.Level.Key);
        WriteField(writer, ev.Category.Value);
        WriteField(writer, ev.MessageTemplate);
        WriteField(writer, ev.Message);
        WriteField(writer, ev.EventId?.Id.ToString(CultureInfo.InvariantCulture) ?? string.Empty);
        WriteField(writer, ev.EventId?.Name ?? string.Empty);
        WriteField(writer, ev.CausedBy ?? string.Empty);
        WriteProperties(writer, ev.Properties);
        WriteContext(writer, ev.Context);

        return writer.WrittenMemory.ToArray();
    }

    private static void WriteField(IBufferWriter<byte> writer, string value) {
        var bytes = Encoding.UTF8.GetBytes(value);
        WriteInt32BigEndian(writer, bytes.Length);
        writer.Write(bytes);
    }

    private static void WriteInt32BigEndian(IBufferWriter<byte> writer, int value) {
        var span = writer.GetSpan(4);
        BinaryPrimitives.WriteInt32BigEndian(span, value);
        writer.Advance(4);
    }

    private static void WriteProperties(IBufferWriter<byte> writer, IReadOnlyList<LogProperty> properties) {
        WriteInt32BigEndian(writer, properties.Count);

        // Properties carry ordering semantics (argument order matches the template).
        // Preserve that order rather than sort, so replay matches original intent.
        foreach (var property in properties)
        {
            WriteField(writer, property.Name);
            WriteField(writer, FormatValue(property.Value));
        }
    }

    private static void WriteContext(IBufferWriter<byte> writer, IReadOnlyDictionary<string, object?> context) {
        // Context is a dictionary and carries no inherent order, so sort by
        // key for determinism. Exclude the chain-integrity fields themselves
        // (the hash cannot be part of its own input, and the version appears
        // in the canonical payload already).
        var sortedKeys = new List<string>(context.Count);
        foreach (var key in context.Keys)
        {
            if (string.Equals(key, ComplianceContextKeys.HmacChain, StringComparison.Ordinal))
            {
                continue;
            }
            if (string.Equals(key, VersionContextKey, StringComparison.Ordinal))
            {
                continue;
            }
            sortedKeys.Add(key);
        }
        sortedKeys.Sort(StringComparer.Ordinal);

        WriteInt32BigEndian(writer, sortedKeys.Count);
        foreach (var key in sortedKeys)
        {
            WriteField(writer, key);
            WriteField(writer, FormatValue(context[key]));
        }
    }

    private static string FormatValue(object? value) {
        return value switch
        {
            null => string.Empty,
            string s => s,
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty,
        };
    }

    private static string ComputeHmac(byte[] canonical, byte[] key) {
        var hashBytes = HMACSHA256.HashData(key, canonical);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    /// <summary>
    /// Constant-time comparison of two hex-encoded hashes. Both inputs
    /// must be the same length; different-length inputs short-circuit
    /// to false without leaking timing (the length check is public
    /// information). For equal-length inputs we decode to bytes and
    /// call <see cref="CryptographicOperations.FixedTimeEquals"/> —
    /// the comparison runs in constant time regardless of where the
    /// first mismatching byte is.
    /// </summary>
    private static bool HashesEqualFixedTime(string expected, string stored)
    {
        if (expected.Length != stored.Length) return false;
        // Convert.FromHexString tolerates lowercase hex; both sides
        // produce lowercase digests so allocation is exactly one byte
        // array per side.
        try
        {
            var expectedBytes = Convert.FromHexString(expected);
            var storedBytes = Convert.FromHexString(stored);
            return CryptographicOperations.FixedTimeEquals(expectedBytes, storedBytes);
        }
        catch (FormatException)
        {
            // A malformed stored hash (non-hex characters) is not a
            // timing leak — just a failed match. Old chains with
            // corrupt context survive as "not equal" the same way a
            // genuine mismatch does.
            return false;
        }
    }

    private void ThrowIfDisposed() {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(HmacChainLogger));
        }
    }

    // -- IComponentMetadata --
    string Pipeline.IComponentMetadata.ComponentName => "hmacChain";
    string Pipeline.IComponentMetadata.DisplayName => "HMAC Chain";
    string Pipeline.IComponentMetadata.Description => "Tamper-evident logging via HMAC-SHA256 chaining for PCI-DSS/SOC2.";
    string Pipeline.IComponentMetadata.Help => "Each event's hash includes the previous hash. Canonical encoding is v1 length-delimited UTF-8 covering template, message, timestamp, level, category, event id, causedBy, properties, and sorted context. Use VerifyChain(IReadOnlyList<LogEvent>, ...) to audit.";
    Pipeline.VendorInfo Pipeline.IComponentMetadata.Vendor => Pipeline.VendorInfo.MMP;
    System.Collections.Generic.IReadOnlyList<Routing.SinkConfigField> Pipeline.IComponentMetadata.ConfigurationSchema => [];
}

#nullable enable

using System;
using MMP.Herald.Pipeline.Kernel;

namespace MMP.Herald.Routing.Map;

/// <summary>
/// Extracts the routing key for an event by <b>slicing an existing buffer
/// string</b> — never by formatting. Returns an empty span when the event
/// carries no usable key; the router sends such events to its default
/// destination.
///
/// <para>
/// Slicing (not formatting) is the load-bearing constraint. A key produced by
/// <c>string.Format</c> or interpolation allocates per event and defeats the
/// 0-alloc contract. The canonical selector reads a string property straight
/// off the buffer:
/// </para>
///
/// <code>
/// static (in LogEventBuffer b) =>
///     b.TryGetStringSpan("TenantId", out var k) ? k : ReadOnlySpan&lt;char&gt;.Empty;
/// </code>
///
/// <para>
/// <b>Producer-thread-eager fields only.</b> The selector must read fields the
/// producer thread already populated — the buffer's own properties, level,
/// category. It must not read ambient context (<c>AsyncLocal</c>,
/// <c>HttpContext.Current</c>): on an async drain the routing decision can run
/// on a different thread, and an ambient read there returns the wrong (or
/// another tenant's) value — the same scope-capture hazard the ratified
/// lazy-resolution PII fix addresses. The <c>HERALD050</c> analyzer flags a
/// selector that reads ambient context.
/// </para>
/// </summary>
public delegate ReadOnlySpan<char> LogEventBufferKeySelector(in LogEventBuffer buffer);

/// <summary>
/// Validates routing keys before they can become a downstream sink identity
/// (e.g. a filename). A data-driven key — a tenant id or correlation id pulled
/// from an inbound request — is untrusted input. Without validation a key like
/// <c>"../../secret"</c> is a path-traversal vector the moment a factory does
/// <c>wt.File($"logs/{key}.log")</c>.
/// </summary>
public static class RouteKey
{
    /// <summary>Conservative default cap on key length.</summary>
    public const int DefaultMaxKeyLength = 128;

    /// <summary>
    /// True when the key is safe to use as a sink identity: non-empty, within
    /// the length cap, and free of path separators and control characters.
    /// The router routes an invalid key to its default destination (or drops
    /// it) rather than handing it to the factory.
    /// </summary>
    public static bool IsValid(ReadOnlySpan<char> key, int maxKeyLength = DefaultMaxKeyLength)
    {
        if (key.IsEmpty || key.Length > maxKeyLength) return false;

        foreach (var c in key)
        {
            // Reject anything that could escape a path segment or break a
            // filename: separators, drive/scheme punctuation, control chars,
            // and the dot-runs that build a traversal. The whitelist intent is
            // "an identifier-shaped token", not "an arbitrary string".
            if (c is '/' or '\\' or ':' or '*' or '?' or '"' or '<' or '>' or '|')
                return false;
            if (char.IsControl(c)) return false;
        }

        // A key that is only dots ("." / "..") is a traversal even without a
        // separator once it lands in a path segment.
        return !IsAllDots(key);
    }

    private static bool IsAllDots(ReadOnlySpan<char> key)
    {
        foreach (var c in key)
        {
            if (c != '.') return false;
        }
        return true;
    }
}

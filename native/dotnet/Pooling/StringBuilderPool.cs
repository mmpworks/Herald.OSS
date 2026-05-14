#nullable enable

using System;
using System.Text;

namespace MMP.Herald.Pooling;

/// <summary>
/// Thread-local StringBuilder pool for zero-contention reuse.
/// Uses ThreadStaticPool&lt;T&gt; for the rent/return logic.
///
/// The pool retains one StringBuilder per thread (up to 4096 capacity).
/// Larger builders are discarded to prevent unbounded memory retention.
/// </summary>
internal static class StringBuilderPool
{
    private const int MaxRetainedCapacity = 4096;

    private static readonly ThreadStaticPool<StringBuilder> Pool = new(
        factory: static () => new StringBuilder(256),
        clear: static sb => sb.Clear(),
        shouldRetain: static sb => sb.Capacity <= MaxRetainedCapacity);

    [ThreadStatic]
    private static StringBuilder? t_cached;

    public static StringBuilder Rent() =>
        Pool.Rent(ref t_cached);

    public static string ReturnAndGetString(StringBuilder sb) {
        var result = sb.ToString();
        Pool.Return(ref t_cached, sb);
        return result;
    }

    /// <summary>
    /// Return a builder without extracting its string content.
    /// Use when the content was consumed via other means.
    /// </summary>
    public static void Return(StringBuilder sb) =>
        Pool.Return(ref t_cached, sb);
}
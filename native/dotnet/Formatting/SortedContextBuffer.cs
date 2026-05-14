#nullable enable

using System;
using System.Buffers;
using System.Collections.Generic;

namespace MMP.Herald.Formatting;

/// <summary>
/// Rents a pooled array of context entries sorted by key (Ordinal).
/// Used by every formatter that emits log context so we do not pay a LINQ
/// OrderBy enumerator + OrderedEnumerable lambda capture per formatted event.
///
/// Usage: <c>using var sorted = SortedContextBuffer.Create(context);</c>
/// then iterate via <see cref="AsSpan"/> or the indexer.
///
/// Zero-allocation on the hot path: the backing array is rented from
/// <see cref="ArrayPool{T}.Shared"/> and returned on dispose. The empty-context
/// case hands back a zero-length singleton array with nothing to release.
/// </summary>
internal readonly struct SortedContextBuffer : IDisposable
{
    private static readonly KeyValuePair<string, object?>[] EmptyArray =
        Array.Empty<KeyValuePair<string, object?>>();

    private readonly KeyValuePair<string, object?>[] _rented;
    private readonly int _count;

    private SortedContextBuffer(KeyValuePair<string, object?>[] rented, int count)
    {
        _rented = rented;
        _count = count;
    }

    public int Count => _count;

    public KeyValuePair<string, object?> this[int index] => _rented[index];

    public ReadOnlySpan<KeyValuePair<string, object?>> AsSpan() =>
        new(_rented, 0, _count);

    public static SortedContextBuffer Create(IReadOnlyDictionary<string, object?> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var count = context.Count;
        if (count == 0)
        {
            return new SortedContextBuffer(EmptyArray, 0);
        }

        var rented = ArrayPool<KeyValuePair<string, object?>>.Shared.Rent(count);

        var index = 0;
        foreach (var pair in context)
        {
            rented[index] = pair;
            index += 1;
        }

        Array.Sort(rented, 0, count, PairKeyComparer.Instance);
        return new SortedContextBuffer(rented, count);
    }

    public void Dispose()
    {
        if (_rented.Length == 0)
        {
            return;
        }

        // Clear references so the pool does not pin the context values after
        // this event is done. The clear is bounded by the populated slots.
        Array.Clear(_rented, 0, _count);
        ArrayPool<KeyValuePair<string, object?>>.Shared.Return(_rented);
    }

    private sealed class PairKeyComparer : IComparer<KeyValuePair<string, object?>>
    {
        public static readonly PairKeyComparer Instance = new();

        public int Compare(KeyValuePair<string, object?> x, KeyValuePair<string, object?> y) =>
            string.CompareOrdinal(x.Key, y.Key);
    }
}

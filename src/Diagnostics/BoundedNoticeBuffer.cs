#nullable enable

using System;
using System.Collections.Generic;

namespace MMP.Herald.Diagnostics;

/// <summary>
/// Thread-safe bounded FIFO buffer that retains the most recent
/// <see cref="Capacity"/> entries. New entries past the capacity
/// evict the oldest in O(1). The dropped count is exposed so a
/// diagnostic surface can tell a viewer "you're seeing 64 of 14,210
/// notices" rather than silently lying.
///
/// <para>
/// <b>Why this exists as its own type.</b> Two Herald.OSS diagnostic
/// surfaces (<see cref="HeraldRuntimeMessagesInstance"/> for runtime
/// notices and <see cref="MMP.Herald.Failures.DiagnosticLogFailureSink"/>
/// for failure records) need the same buffer mechanics: bounded
/// queue + snapshot + clear + thread-safe write. Composing them on
/// top of one buffer keeps the queue + lock + eviction logic in one
/// place and lets each consumer add its own domain-specific surface
/// (file mirror, GenSource stamping) without re-implementing the
/// mechanics.
/// </para>
///
/// <para>
/// <b>Threading.</b> A single internal lock serialises every
/// mutation and every snapshot. Snapshots return a fresh array so
/// callers can iterate without holding the lock; the cost is one
/// allocation per <see cref="Snapshot"/> call, acceptable for
/// diagnostic surfaces that poll at human cadence (per-second or
/// slower).
/// </para>
/// </summary>
/// <typeparam name="T">The buffered item type.</typeparam>
public sealed class BoundedNoticeBuffer<T>
{
    private readonly object _sync = new();
    private readonly int _capacity;
    private readonly Queue<T> _items;
    private long _droppedCount;

    public BoundedNoticeBuffer(int capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(capacity), capacity,
                "Buffer capacity must be greater than zero.");
        }
        _capacity = capacity;
        _items = new Queue<T>(capacity);
    }

    /// <summary>Maximum entries retained before eviction begins.</summary>
    public int Capacity => _capacity;

    /// <summary>
    /// Number of entries dropped because the buffer was full at
    /// enqueue time. Monotonic for the buffer's lifetime; reset by
    /// <see cref="Clear"/>.
    /// </summary>
    public long DroppedCount
    {
        get { lock (_sync) return _droppedCount; }
    }

    /// <summary>
    /// Append <paramref name="item"/>. When the buffer is at
    /// capacity, the oldest entry is evicted and
    /// <see cref="DroppedCount"/> increments.
    /// </summary>
    public void Enqueue(T item)
    {
        lock (_sync)
        {
            if (_items.Count >= _capacity)
            {
                _items.Dequeue();
                _droppedCount++;
            }
            _items.Enqueue(item);
        }
    }

    /// <summary>
    /// Oldest-first snapshot. Returns a fresh array so callers can
    /// iterate without holding the lock and so the buffer can keep
    /// mutating without affecting the returned view.
    /// </summary>
    public IReadOnlyList<T> Snapshot()
    {
        lock (_sync)
        {
            return _items.ToArray();
        }
    }

    /// <summary>
    /// Empty the buffer and reset <see cref="DroppedCount"/>. Tests
    /// call this between scenarios to isolate against process-wide
    /// state in the static facade.
    /// </summary>
    public void Clear()
    {
        lock (_sync)
        {
            _items.Clear();
            _droppedCount = 0;
        }
    }
}

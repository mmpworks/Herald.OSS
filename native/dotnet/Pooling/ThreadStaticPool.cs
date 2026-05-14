#nullable enable

using System;

namespace MMP.Herald.Pooling;

/// <summary>
/// Generic thread-local object pool with zero contention.
/// Each thread retains at most one instance. Objects are cleared on rent
/// and returned after use. Oversized objects are discarded.
///
/// Since C# [ThreadStatic] only works on static fields, each pool instance
/// operates on a caller-provided ref slot. The generic eliminates the duplicated
/// rent/return LOGIC while callers own the [ThreadStatic] STORAGE.
///
/// Thread safety: [ThreadStatic] ensures zero contention between threads.
/// Each thread has its own cached instance. No locking required.
/// </summary>
internal sealed class ThreadStaticPool<T> where T : class
{
    private readonly Func<T> _factory;
    private readonly Action<T> _clear;
    private readonly Func<T, bool> _shouldRetain;

    public ThreadStaticPool(
        Func<T> factory,
        Action<T> clear,
        Func<T, bool> shouldRetain) {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _clear = clear ?? throw new ArgumentNullException(nameof(clear));
        _shouldRetain = shouldRetain ?? throw new ArgumentNullException(nameof(shouldRetain));
    }

    /// <summary>
    /// Get an item from the provided thread-local slot, or create a new one.
    /// The ref parameter must be a [ThreadStatic] field owned by the caller.
    /// </summary>
    public T Rent(ref T? slot) {
        var item = slot;
        if (item is not null)
        {
            slot = null;
            _clear(item);
            return item;
        }

        return _factory();
    }

    /// <summary>
    /// Return an item to the provided thread-local slot.
    /// If the item is too large, it is discarded.
    /// </summary>
    public void Return(ref T? slot, T item) {
        if (_shouldRetain(item))
        {
            slot = item;
        }
    }
}
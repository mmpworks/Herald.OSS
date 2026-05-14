#nullable enable

using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace MMP.Herald.Pipeline.Kernel;

/// <summary>
/// One-slot ThreadStatic pool for the <see cref="List{T}"/> that every
/// interpolated <c>$"..."</c> accept path rents to carry its properties
/// from handler to sink.
///
/// <para>
/// The handler rents the list in its ctor; the owning <c>*I</c> method
/// calls <c>Drain</c> and hands the list to
/// <c>DispatchFromHandler</c>; after dispatch the list is unreferenced.
/// <c>DispatchFromHandler</c> returns it here so the next accepted call
/// on the same thread skips a 40 B List allocation + a ~24 B backing
/// array allocation.
/// </para>
///
/// <para>
/// One slot per thread (not a stack) because the interpolated log path
/// is not re-entrant in practice — a log call doesn't trigger another
/// log call from the same thread before the first returns. If it ever
/// does nest, the second Rent allocates fresh; that fresh list takes
/// the slot on Return and the first becomes garbage. Graceful degrade,
/// no correctness risk.
/// </para>
///
/// <para>
/// <b>Safety.</b> The rented list never escapes the dispatch call —
/// <c>IKernelSink.Log(in LogEventBuffer)</c> is synchronous and the
/// buffer's property span is dead by the time the sink returns.
/// Sinks that need to persist properties copy them out of the span
/// into their own storage. The chain path copies to a fresh
/// <c>LogProperty[]</c> before enqueuing. Either way, the list is
/// free to recycle when <c>DispatchFromHandler</c> returns.
/// </para>
/// </summary>
internal static class LogPropertyCompactListPool
{
    [System.ThreadStatic]
    private static List<LogPropertyCompact>? _cached;

    /// <summary>
    /// Rent a list with at least <paramref name="capacity"/> slots. Reuses
    /// the thread-local cached instance when available and big enough;
    /// allocates fresh otherwise.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static List<LogPropertyCompact> Rent(int capacity)
    {
        var cached = _cached;
        if (cached is not null && cached.Capacity >= capacity)
        {
            _cached = null;
            return cached;
        }
        return new List<LogPropertyCompact>(capacity);
    }

    /// <summary>
    /// Clear and return the list to the thread-local slot. Always clears
    /// before storing so the pool doesn't hold onto user objects
    /// (property values, strings) past the log call that produced them.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Return(List<LogPropertyCompact> list)
    {
        list.Clear();
        _cached = list;
    }
}

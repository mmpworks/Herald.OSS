#nullable enable

using System.Threading;

namespace MMP.Herald.Pipeline.Kernel;

/// <summary>
/// Shared mutable carrier for a <see cref="LogKernel"/> delegate. The parent
/// <see cref="StructuredLogger"/> and every child produced by
/// <see cref="StructuredLogger.WithContext"/> reference the same holder
/// instance, so a hot-reload <see cref="StructuredLogger.SwapKernel"/> call
/// on the parent is observed by the children on their next dispatch.
///
/// <para>
/// Without this indirection, a child captured the parent's kernel by value at
/// construction time and silently dispatched through the orphaned old kernel
/// after a reload — long-running scope-bearing loggers (e.g. per-request
/// ASP.NET loggers) kept routing events to retired sinks. The holder closes
/// that orphan window.
/// </para>
///
/// <para>
/// Concurrency: <see cref="Current"/> is a single <see cref="Volatile.Read{T}"/>
/// on the hot path; <see cref="Swap"/> publishes through
/// <see cref="Volatile.Write{T}"/>. The kernel reference is itself immutable
/// once installed, so a reader either sees the pre-swap delegate or the
/// post-swap delegate — never a torn state.
/// </para>
/// </summary>
internal sealed class KernelHolder
{
    private LogKernel? _current;

    public KernelHolder(LogKernel? initial)
    {
        _current = initial;
    }

    /// <summary>
    /// The currently installed kernel, or <c>null</c> when no kernel is
    /// bound. Volatile-read so a concurrent <see cref="Swap"/> is observed
    /// consistently by in-flight dispatch calls.
    /// </summary>
    public LogKernel? Current => Volatile.Read(ref _current);

    /// <summary>
    /// Atomically replace the current kernel. Pass <c>null</c> to force
    /// every subsequent dispatch through the decorator-chain fallback.
    /// </summary>
    public void Swap(LogKernel? next) => Volatile.Write(ref _current, next);
}

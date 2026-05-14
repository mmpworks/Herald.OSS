#nullable enable

using System.Runtime.CompilerServices;

namespace MMP.Herald.Pipeline.Kernel;

/// <summary>
/// Drop-decision contract for kernel-aware companions that need to
/// short-circuit dispatch BEFORE any <see cref="Events.LogEventBuffer"/>
/// allocation. Captures the cheapest-rejection pattern proven by
/// <see cref="FastPathSampler"/> (1-in-N counter sampling drops at
/// ~13 ns / 0 B because nothing further runs once
/// <see cref="ShouldDrop"/> returns true).
///
/// <para>
/// <b>Why this exists.</b> Future companions that want drop semantics —
/// e.g., a per-event quota guard, a circuit-breaker that drops while a
/// downstream sink is unhealthy — should inherit the same shape. Capturing
/// it as a contract instead of re-inventing the field-and-volatile-read
/// dance per class keeps the kernel-resident dispatch surface small.
/// </para>
///
/// <para>
/// <b>Composition.</b> Multiple droppers compose through
/// <see cref="CompositeFastPathDropper"/>, which iterates in registration
/// order and short-circuits on the first <c>true</c>. That preserves the
/// "every dropper that says yes saves every later dropper's work"
/// guarantee callers rely on.
/// </para>
///
/// <para>
/// <b>Threading.</b> Implementations must be safe for concurrent
/// <see cref="ShouldDrop"/> calls. The dispatch site does not serialise.
/// State that mutates across calls (e.g. a counter) belongs behind
/// <see cref="System.Threading.Interlocked"/> or an equivalent lock-free
/// primitive.
/// </para>
/// </summary>
public interface IFastPathDropper
{
    /// <summary>
    /// Returns <c>true</c> when the next event should be dropped without
    /// reaching the kernel. Implementations should keep this method
    /// allocation-free and inline-friendly — it runs on every accepted-
    /// by-level call to a kernel-eligible logger.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    bool ShouldDrop();
}

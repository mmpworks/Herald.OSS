#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace MMP.Herald.Pipeline.Kernel;

/// <summary>
/// Composes multiple <see cref="IFastPathDropper"/> instances behind a
/// single dropper. Iteration order is the order droppers were passed to
/// the constructor; <see cref="ShouldDrop"/> returns <c>true</c> on the
/// first dropper that says drop, and skips the rest. That short-circuit
/// is what preserves the cheapest-rejection invariant: if dropper #1 has
/// already decided, dropper #2's counter never moves.
///
/// <para>
/// <b>Why this exists.</b> The kernel-aware dispatch site holds a single
/// dropper slot per logger. When a deployment configures more than one
/// drop semantic — e.g. a sampler plus a quota guard — the install hook
/// wraps them into one composite and stores that. The dispatch site
/// stays exactly the same shape (one volatile read, one virtual call);
/// the composite is where the loop lives.
/// </para>
/// </summary>
public sealed class CompositeFastPathDropper : IFastPathDropper
{
    private readonly IFastPathDropper[] _droppers;

    /// <summary>
    /// Build a composite over <paramref name="droppers"/>. Empty arrays
    /// are not accepted — if you have nothing to compose, install
    /// nothing instead of installing an inert composite.
    /// </summary>
    public CompositeFastPathDropper(IReadOnlyList<IFastPathDropper> droppers)
    {
        ArgumentNullException.ThrowIfNull(droppers);
        if (droppers.Count == 0)
        {
            throw new ArgumentException(
                "At least one dropper must be supplied. Install null instead of an empty composite.",
                nameof(droppers));
        }

        // Defensive copy. Caller's list shape (List<T>, array, etc.) is
        // not our concern; we want a fixed-shape backing array so the
        // hot-path foreach has no virtual GetEnumerator dispatch.
        var copy = new IFastPathDropper[droppers.Count];
        for (var i = 0; i < droppers.Count; i++)
        {
            var d = droppers[i] ?? throw new ArgumentException(
                $"Dropper at index {i} is null.", nameof(droppers));
            copy[i] = d;
        }
        _droppers = copy;
    }

    /// <summary>
    /// The droppers this composite iterates, in installation order.
    /// Exposed for diagnostics and tests; not part of the hot path.
    /// </summary>
    public IReadOnlyList<IFastPathDropper> Droppers => _droppers;

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ShouldDrop()
    {
        // Plain index loop over a fixed array — no enumerator allocation,
        // no LINQ, predictable bounds-check elision.
        var droppers = _droppers;
        for (var i = 0; i < droppers.Length; i++)
        {
            if (droppers[i].ShouldDrop()) return true;
        }
        return false;
    }
}

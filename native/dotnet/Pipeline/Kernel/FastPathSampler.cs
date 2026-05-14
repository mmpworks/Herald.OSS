#nullable enable

using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace MMP.Herald.Pipeline.Kernel;

/// <summary>
/// Kernel-aware sampler. Decides keep-or-drop on the call's level and
/// category alone — no <see cref="LogEventBuffer"/> construction, no
/// property-bag walk, no allocation. Drops happen before any kernel
/// dispatch work, so a sampled-out event pays a single
/// <see cref="Interlocked.Increment(ref long)"/> and one modulo.
///
/// <para>
/// <b>Why this exists.</b> Today's
/// <see cref="Filters.ILogFilter"/>-based sampling reads from a
/// fully-materialised <see cref="Events.LogEvent"/> — and the presence
/// of a sampling filter on the policy disqualifies the kernel fast
/// path entirely (see
/// <see cref="KernelEligibility.DescribeRejection"/>). A pipeline
/// whose only sampling need is "1 in N" does not need the heap
/// LogEvent or the chain. This sampler runs at the dispatch boundary,
/// stays on the kernel, and short-circuits the call before any
/// allocation work happens. Same recipe as
/// <see cref="FastPathRedactor"/>.
/// </para>
///
/// <para>
/// <b>Scope.</b> Pure 1-in-N counter sampling. Per-category scoping,
/// rate-limit ("max N per second") shapes, and consistent-hash sampling
/// stay on <see cref="Filters.SamplingFilter"/> /
/// <see cref="Filters.ConsistentSamplingFilter"/> /
/// <see cref="Filters.CompositeSamplingFilter"/> — those need state
/// or shape the kernel-aware path does not currently model. The fast
/// path covers the dominant production case ("keep 1 in 10 of
/// everything"); the long tail uses the heavier filters.
/// </para>
/// </summary>
public sealed class FastPathSampler : IFastPathDropper
{
    private readonly int _sampleRate;
    private long _counter;

    /// <summary>
    /// Build a 1-in-N sampler. <paramref name="sampleRate"/> = 1 keeps
    /// every event (the sampler is configured but inert); larger values
    /// drop the corresponding fraction.
    /// </summary>
    public FastPathSampler(int sampleRate)
    {
        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sampleRate), sampleRate,
                "Sample rate must be greater than zero.");
        }
        _sampleRate = sampleRate;
    }

    /// <summary>The configured 1-in-N rate.</summary>
    public int SampleRate => _sampleRate;

    /// <summary>Total <see cref="ShouldEmit"/> calls observed.</summary>
    public long EventsEvaluated => Interlocked.Read(ref _counter);

    /// <summary>
    /// Decide whether to emit the next event. Inlined so the JIT folds
    /// the call into the dispatch frame. Lock-free: the only contended
    /// state is the counter, updated via
    /// <see cref="Interlocked.Increment(ref long)"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ShouldEmit()
    {
        // Configured-but-inert short circuit. Rate=1 means "keep every
        // event" — the sampler is wired up but functionally a no-op.
        // Skipping the Interlocked.Increment in this case keeps the
        // hot path predictable for callers that install the sampler
        // by default and only ever raise the rate later.
        if (_sampleRate == 1) return true;

        // Use unsigned modulo so a wrap past long.MaxValue does not flip
        // the sign and skew the sample. Mirrors the legacy
        // SamplingFilter implementation.
        var count = (ulong)Interlocked.Increment(ref _counter);
        return count % (ulong)_sampleRate == 0;
    }

    /// <summary>
    /// <see cref="IFastPathDropper"/> adapter — returns true when this
    /// event should be dropped (the inverse of <see cref="ShouldEmit"/>).
    /// Lets the sampler compose with other droppers through
    /// <see cref="CompositeFastPathDropper"/>; the existing single-sampler
    /// dispatch path keeps calling <see cref="ShouldEmit"/> directly so no
    /// virtual call lands on the byte-counted hot path.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ShouldDrop() => !ShouldEmit();
}

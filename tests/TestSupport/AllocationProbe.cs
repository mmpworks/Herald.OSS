#nullable enable

using System;
using System.Runtime.CompilerServices;

namespace MMP.Herald.OSS.Tests.TestSupport;

/// <summary>
/// Measures steady-state per-iteration managed heap allocation for an action,
/// using the per-thread allocation counter
/// (<see cref="GC.GetAllocatedBytesForCurrentThread"/>).
///
/// <para>
/// The probe exists to pin Herald's zero-allocation contract so it cannot
/// silently regress. A benchmark tells you the number once; a test fails the
/// build the day someone reintroduces a box on the hot path.
/// </para>
///
/// <para><b>How it avoids flake.</b> Three things make the measurement
/// deterministic enough to assert exact zero (or an exact box count):
/// </para>
/// <list type="number">
///   <item>
///     <b>Warmup.</b> The action runs a warmup batch first so the JIT compiles
///     it, tiered compilation settles, and any one-time caches (template name
///     resolution, etc.) are already populated. First-call allocation is never
///     part of the steady-state number.
///   </item>
///   <item>
///     <b>Per-thread counter, single thread.</b>
///     <see cref="GC.GetAllocatedBytesForCurrentThread"/> only counts
///     allocations on the calling thread, so background/test-host noise on
///     other threads does not leak in. The measured loop never awaits and
///     never hops threads.
///   </item>
///   <item>
///     <b>Amortized over a large loop.</b> The delta is divided by the
///     iteration count. Any single stray allocation the runtime makes during
///     the window (it generally makes none on this thread for these paths)
///     averages toward zero, and the result is reported as floor-divided
///     bytes-per-iteration so a clean path reads exactly 0.
///   </item>
/// </list>
///
/// <para>
/// The counter itself does not allocate, so reading it inside the window does
/// not perturb the measurement.
/// </para>
/// </summary>
public static class AllocationProbe
{
    /// <summary>Default warmup iterations: enough to clear tiered JIT.</summary>
    public const int DefaultWarmupIterations = 2_000;

    /// <summary>Default measured iterations: large enough to amortize noise.</summary>
    public const int DefaultMeasuredIterations = 100_000;

    /// <summary>
    /// Runs <paramref name="action"/> through a warmup phase, then measures the
    /// average managed bytes allocated per call across a measured loop.
    /// Returns floor-divided bytes-per-iteration: a path that allocates nothing
    /// in steady state returns 0.
    /// </summary>
    public static long BytesPerIteration(
        Action action,
        int warmupIterations = DefaultWarmupIterations,
        int measuredIterations = DefaultMeasuredIterations)
    {
        if (action is null) throw new ArgumentNullException(nameof(action));
        if (measuredIterations <= 0)
            throw new ArgumentOutOfRangeException(nameof(measuredIterations));

        // Warmup: JIT + tiered compilation + one-time caches (template name
        // resolution lives behind TryGetCachedNames, so the first call per
        // template populates it — keep that out of the measured window).
        for (var i = 0; i < warmupIterations; i++)
            action();

        // Settle the heap so the counter delta reflects only what the loop
        // allocates, not collection bookkeeping mid-window.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < measuredIterations; i++)
            action();
        var after = GC.GetAllocatedBytesForCurrentThread();

        var total = after - before;
        if (total < 0) total = 0; // counter is monotonic per thread; guard anyway

        // Floor-divide: a clean zero-alloc path reads exactly 0. A path that
        // boxes one value per call reads the box size (e.g. 24 on x64).
        return total / measuredIterations;
    }

    /// <summary>
    /// Total managed bytes allocated by <paramref name="action"/> across the
    /// measured loop, after warmup. Use when the caller wants the raw window
    /// total (e.g. to compare two shapes and isolate a single box).
    /// </summary>
    public static long TotalBytes(
        Action action,
        int warmupIterations = DefaultWarmupIterations,
        int measuredIterations = DefaultMeasuredIterations)
    {
        if (action is null) throw new ArgumentNullException(nameof(action));
        if (measuredIterations <= 0)
            throw new ArgumentOutOfRangeException(nameof(measuredIterations));

        for (var i = 0; i < warmupIterations; i++)
            action();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < measuredIterations; i++)
            action();
        var after = GC.GetAllocatedBytesForCurrentThread();

        var total = after - before;
        return total < 0 ? 0 : total;
    }

    // Keep the JIT from optimizing a call away entirely. Not currently needed
    // (the logger has observable side effects through the sink), but available
    // if a future probe targets a pure path.
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Consume<T>(T value)
    {
        _ = value;
    }
}

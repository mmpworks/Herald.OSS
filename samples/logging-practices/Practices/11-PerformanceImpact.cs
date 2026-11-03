using System.Diagnostics;
using MMP.Herald.Events;
using MMP.Herald.Quick;

namespace LoggingPractices.Practices;

/// <summary>
/// Practice 11. Know what logging costs. This measures the producer side of one
/// typed call on this machine, in this run. The published per-call and soak
/// numbers, with the harness that produced them, live in docs/benchmarks/.
/// </summary>
public static class PerformanceImpact
{
    private const int Calls = 200_000;

    public static async Task RunAsync()
    {
        await using var result = QuickLogBuilder.Create()
            .WithoutConsoleSink()
            .WithNullSink()
            .WithMinimumLevel("info")
            .BuildAndCommit();
        var logger = result.Logger;

        // Warm up so the JIT and the pipeline caches are ready.
        for (var i = 0; i < 10_000; i++) logger.Information(LogCategory.App, "Tick {N} at {Value}", i, 1.5);

        var bytesBefore = GC.GetAllocatedBytesForCurrentThread();
        var clock = Stopwatch.StartNew();
        for (var i = 0; i < Calls; i++) logger.Information(LogCategory.App, "Tick {N} at {Value}", i, 1.5);
        clock.Stop();
        var bytes = GC.GetAllocatedBytesForCurrentThread() - bytesBefore;

        Console.WriteLine($"  {Calls:N0} calls: {clock.Elapsed.TotalNanoseconds / Calls:F1} ns per call, {(double)bytes / Calls:F2} bytes per call on this thread");
        Console.WriteLine("  (one run on this machine; build with -c Release for a fair number)");
    }
}

using MMP.Herald.Events;
using MMP.Herald.Filters;
using MMP.Herald.Levels;
using MMP.Herald.Quick;

namespace LoggingPractices.Practices;

/// <summary>
/// Practice 5. Keep every error, sample the success traffic.
/// Three samplers: AdaptiveSamplingFilter (builder: WithAdaptiveSampling),
/// PriorityWindowSamplingFilter (a per-level floor per time window), and
/// FastPathSampler (builder: WithFastSampling, on the kernel fast path).
/// </summary>
public static class LogSampling
{
    public static async Task RunAsync()
    {
        // Adaptive: keep 1 in 10 info events, keep every error.
        var adaptive = new CaptureLogger(print: false);
        await using (var result = QuickLogBuilder.Create()
            .WithoutConsoleSink()
            .WithAdaptiveSampling(normalSampleRate: 10, errorThreshold: 50)
            .WithBridge(adaptive)
            .BuildAndCommit())
        {
            Emit(result, info: 100, errors: 5);
        }
        Report("AdaptiveSamplingFilter", adaptive);

        // Priority window: up to 1000 errors per minute always pass.
        // Every other level is sampled 1 in 20.
        var window = new PriorityWindowSamplingFilter(
            minimumPerLevel: new Dictionary<LogLevel, int> { [KnownLogLevels.Error] = 1000 },
            windowDuration: TimeSpan.FromMinutes(1),
            defaultSampleRate: 20);
        var windowed = new CaptureLogger(print: false);
        await using (var result = QuickLogBuilder.Create()
            .WithoutConsoleSink()
            .WithBridge(windowed, window.Allow)
            .BuildAndCommit())
        {
            Emit(result, info: 100, errors: 5);
        }
        Report("PriorityWindowSamplingFilter", windowed);

        // Kernel fast path: 1 in 10, decided before the event is built.
        // The console sink stays on the kernel path, so 30 calls print 3 lines.
        Console.WriteLine("  FastPathSampler, 30 calls at 1 in 10:");
        await using (var result = QuickLogBuilder.Create()
            .WithConsoleSink()
            .WithFastSampling(10)
            .BuildAndCommit())
        {
            for (var i = 0; i < 30; i++) result.Logger.Information(LogCategory.App, "Heartbeat {N}", i);
        }
    }

    private static void Emit(QuickLogResult result, int info, int errors)
    {
        for (var i = 0; i < info; i++) result.Logger.Information(LogCategory.App, "Request {N} ok", i);
        for (var i = 0; i < errors; i++) result.Logger.Error(LogCategory.App, "Request {N} failed", i);
    }

    private static void Report(string name, CaptureLogger capture)
    {
        var kept = capture.Events.GroupBy(e => e.Level.Key).Select(g => $"{g.Key} {g.Count()}");
        Console.WriteLine($"  {name,-30} kept: {string.Join(", ", kept)}");
    }
}

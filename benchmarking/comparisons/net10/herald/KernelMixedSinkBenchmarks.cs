#nullable enable

using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using MMP.Herald.Events;
using MMP.Herald.Quick;

namespace MMP.Herald.OSS.Benchmarks.Comparisons.HeraldRow;

/// <summary>
/// Architectural cost: what does wiring one custom sink that skips
/// <see cref="MMP.Herald.Pipeline.Kernel.IKernelSink"/> into an
/// otherwise kernel-eligible pipeline cost?
///
/// <para>
/// Kernel eligibility requires every routed sink to implement
/// <see cref="MMP.Herald.Pipeline.Kernel.IKernelSink"/>. Every
/// built-in Herald.OSS sink does. A custom sink that implements only
/// <see cref="MMP.Herald.ILogger"/> disqualifies the kernel for the
/// whole pipeline, and every emit drops to the chain path. This
/// bench quantifies the premium an adopter pays for that mistake
/// and is the load-bearing reason
/// <c>QuickLogResult.KernelDiagnostic.RejectionReason</c> exists.
/// </para>
///
/// <para>
/// Both pipelines emit through Herald's typed-args path with four
/// properties. Pure-kernel uses <see cref="QuickLogBuilder.WithNullSink"/>;
/// mixed uses the same null sink plus a bridge to a discarding
/// <see cref="MMP.Herald.ILogger"/> that intentionally does not
/// implement <see cref="MMP.Herald.Pipeline.Kernel.IKernelSink"/>.
/// </para>
/// </summary>
[MemoryDiagnoser]
public class KernelMixedSinkBenchmarks
{
    private QuickLogResult _pureKernel = null!;
    private QuickLogResult _oneLegacyMixedIn = null!;

    [GlobalSetup]
    public void Setup()
    {
        _pureKernel = QuickLogBuilder.Create()
            .WithNullSink()
            .WithMinimumLevel("trace")
            .BuildAndCommit();

        // Same null sink, plus one bridge sink whose target is a
        // discarding ILogger that does NOT implement IKernelSink.
        // The bridge wrapper (PipelineBridge) is itself not
        // IKernelSink, so kernel eligibility fails and the entire
        // pipeline takes the chain path.
        _oneLegacyMixedIn = QuickLogBuilder.Create()
            .WithNullSink()
            .WithBridge(new ChainOnlyDiscardSink())
            .WithMinimumLevel("trace")
            .BuildAndCommit();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        DisposeAsync(_pureKernel);
        DisposeAsync(_oneLegacyMixedIn);
    }

    [Benchmark(Baseline = true)]
    public void PureKernel_FourProps()
    {
        _pureKernel.Logger.Information(LogCategory.App,
            "user {Id} purchased {Sku} for {Price} at {Time}",
            42, "alpha", 9.99, "now");
    }

    [Benchmark]
    public void OneLegacySink_ForcesChainPath_FourProps()
    {
        _oneLegacyMixedIn.Logger.Information(LogCategory.App,
            "user {Id} purchased {Sku} for {Price} at {Time}",
            42, "alpha", 9.99, "now");
    }

    private static void DisposeAsync(QuickLogResult? r)
    {
        if (r?.AsyncResource is { } resource)
            resource.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    /// <summary>
    /// Plain ILogger sink — does NOT implement IKernelSink, so the
    /// pipeline is kernel-ineligible whenever this sink is part of
    /// the route set.
    /// </summary>
    private sealed class ChainOnlyDiscardSink : MMP.Herald.ILogger
    {
        public void Log(LogEvent logEvent) { }
        public ValueTask LogAsync(LogEvent logEvent, CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;
    }
}

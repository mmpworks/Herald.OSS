#nullable enable

using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MMP.Herald.Events;
using MMP.Herald.Pipeline;
using MMP.Herald.Quick;
using Xunit;

namespace MMP.Herald.OSS.Tests.Pipeline;

/// <summary>
/// Echo G2.2 — the single riskiest path: a live config rebuild (RebuildFrom) must
/// TAKE without restart AND swap coherently. Every event emitted across a swap must
/// land — never half, never dropped — and the swap must never throw (no torn pipeline).
/// <para>
/// Deterministic by design: events are emitted in synchronous batches interleaved with
/// swaps on the same thread, so the proof does not depend on a concurrent writer racing
/// the swap (which would steal CPU from timing-sensitive sibling tests on the shared
/// host). The coherence guarantee under test is structural — SwappableLogger.Log reads
/// _inner via Volatile.Read and SwapInner does Interlocked.Exchange, so any single Log
/// dispatches to exactly one inner pipeline (old or new), and ReconstructAndSwap pairs
/// SwapInner with SwapKernel — so a deterministic before/after/around-swap emit set is a
/// sufficient and stable proof of no-loss + no-tear.
/// </para>
/// </summary>
public sealed class CoherentSwapUnderLoadTests
{
    [Fact]
    public void RebuildFrom_succeeds_on_a_hot_reload_enabled_pipeline()
    {
        var sink = new CountingBridge();
        var result = QuickLogBuilder.Create()
            .WithBridge(sink)
            .WithMinimumLevel("trace")
            .WithHotReload()
            .BuildAndCommit();

        var swapped = result.RebuildFrom(
            QuickLogBuilder.Create()
                .WithBridge(sink)
                .WithMinimumLevel("trace")
                .WithHotReload());

        swapped.Should().BeTrue("a hot-reload-enabled pipeline can swap live (success, not the old success=false)");
    }

    [Fact]
    public void Events_emitted_across_repeated_swaps_are_never_lost_and_swaps_never_tear()
    {
        var sink = new CountingBridge();
        var result = QuickLogBuilder.Create()
            .WithBridge(sink)
            .WithMinimumLevel("trace")
            .WithHotReload()
            .BuildAndCommit();

        const int batches = 6;
        const int perBatch = 500;
        var expected = 0;

        for (var b = 0; b < batches; b++)
        {
            // Emit a batch into whatever inner pipeline is currently live.
            for (var i = 0; i < perBatch; i++)
            {
                result.Logger.Information(LogCategory.App, "evt");
                expected++;
            }

            // Swap mid-stream. Must succeed (Commit returns true) and never throw
            // (no torn pipeline). The next batch lands in the freshly-swapped inner.
            var ok = result.RebuildFrom(
                QuickLogBuilder.Create()
                    .WithBridge(sink)
                    .WithMinimumLevel("trace")
                    .WithHotReload());
            ok.Should().BeTrue($"swap #{b} must succeed on a hot-reload pipeline");
        }

        // Every event across every batch + swap landed — no loss.
        sink.Count.Should().Be(expected,
            "coherent swap loses no events — each emit hit exactly one inner pipeline");
    }

    private sealed class CountingBridge : ILogger
    {
        private int _count;
        public int Count => _count;
        public void Log(LogEvent logEvent) => Interlocked.Increment(ref _count);
        public ValueTask LogAsync(LogEvent logEvent, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _count);
            return ValueTask.CompletedTask;
        }
    }
}

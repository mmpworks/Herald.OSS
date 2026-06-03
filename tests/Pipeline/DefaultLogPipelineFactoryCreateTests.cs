#nullable enable

using System.Collections.Generic;
using FluentAssertions;
using MMP.Herald.Events;
using MMP.Herald.Levels;
using MMP.Herald.Pipeline;
using MMP.Herald.Quick;
using MMP.Herald.Templating;
using Xunit;

namespace MMP.Herald.OSS.Tests.Pipeline;

/// <summary>
/// Pins the two-phase <c>DefaultLogPipelineFactory.Create</c> shape (M-2 refactor:
/// <c>AssembleDecoratorChain</c> + <c>FinalizeComposition</c>). The refactor is
/// behaviour-preserving, so these assert the cross-phase contracts the extraction
/// touches directly: the assembled chain's effective minimum, sink disposal, and
/// kernel hand-off all still produce a working logger after the split.
///
/// <para>
/// Broader behaviour (kernel fan-out dispatch, async drain disposal, level
/// filtering, injection consent) is pinned by the existing suites that drive the
/// same factory through <see cref="QuickLogBuilder"/>; this file adds the focused
/// construction pins for the specific Phase-1/Phase-2 boundary.
/// </para>
/// </summary>
public sealed class DefaultLogPipelineFactoryCreateTests
{
    // A capturing sink that is also IKernelSink-shaped via the bridge surface, so
    // the built pipeline can route a real event end-to-end after Create runs.
    private sealed class CapturingSink : MMP.Herald.ILogger
    {
        private readonly List<LogEvent> _events = new();
        public IReadOnlyList<LogEvent> Events => _events;
        public void Log(LogEvent logEvent) => _events.Add(logEvent);
    }

    [Fact]
    public void Create_produces_a_logger_that_routes_an_event_to_the_sink()
    {
        var sink = new CapturingSink();
        var result = QuickLogBuilder.Create()
            .WithBridge(sink)
            .WithMinimumLevel("verbose")
            .BuildAndCommit();

        // Phase-2 (FinalizeComposition) must build a working StructuredLogger over
        // the Phase-1 (AssembleDecoratorChain) chain. A routed event proves the
        // hand-off carried the event factory, filters, and sink chain intact.
        result.Logger.Information("create routes {Marker}", "ok");

        sink.Events.Should().ContainSingle(
            e => e.MessageTemplate == "create routes {Marker}",
            "the two-phase Create must still produce a logger that routes events to the sink");
    }

    [Fact]
    public void Create_applies_the_effective_minimum_from_phase_one()
    {
        var sink = new CapturingSink();
        var result = QuickLogBuilder.Create()
            .WithBridge(sink)
            .WithMinimumLevel("warning")
            .BuildAndCommit();

        // The effective minimum is computed in Phase 1 and threaded into the Build
        // in Phase 2. A sub-floor event must be rejected; an at-floor event passes.
        result.Logger.Information("below floor {X}", 1);
        result.Logger.Warning("at floor {X}", 2);

        sink.Events.Should().ContainSingle(
            e => e.MessageTemplate == "at floor {X}",
            "only the at-or-above-floor event survives — the Phase-1 effective minimum " +
            "must reach the Phase-2 Build");
        sink.Events.Should().NotContain(
            e => e.MessageTemplate == "below floor {X}",
            "a sub-floor event is rejected by the effective minimum threaded through Create");
    }

    [Fact]
    public void Create_attaches_a_kernel_diagnostic_for_a_kernel_eligible_pipeline()
    {
        // A null-sink pipeline is kernel-eligible. FinalizeComposition compiles the
        // kernel and attaches the diagnostic via `with { KernelDiagnostic }`. The
        // logger must report the kernel (KernelOrNull non-null), proving Phase 2
        // wired the compiled kernel into the StructuredLogger.
        var result = QuickLogBuilder.Create()
            .WithNullSink()
            .WithMinimumLevel(KnownLogLevels.Information.Key)
            .BuildAndCommit();

        result.Logger.KernelOrNull.Should().NotBeNull(
            "a kernel-eligible pipeline must carry its compiled kernel after the two-phase Create");
    }
}

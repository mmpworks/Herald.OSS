#nullable enable

using System.Collections.Generic;
using FluentAssertions;
using MMP.Herald.Levels;
using MMP.Herald.Pipeline;
using MMP.Herald.Pipeline.Kernel;
using MMP.Herald.Quick;
using Xunit;

namespace MMP.Herald.OSS.Tests.Pipeline;

/// <summary>
/// Pins the parent-child kernel-sharing contract on
/// <see cref="StructuredLogger.WithContext"/>. A long-running scope-bearing
/// child (e.g. a per-request ASP.NET logger built once at request start)
/// must observe a parent <see cref="StructuredLogger.SwapKernel"/> on its
/// next dispatch — otherwise hot reload silently orphans the child against
/// the retired kernel.
///
/// <para>
/// Pre-fix shape: the child captured the parent's <c>LogKernel</c> by value
/// at <c>WithContext</c> time, so <c>KernelOrNull</c> on the child kept
/// returning the original delegate after the parent's swap. Source-gen
/// consumers (HotPathLogger, downstream kernel-aware loggers) that pulled
/// the kernel through the child's accessor kept dispatching to retired
/// sinks. Post-fix shape: parent and child share a <c>KernelHolder</c>
/// reference, so a swap on the parent updates both sides.
/// </para>
/// </summary>
public sealed class WithContextKernelOrphanTests
{
    [Fact]
    public void Parent_and_child_see_the_same_kernel_before_any_swap()
    {
        var parent = BuildKernelEligibleLogger();

        var initialKernel = parent.KernelOrNull;
        initialKernel.Should().NotBeNull("the test pipeline is kernel-eligible");

        var child = parent.ForContext(new Dictionary<string, object?> { ["requestId"] = "abc" });

        child.KernelOrNull.Should().BeSameAs(initialKernel,
            "ForContext shares the parent's KernelHolder reference");
    }

    [Fact]
    public void Parent_swap_propagates_to_child_kernel_view()
    {
        var parent = BuildKernelEligibleLogger();
        var originalKernel = parent.KernelOrNull;
        originalKernel.Should().NotBeNull();

        var child = parent.ForContext(new Dictionary<string, object?> { ["requestId"] = "abc" });

        // Construct a sentinel kernel that the test can identify by reference.
        // Reference identity is the only semantic the test needs — the body
        // never runs because we don't dispatch through it.
        LogKernel sentinelKernel = (in LogEventBuffer _) => { };

        parent.SwapKernel(sentinelKernel);

        parent.KernelOrNull.Should().BeSameAs(sentinelKernel,
            "the parent observed its own swap");
        child.KernelOrNull.Should().BeSameAs(sentinelKernel,
            "the child shares the parent's KernelHolder and sees the new kernel — " +
            "without the holder, the child would still report the orphan");
    }

    [Fact]
    public void Child_swap_propagates_to_parent_kernel_view()
    {
        // Symmetric guard: the holder is shared, so a swap initiated on the
        // child reaches the parent too. This is not a documented use case,
        // but the contract should be observably consistent in both directions.
        var parent = BuildKernelEligibleLogger();
        var child = parent.ForContext(new Dictionary<string, object?> { ["requestId"] = "abc" });

        LogKernel sentinelKernel = (in LogEventBuffer _) => { };

        child.SwapKernel(sentinelKernel);

        parent.KernelOrNull.Should().BeSameAs(sentinelKernel);
        child.KernelOrNull.Should().BeSameAs(sentinelKernel);
    }

    [Fact]
    public void Swap_to_null_clears_both_parent_and_child()
    {
        var parent = BuildKernelEligibleLogger();
        var child = parent.ForContext(new Dictionary<string, object?> { ["requestId"] = "abc" });

        parent.KernelOrNull.Should().NotBeNull();

        parent.SwapKernel(null);

        parent.KernelOrNull.Should().BeNull();
        child.KernelOrNull.Should().BeNull();
    }

    [Fact]
    public void Grandchild_shares_the_root_kernel_holder()
    {
        // ForContext returns a StructuredLogger, which itself can be the
        // subject of another ForContext call. Each step should keep the
        // root holder reference rather than allocate a fresh one — otherwise
        // a grandchild orphans against the parent it was derived from.
        var parent = BuildKernelEligibleLogger();
        var child = parent.ForContext(new Dictionary<string, object?> { ["requestId"] = "abc" });
        var grandchild = child.ForContext(new Dictionary<string, object?> { ["tenant"] = "xyz" });

        LogKernel sentinelKernel = (in LogEventBuffer _) => { };

        parent.SwapKernel(sentinelKernel);

        grandchild.KernelOrNull.Should().BeSameAs(sentinelKernel);
    }

    // Build a logger configuration that QuickLogBuilder recognises as
    // kernel-eligible. The test relies on the kernel being non-null after
    // commit so the parent.KernelOrNull check is meaningful — every assertion
    // here is about reference identity, not dispatch behaviour. WithNullSink
    // produces an IKernelSink-compatible drop sink, which keeps the
    // pipeline within the kernel's eligibility surface.
    private static StructuredLogger BuildKernelEligibleLogger()
    {
        var result = QuickLogBuilder.Create()
            .WithNullSink()
            .WithMinimumLevel(KnownLogLevels.Information.Key)
            .BuildAndCommit();

        return result.Logger;
    }
}

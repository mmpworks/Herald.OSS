#nullable enable

using System.Collections.Generic;

namespace MMP.Herald.Pipeline.Kernel;

/// <summary>
/// Snapshot of kernel-path eligibility taken at pipeline construction.
/// Reports whether the kernel fast path activated and the human-readable
/// reason from <see cref="KernelEligibility"/> when it did not.
///
/// <para>
/// Every built-in Herald.OSS sink implements <see cref="IKernelSink"/>,
/// so a fresh pipeline with default options reports
/// <see cref="KernelEligible"/> = <c>true</c>. Configurations that
/// disable the kernel — deferred rendering, hot reload, dynamic level
/// policies, custom decorators that aren't kernel-aware, custom sinks
/// that don't implement <see cref="IKernelSink"/> — set
/// <see cref="KernelEligible"/> to <c>false</c> and populate
/// <see cref="RejectionReason"/> with the first failing rule.
/// </para>
///
/// <para>
/// <see cref="Advisories"/> carries non-blocking warnings the factory
/// detected at build time — pipeline still builds and runs, but
/// something about the configuration is likely to surprise the adopter
/// in production. The canonical case: a network-bound sink wired
/// without <see cref="MMP.Herald.Pipeline.AsyncLogger"/>, where every
/// emit blocks the producer thread on socket I/O. Inspect this list at
/// startup to surface configuration issues before they bite.
/// </para>
/// </summary>
public sealed record KernelDiagnostic(
    bool KernelEligible,
    string? RejectionReason,
    IReadOnlyList<string> Advisories)
{
    /// <summary>
    /// Convenience constructor for the common case of no advisories.
    /// </summary>
    public KernelDiagnostic(bool KernelEligible, string? RejectionReason)
        : this(KernelEligible, RejectionReason, System.Array.Empty<string>())
    {
    }
}

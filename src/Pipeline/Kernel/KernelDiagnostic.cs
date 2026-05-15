#nullable enable

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
/// </summary>
public sealed record KernelDiagnostic(
    bool KernelEligible,
    string? RejectionReason);

#nullable enable

using System.Collections.Generic;

namespace MMP.Herald.Pipeline.Kernel;

/// <summary>
/// Snapshot of kernel-path eligibility taken at pipeline construction.
/// Surfaces what an adopter could otherwise only learn by running the
/// benchmarks: did the kernel fast path activate, and which sinks (if
/// any) had to be auto-wrapped in <see cref="MaterializingKernelSink"/>
/// so the kernel could still fan out to them.
///
/// <para>
/// <see cref="KernelEligible"/> is true when the pipeline took the
/// kernel fast path. <see cref="RejectionReason"/> carries the
/// human-readable description from <see cref="KernelEligibility"/>
/// when the kernel was rejected.
/// </para>
///
/// <para>
/// <see cref="LegacySinks"/> reports sinks that do not implement
/// <see cref="IKernelSink"/> directly. The factory wraps each one in
/// <see cref="MaterializingKernelSink"/> so the kernel still activates,
/// but each wrapped emit costs more than a native
/// <see cref="IKernelSink"/> emit because the buffer is materialised
/// into a heap <see cref="MMP.Herald.Events.LogEvent"/> at the sink
/// boundary. Adopters who want the absolute best numbers can use this
/// list to find sinks worth upgrading.
/// </para>
/// </summary>
public sealed record KernelDiagnostic(
    bool KernelEligible,
    string? RejectionReason,
    IReadOnlyList<LegacySinkInfo> LegacySinks);

/// <summary>
/// One legacy sink entry inside <see cref="KernelDiagnostic.LegacySinks"/>.
/// Carries the position in the sink chain plus the runtime type name so
/// an adopter can map the entry back to their configuration.
/// </summary>
public sealed record LegacySinkInfo(int Index, string TypeName);

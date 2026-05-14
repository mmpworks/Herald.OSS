#nullable enable

namespace MMP.Herald.Pipeline.Kernel;

/// <summary>
/// Optional companion interface for <see cref="Enrichers.ILogEnricher"/> that
/// lets an enricher participate in the kernel fast path. Implementations
/// observe the buffer — they can emit metrics, increment counters, sample
/// events, or run side effects, but they cannot mutate the buffer (it is
/// passed by readonly reference).
///
/// <para>
/// Mutating enrichment — adding properties, setting context keys — is a
/// deeper phase of the kernel refactor (Plan C phase beyond this one). For
/// now, an enricher chain that contains any enricher that is not
/// <see cref="IKernelEnricher"/> forces the pipeline to use the decorator
/// chain (unchanged behavior).
/// </para>
///
/// <para>
/// When every enricher registered with <see cref="Quick.QuickLogBuilder"/>
/// implements this interface, <see cref="KernelEligibility"/> treats the
/// chain as kernel-safe and the compiler inlines the enricher calls
/// between event construction and sink dispatch.
/// </para>
/// </summary>
public interface IKernelEnricher
{
    /// <summary>
    /// Observe the buffer. Implementations must not retain the buffer past
    /// this call — the underlying storage is the caller's stack.
    /// </summary>
    void Enrich(in LogEventBuffer buffer);
}

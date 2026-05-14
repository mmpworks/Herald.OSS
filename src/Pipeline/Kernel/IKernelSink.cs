#nullable enable

namespace MMP.Herald.Pipeline.Kernel;

/// <summary>
/// Optional sink contract for direct consumption of kernel-path events.
/// Sinks that implement this interface can accept a
/// <see cref="LogEventBuffer"/> without the kernel materialising a heap
/// <see cref="Events.LogEvent"/>.
///
/// Not implementing <c>IKernelSink</c> is always safe — the kernel falls
/// back to <see cref="Events.LogEvent"/> dispatch at the sink boundary.
/// The difference is one allocation per event when any sink in the route
/// set lacks the interface.
/// </summary>
public interface IKernelSink
{
    /// <summary>
    /// Consume an event directly from the kernel path. Implementations must
    /// not retain the buffer past this call — the underlying storage is the
    /// caller's stack and will be reclaimed on return.
    /// </summary>
    void Log(in LogEventBuffer buffer);
}

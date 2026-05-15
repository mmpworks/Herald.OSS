#nullable enable

using System.Threading;
using System.Threading.Tasks;

namespace MMP.Herald.Pipeline.Kernel;

/// <summary>
/// Sink contract for direct consumption of kernel-path events. Sinks
/// implementing this interface accept a <see cref="LogEventBuffer"/>
/// without the kernel materialising a heap <see cref="Events.LogEvent"/>.
///
/// <para>
/// The contract carries both a synchronous entry
/// (<see cref="Log(in LogEventBuffer)"/>) and an async entry
/// (<see cref="LogAsync(in LogEventBuffer, CancellationToken)"/>). The
/// sync entry is the kernel's hot path. The async entry is forward-compat
/// for buffer-aware drain decorators landing in a later release — no
/// kernel call site dispatches through it at v0.x.
/// </para>
/// </summary>
public interface IKernelSink
{
    /// <summary>
    /// Consume an event directly from the kernel path. Implementations must
    /// not retain the buffer past this call — the underlying storage is the
    /// caller's stack and will be reclaimed on return.
    /// </summary>
    void Log(in LogEventBuffer buffer);

    /// <summary>
    /// Async kernel-path entry. Mirror of <see cref="Log(in LogEventBuffer)"/>
    /// for sinks that need to suspend on I/O.
    ///
    /// <para>
    /// C# forbids a <see langword="ref"/> struct from crossing an
    /// <see langword="await"/> — <see cref="LogEventBuffer"/> cannot survive
    /// into the async continuation. Implementers capture the buffer's
    /// contents synchronously at the top of the method body (via
    /// <see cref="LogEventBuffer.ToLogEvent"/> or by reading individual
    /// fields), then hand off to a normal async helper that operates on
    /// heap state. The implementer's body must NOT be marked
    /// <see langword="async"/> when the parameter is <c>in LogEventBuffer</c>
    /// (CS4012); the sync-outer / async-inner shape is the load-bearing
    /// idiom:
    /// </para>
    ///
    /// <code>
    /// public ValueTask LogAsync(in LogEventBuffer buffer, CancellationToken ct = default)
    /// {
    ///     var heap = buffer.ToLogEvent();   // synchronous capture
    ///     return SendAsync(heap, ct);       // hand off to async helper
    /// }
    /// private async ValueTask SendAsync(LogEvent ev, CancellationToken ct)
    /// {
    ///     await _http.SendAsync(BuildRequest(ev), ct).ConfigureAwait(false);
    /// }
    /// </code>
    ///
    /// <para>
    /// The default body is a sync forward to <see cref="Log(in LogEventBuffer)"/>.
    /// Sinks that do not perform real I/O inherit the default and never
    /// pay the async-state-machine cost. Network sinks override to do
    /// real async I/O without blocking the producer thread.
    /// </para>
    ///
    /// <para>
    /// <b>Forward-compat note.</b> No kernel call site dispatches through
    /// this method at v0.x. The pair (sync + async) locks the v1.0 sink
    /// contract shape in source today so buffer-aware drain decorators
    /// can wire through to it without changing the sink contract.
    /// </para>
    /// </summary>
    ValueTask LogAsync(in LogEventBuffer buffer, CancellationToken cancellationToken = default)
    {
        Log(in buffer);
        return ValueTask.CompletedTask;
    }
}

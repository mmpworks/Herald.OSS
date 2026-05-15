#nullable enable

using System.Threading;
using System.Threading.Tasks;
using MMP.Herald.Events;
using MMP.Herald.Pipeline.Kernel;

namespace MMP.Herald.Sinks;

/// <summary>
/// Base class for Herald sinks. Implements both <see cref="ILogger"/>
/// (the legacy heap-event contract) and <see cref="IKernelSink"/> (the
/// stack-buffer fast path) so subclasses get the kernel-eligibility
/// guarantee for free.
///
/// <para>
/// <b>What subclasses must do.</b> Override <see cref="Log(LogEvent)"/>
/// with the sink's actual write logic. That's the minimum surface for
/// a working sink. Pipelines that wire the subclass take the kernel
/// fast path because <see cref="IKernelSink"/> is implemented by the
/// base.
/// </para>
///
/// <para>
/// <b>What the base does on the kernel path.</b> The default
/// <see cref="Log(in LogEventBuffer)"/> body uses
/// <see cref="KernelBufferAdapter.MaterializeAndRender(in LogEventBuffer)"/>
/// to produce a fully-formed <see cref="LogEvent"/> with a rendered
/// <see cref="LogEvent.Message"/>, then forwards to the heap-path
/// override. Every sink that reads <c>Message</c> gets correct
/// behaviour without writing any boundary code.
/// </para>
///
/// <para>
/// <b>Zero-allocation opt-in.</b> Sinks that do NOT read
/// <see cref="LogEvent.Message"/> — pure structured / JSON / OTLP
/// writers — can override <see cref="Log(in LogEventBuffer)"/> directly
/// and consume the buffer in place. The base's heap-path forwarding is
/// skipped; the sink pays no boundary allocation. The override pattern:
/// </para>
///
/// <code>
/// public override void Log(in LogEventBuffer buffer)
/// {
///     // Read template + properties straight from the buffer.
///     // No materialise, no render — zero allocation.
///     WriteJson(buffer.MessageTemplate, buffer.CompactProperties);
/// }
/// </code>
/// </summary>
public abstract class HeraldSinkBase : ILogger, IKernelSink
{
    /// <summary>
    /// Handle a fully-formed heap <see cref="LogEvent"/>. Subclasses
    /// implement their write logic here. Called both from the chain
    /// path (directly) and from the default kernel path (after the
    /// base materialises the buffer).
    /// </summary>
    public abstract void Log(LogEvent logEvent);

    /// <summary>
    /// Kernel-path entry. The default body materialises the buffer
    /// into a heap event with a rendered Message and forwards to
    /// <see cref="Log(LogEvent)"/>. Sinks that do not need the
    /// rendered text override this method to consume the buffer
    /// directly and avoid the boundary allocation.
    /// </summary>
    public virtual void Log(in LogEventBuffer buffer) =>
        Log(KernelBufferAdapter.MaterializeAndRender(in buffer));

    /// <summary>
    /// Async kernel-path entry. Default body is a sync forward to
    /// <see cref="Log(in LogEventBuffer)"/>, returning a completed
    /// <see cref="ValueTask"/>. Network sinks override with the
    /// sync-outer / async-inner pattern documented on
    /// <see cref="IKernelSink.LogAsync(in LogEventBuffer, CancellationToken)"/>:
    /// capture the buffer's contents synchronously at the top of the
    /// body, then hand off to a normal async helper that operates on
    /// heap state.
    /// </summary>
    public virtual ValueTask LogAsync(in LogEventBuffer buffer, CancellationToken cancellationToken = default)
    {
        Log(in buffer);
        return ValueTask.CompletedTask;
    }
}

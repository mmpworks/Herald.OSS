#nullable enable

using MMP.Herald.Events;

namespace MMP.Herald.Pipeline.Kernel;

/// <summary>
/// Adapter that makes any <see cref="ILogger"/> sink kernel-compatible.
/// On the kernel path the adapter calls <see cref="LogEventBuffer.ToLogEvent"/>
/// to produce a heap <see cref="LogEvent"/>, then forwards to the inner
/// logger. The chain path dispatches directly to the inner logger.
///
/// <para>
/// This is the "universal compatibility" shim. A native
/// <see cref="IKernelSink"/> implementation on a given sink — one that
/// writes its own serialised form from a <see cref="LogEventBuffer"/>
/// without materialising the heap event — is strictly faster (no 80-B
/// LogEvent allocation, no property array copy). The adapter gives
/// sinks that haven't migrated yet a middle-ground fast path: the
/// kernel still bypasses the decorator chain, at the cost of one event
/// materialisation at the sink boundary.
/// </para>
/// </summary>
public sealed class MaterializingKernelSink : ILogger, IKernelSink
{
    private readonly ILogger _inner;

    public MaterializingKernelSink(ILogger inner)
    {
        System.ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
    }

    public void Log(LogEvent logEvent) => _inner.Log(logEvent);

    public void Log(in LogEventBuffer buffer) => _inner.Log(buffer.ToLogEvent());

    /// <summary>The wrapped logger. Exposed for diagnostics and pipeline introspection.</summary>
    public ILogger Inner => _inner;
}

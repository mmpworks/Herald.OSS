#nullable enable

using MMP.Herald.Events;
using MMP.Herald.Templating;

namespace MMP.Herald.Pipeline.Kernel;

/// <summary>
/// Adapter that makes any <see cref="ILogger"/> sink kernel-compatible.
/// On the kernel path the adapter materialises the
/// <see cref="LogEventBuffer"/> into a heap <see cref="LogEvent"/> and
/// forwards to the inner logger. The chain path dispatches directly to
/// the inner logger.
///
/// <para>
/// This is the "universal compatibility" shim. A native
/// <see cref="IKernelSink"/> implementation on a given sink — one that
/// writes its own serialised form from a <see cref="LogEventBuffer"/>
/// without materialising the heap event — is strictly faster (no
/// LogEvent record allocation, no property array copy). The adapter
/// gives sinks that haven't migrated yet a middle-ground fast path:
/// the kernel still bypasses the decorator chain, at the cost of one
/// event materialisation at the sink boundary.
/// </para>
///
/// <para>
/// <b>Message rendering at the boundary.</b> The kernel never builds
/// the rendered <see cref="LogEvent.Message"/> string — kernel buffers
/// carry only template + properties. Sinks that implement
/// <see cref="IStructuredOnlySink"/> (JSON, OTLP, the null sink)
/// declare they never read the rendered message; the adapter forwards
/// the buffer-materialised event with an empty <c>Message</c> for
/// them. Sinks that do not claim the marker get the message rendered
/// at the boundary so their behaviour on the kernel path matches what
/// they would have seen on the chain path. The render uses the same
/// <see cref="MessageTemplateParser"/> as the chain path, so the
/// rendered text is byte-for-byte identical.
/// </para>
/// </summary>
public sealed class MaterializingKernelSink : ILogger, IKernelSink
{
    private readonly ILogger _inner;
    // Non-null only when the inner sink might read LogEvent.Message
    // (i.e. it does NOT claim IStructuredOnlySink). The renderer
    // reference is captured at construction so the boundary path
    // skips one virtual dispatch on every emit.
    private readonly MessageTemplateParser? _renderer;

    public MaterializingKernelSink(ILogger inner)
        : this(inner, renderer: null) { }

    public MaterializingKernelSink(ILogger inner, MessageTemplateParser? renderer)
    {
        System.ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
        _renderer = inner is IStructuredOnlySink ? null : renderer;
    }

    public void Log(LogEvent logEvent) => _inner.Log(logEvent);

    public void Log(in LogEventBuffer buffer)
    {
        var heap = buffer.ToLogEvent();
        if (_renderer is null || !string.IsNullOrEmpty(heap.Message))
        {
            _inner.Log(heap);
            return;
        }

        // Kernel-path buffers always carry an empty Message; the inner
        // sink reads it. Render once at the boundary so the sink sees
        // the same text it would have on the chain path.
        var rendered = _renderer.Render(heap.MessageTemplate, heap.Properties);
        _inner.Log(heap with { Message = rendered.Message });
    }

    /// <summary>The wrapped logger. Exposed for diagnostics and pipeline introspection.</summary>
    public ILogger Inner => _inner;
}

#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using MMP.Herald.Events;
using MMP.Herald.Templating;

namespace MMP.Herald.Pipeline.Kernel;

/// <summary>
/// Helper for sinks implementing <see cref="IKernelSink"/> that need a
/// fully-materialised <see cref="LogEvent"/> with a rendered
/// <see cref="LogEvent.Message"/>. Kernel-path buffers always carry an
/// empty <c>Message</c> because the accept path skips the render to
/// stay zero-allocation; sinks that read the rendered text take that
/// cost at their own boundary by calling
/// <see cref="MaterializeAndRender"/>.
///
/// <para>
/// Use this helper when porting an existing legacy sink to
/// <see cref="IKernelSink"/>: the typical migration is a one-line
/// <c>Log(in LogEventBuffer)</c> method that calls
/// <c>MaterializeAndRender</c> and forwards to the existing
/// <c>Log(LogEvent)</c> body.
/// </para>
///
/// <para>
/// Sinks that do NOT read <see cref="LogEvent.Message"/> — pure
/// structured / JSON / OTLP outputs — should skip the helper, call
/// <see cref="LogEventBuffer.ToLogEvent"/> directly (or consume the
/// buffer in place), and avoid the per-event render cost.
/// </para>
/// </summary>
public static class KernelBufferAdapter
{
    // Shared parser: stateless once constructed, and the internal
    // parse-strategy carries its own cache, so every sink in the
    // process amortises template parsing through one instance.
    private static readonly MessageTemplateParser SharedRenderer = new();

    /// <summary>
    /// Materialise the buffer into a heap <see cref="LogEvent"/> and
    /// render the <see cref="LogEvent.Message"/> from the template
    /// when it is empty. The returned event is safe to hand to legacy
    /// <c>ILogger.Log(LogEvent)</c> consumers — they see the same
    /// rendered text the chain path would have produced.
    /// </summary>
    public static LogEvent MaterializeAndRender(in LogEventBuffer buffer)
    {
        var heap = buffer.ToLogEvent();
        if (!string.IsNullOrEmpty(heap.Message)) return heap;

        var rendered = SharedRenderer.Render(heap.MessageTemplate, heap.Properties);
        return heap with { Message = rendered.Message };
    }

    /// <summary>
    /// Build a stack <see cref="LogEventBuffer"/> view over a heap
    /// <see cref="LogEvent"/> so a buffer-shaped decision (a W6 exclude
    /// predicate, a W7 key selector) can run unchanged on the heap entry point.
    ///
    /// <para>
    /// This is the load-bearing bridge that lets one predicate drive both
    /// <c>Log(in LogEventBuffer)</c> and <c>Log(LogEvent)</c>: the heap twin
    /// wraps its event here and evaluates the <i>same</i> delegate, so the two
    /// entry points cannot diverge. The property span is zero-copy when the
    /// event's <see cref="LogEvent.Properties"/> is a <c>LogProperty[]</c>
    /// (the common factory shape); any other list backing copies once into
    /// <paramref name="scratch"/> — a heap-path-only cost the hot buffer path
    /// never pays.
    /// </para>
    ///
    /// <para>
    /// The returned buffer does not escape its caller — it is a <c>ref struct</c>
    /// consumed immediately for a routing/filter decision. The optional
    /// <paramref name="scratch"/> keeps the copy out of this helper so the
    /// caller owns its lifetime on the stack.
    /// </para>
    /// </summary>
    public static LogEventBuffer AsBuffer(LogEvent logEvent, ref LogProperty[]? scratch)
    {
        ArgumentNullException.ThrowIfNull(logEvent);

        ReadOnlySpan<LogProperty> props = PropertySpan(logEvent.Properties, ref scratch);
        return new LogEventBuffer(
            timeUtc: logEvent.TimeUtc,
            level: logEvent.Level,
            category: logEvent.Category,
            messageTemplate: logEvent.MessageTemplate,
            message: logEvent.Message,
            properties: props,
            eventId: logEvent.EventId,
            genSource: logEvent.GenSource);
    }

    // Zero-copy span when the list is already a LogProperty[]; otherwise copy
    // once into the caller-owned scratch. The heap entry point is not the hot
    // path, so a defensive copy for the uncommon list backing is acceptable.
    private static ReadOnlySpan<LogProperty> PropertySpan(
        IReadOnlyList<LogProperty> properties, ref LogProperty[]? scratch)
    {
        if (properties.Count == 0) return ReadOnlySpan<LogProperty>.Empty;
        if (properties is LogProperty[] array) return array;
        if (properties is List<LogProperty> list) return CollectionsMarshal.AsSpan(list);

        if (scratch is null || scratch.Length < properties.Count)
            scratch = new LogProperty[properties.Count];
        for (var i = 0; i < properties.Count; i++) scratch[i] = properties[i];
        return scratch.AsSpan(0, properties.Count);
    }
}

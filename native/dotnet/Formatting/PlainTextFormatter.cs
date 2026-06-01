#nullable enable

using System;
using System.Globalization;
using System.Text;
using MMP.Herald.Events;
using MMP.Herald.Levels;
using MMP.Herald.Pipeline.Kernel;
using MMP.Herald.Pooling;

namespace MMP.Herald.Formatting;

/// <summary>
/// Plain text formatter for storage-oriented sinks.
/// Uses the rendered message, not the raw template.
/// </summary>
public sealed class PlainTextFormatter : ILogFormatter
{
    private readonly ILogLevelRegistry _levelRegistry;

    public PlainTextFormatter(ILogLevelRegistry levelRegistry)
    {
        _levelRegistry = levelRegistry;
    }

    public string Format(LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);

        var registeredLevel = _levelRegistry.GetRegisteredLevel(logEvent.Level);
        var builder = StringBuilderPool.Rent();

        builder.Append('[');
        builder.Append(logEvent.TimeUtc.ToString("O", CultureInfo.InvariantCulture));
        builder.Append("] [");
        builder.Append(registeredLevel.Level.DisplayName);
        builder.Append(':');
        builder.Append(registeredLevel.Rank);
        builder.Append("] ");
        builder.Append(logEvent.Category.Value);
        builder.Append(": ");
        builder.Append(logEvent.Message);

        using var sorted = SortedContextBuffer.Create(logEvent.Context);
        foreach (var pair in sorted.AsSpan())
        {
            builder.Append(' ');
            builder.Append(pair.Key);
            builder.Append('=');
            builder.Append(pair.Value?.ToString() ?? "null");
        }

        return StringBuilderPool.ReturnAndGetString(builder);
    }

    /// <summary>
    /// Kernel-path overload. Plain text needs the message holes FILLED, but on the
    /// kernel fast path <see cref="LogEventBuffer.Message"/> is empty — the kernel
    /// renders text only on demand (see the field doc), and storage sinks never
    /// signalled that demand. Reading <c>buffer.Message</c> directly therefore
    /// produced a record with an empty body (timestamp + level, no message). So we
    /// materialise + render via the shared <see cref="KernelBufferAdapter"/> — the
    /// same path the base sink and the archive sink use — and reuse
    /// <see cref="Format(LogEvent)"/>. PlainTextFormatter is only used by the file
    /// (storage) sink, where the per-event heap event is negligible against disk I/O.
    /// JSON file output is unaffected (it uses <c>JsonFormatter</c>, which writes the
    /// template + properties and needs no rendered message).
    /// </summary>
    public string Format(in LogEventBuffer buffer)
        => Format(KernelBufferAdapter.MaterializeAndRender(in buffer));
}

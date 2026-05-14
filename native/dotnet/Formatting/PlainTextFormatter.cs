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
    /// Kernel-path overload. Reads fields directly from the stack-allocated
    /// buffer and skips the per-event <see cref="LogEvent"/> + property-array
    /// allocations that the legacy <see cref="Format(LogEvent)"/> path pays.
    /// Context is always empty on the kernel path (gated in
    /// <c>StructuredLogger.Log</c>), so no context emission loop.
    /// </summary>
    public string Format(in LogEventBuffer buffer)
    {
        var registeredLevel = _levelRegistry.GetRegisteredLevel(buffer.Level);
        var builder = StringBuilderPool.Rent();

        builder.Append('[');
        builder.Append(buffer.TimeUtc.ToString("O", CultureInfo.InvariantCulture));
        builder.Append("] [");
        builder.Append(registeredLevel.Level.DisplayName);
        builder.Append(':');
        builder.Append(registeredLevel.Rank);
        builder.Append("] ");
        builder.Append(buffer.Category.Value);
        builder.Append(": ");
        builder.Append(buffer.Message);

        return StringBuilderPool.ReturnAndGetString(builder);
    }
}

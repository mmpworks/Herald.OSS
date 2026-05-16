#nullable enable

using System;
using System.Collections.Generic;
using MMP.Herald.Events;
using MMP.Herald.Levels;
using MMP.Herald.Templating;

namespace MMP.Herald.Pipeline.Kernel;

/// <summary>
/// Stack-allocated log event envelope used by the kernel pipeline. Mirrors the
/// fields of <see cref="LogEvent"/> but stays on the caller's frame — no heap
/// allocation, no boxing through virtual dispatch.
///
/// The ref-struct constraint is intentional. It prevents the buffer from
/// escaping to a field, async state machine, or boxed interface, which is what
/// makes the kernel-path optimizations safe: escape analysis knows the event
/// cannot leak and the JIT can keep everything in registers.
///
/// Sinks that want to consume buffers directly implement
/// <see cref="IKernelSink"/>. Sinks that need a heap <see cref="LogEvent"/>
/// (most today) receive one via <see cref="ToLogEvent"/> at the boundary —
/// paid once per event when at least one consuming sink needs it.
/// </summary>
public readonly ref struct LogEventBuffer
{
    /// <summary>UTC timestamp captured at the call site.</summary>
    public readonly DateTimeOffset TimeUtc;

    /// <summary>Level the caller asked to log at.</summary>
    public readonly LogLevel Level;

    /// <summary>Caller-supplied category; typically a static field.</summary>
    public readonly LogCategory Category;

    /// <summary>Raw message template with {Placeholder} tokens.</summary>
    public readonly string MessageTemplate;

    /// <summary>
    /// Rendered message string. Empty when the kernel skipped rendering because
    /// no downstream sink asked for text (e.g. null sink, JSON sink).
    /// </summary>
    public readonly string Message;

    /// <summary>
    /// Property values matching the template placeholders, in the legacy
    /// reference-type form. Populated when the caller handed in a
    /// <see cref="LogProperty"/>[] or list; empty when the caller used the
    /// compact-struct overload (see <see cref="CompactProperties"/>).
    /// Consumers should pick whichever span is non-empty.
    /// </summary>
    public readonly ReadOnlySpan<LogProperty> Properties;

    /// <summary>
    /// Property values in compact-struct form. Populated when the caller used
    /// the <see cref="LogPropertyCompact"/> overload (typically
    /// <c>stackalloc LogPropertyCompact[N]</c>) — no per-property heap
    /// allocation at the call site. Empty when the caller used the legacy
    /// <see cref="Properties"/> overload.
    /// </summary>
    public readonly ReadOnlySpan<LogPropertyCompact> CompactProperties;

    /// <summary>
    /// Optional event identifier for de-duplication or trace correlation.
    /// Null in the common case.
    /// </summary>
    public readonly LogEventId? EventId;

    public LogEventBuffer(
        DateTimeOffset timeUtc,
        LogLevel level,
        LogCategory category,
        string messageTemplate,
        string message,
        ReadOnlySpan<LogProperty> properties,
        LogEventId? eventId = null)
    {
        TimeUtc = timeUtc;
        Level = level;
        Category = category;
        MessageTemplate = messageTemplate;
        Message = message;
        Properties = properties;
        CompactProperties = default;
        EventId = eventId;
    }

    /// <summary>
    /// Compact-property overload. The caller passes a span of
    /// <see cref="LogPropertyCompact"/> (typically stack-allocated) and the
    /// buffer carries it through to the sinks without materialising
    /// <see cref="LogProperty"/> records — closing the per-property allocation
    /// gap on the hot path.
    /// </summary>
    public LogEventBuffer(
        DateTimeOffset timeUtc,
        LogLevel level,
        LogCategory category,
        string messageTemplate,
        string message,
        ReadOnlySpan<LogPropertyCompact> compactProperties,
        LogEventId? eventId = null)
    {
        TimeUtc = timeUtc;
        Level = level;
        Category = category;
        MessageTemplate = messageTemplate;
        Message = message;
        Properties = default;
        CompactProperties = compactProperties;
        EventId = eventId;
    }

    /// <summary>
    /// Materialise this buffer as a heap <see cref="LogEvent"/>. Paid once per
    /// event when at least one downstream sink cannot accept the buffer
    /// directly. Sinks that implement <see cref="IKernelSink"/> skip this.
    /// </summary>
    public LogEvent ToLogEvent()
    {
        IReadOnlyList<LogProperty> props;
        if (!Properties.IsEmpty)
        {
            props = Properties.ToArray();
        }
        else if (!CompactProperties.IsEmpty)
        {
            // Inflate compact props to full LogProperty records for sinks
            // that expect the legacy type. This is the boundary where the
            // per-property heap allocation re-appears — worth it only when
            // a heap LogEvent is already being built.
            var buffer = new LogProperty[CompactProperties.Length];
            for (var i = 0; i < CompactProperties.Length; i++)
            {
                buffer[i] = CompactProperties[i].ToLogProperty();
            }
            props = buffer;
        }
        else
        {
            props = LogEvent.EmptyProperties;
        }

        return new LogEvent(
            TimeUtc: TimeUtc,
            Level: Level,
            Category: Category,
            MessageTemplate: MessageTemplate,
            Message: Message,
            Properties: props,
            Context: LogEvent.EmptyContext,
            EventId: EventId);
    }
}

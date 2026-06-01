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

    /// <summary>
    /// Provenance stamp — optional source token a downstream commercial
    /// wrapper stamps on this event so gated sinks can decide whether to
    /// accept it. See <see cref="LogEvent.GenSource"/> for the heap
    /// equivalent. Null for events from un-gated callers, which is the
    /// default for the Herald.OSS hot path.
    /// </summary>
    public readonly string? GenSource;

    public LogEventBuffer(
        DateTimeOffset timeUtc,
        LogLevel level,
        LogCategory category,
        string messageTemplate,
        string message,
        ReadOnlySpan<LogProperty> properties,
        LogEventId? eventId = null,
        string? genSource = null)
    {
        TimeUtc = timeUtc;
        Level = level;
        Category = category;
        MessageTemplate = messageTemplate;
        Message = message;
        Properties = properties;
        CompactProperties = default;
        EventId = eventId;
        GenSource = genSource;
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
        LogEventId? eventId = null,
        string? genSource = null)
    {
        TimeUtc = timeUtc;
        Level = level;
        Category = category;
        MessageTemplate = messageTemplate;
        Message = message;
        Properties = default;
        CompactProperties = compactProperties;
        EventId = eventId;
        GenSource = genSource;
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
            EventId: EventId,
            GenSource: GenSource);
    }

    // ── Property-access span helpers (shared by W6 filter + W7 routing) ──
    //
    // Both kernel decorators need to read a named property off the buffer
    // without materialising a dictionary. A buffer carries its properties in
    // exactly one of two spans (Properties XOR CompactProperties — see the
    // two constructors above), so each helper scans whichever span is
    // populated. Scans are linear over a small N (template placeholder count,
    // typically < 16), run entirely on the stack, and allocate nothing.
    //
    // Name matching uses ordinal string comparison — property names are
    // template tokens (compile-time literals), not user text, so culture-aware
    // comparison would be both wrong and slower.

    /// <summary>
    /// True when a property with the given name is present, regardless of its
    /// value. The W6 <c>Filter.ByExcluding(e =&gt; e.Properties.ContainsKey("X"))</c>
    /// equivalent reads through here.
    /// </summary>
    public bool HasProperty(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        // Exactly one span is populated; the empty one's loop is skipped at
        // zero cost (IsEmpty short-circuits via Length == 0).
        for (var i = 0; i < Properties.Length; i++)
        {
            if (string.Equals(Properties[i].Name, name, StringComparison.Ordinal))
                return true;
        }

        for (var i = 0; i < CompactProperties.Length; i++)
        {
            if (string.Equals(CompactProperties[i].Name, name, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Looks up a property value by name. Returns <c>true</c> and sets
    /// <paramref name="value"/> when found; returns <c>false</c> and sets it to
    /// <c>null</c> otherwise. The value is the resolved object (boxing a
    /// compact scalar only if the matched slot holds one) — callers that only
    /// need presence should prefer <see cref="HasProperty"/>, which never boxes.
    /// </summary>
    public bool TryGetProperty(string name, out object? value)
    {
        ArgumentNullException.ThrowIfNull(name);

        for (var i = 0; i < Properties.Length; i++)
        {
            if (string.Equals(Properties[i].Name, name, StringComparison.Ordinal))
            {
                value = Properties[i].Value;
                return true;
            }
        }

        for (var i = 0; i < CompactProperties.Length; i++)
        {
            if (string.Equals(CompactProperties[i].Name, name, StringComparison.Ordinal))
            {
                value = CompactProperties[i].Value;
                return true;
            }
        }

        value = null;
        return false;
    }

    /// <summary>
    /// Returns the value of the named property, or <c>null</c> when absent.
    /// Convenience over <see cref="TryGetProperty"/> for callers that treat
    /// "absent" and "present-but-null" identically.
    /// </summary>
    public object? GetProperty(string name) =>
        TryGetProperty(name, out var value) ? value : null;

    /// <summary>
    /// Returns the <b>string</b> value of the named property as a span, without
    /// allocating, when the property is present and already a string. The W7
    /// key selector reads through here: routing keys must be sliced out of an
    /// existing buffer string, never formatted, so the hot path stays 0-alloc.
    ///
    /// <para>
    /// Returns <c>true</c> only when the property exists <i>and</i> its stored
    /// value is a <see cref="string"/>. A present-but-non-string property
    /// (an int, a destructured object) returns <c>false</c> — the selector
    /// cannot produce a key span without formatting, and formatting on the hot
    /// path is exactly what this method exists to prevent. Callers route such
    /// events to their default destination.
    /// </para>
    /// </summary>
    public bool TryGetStringSpan(string name, out ReadOnlySpan<char> value)
    {
        ArgumentNullException.ThrowIfNull(name);

        for (var i = 0; i < Properties.Length; i++)
        {
            if (string.Equals(Properties[i].Name, name, StringComparison.Ordinal))
                return AsStringSpan(Properties[i].Value, out value);
        }

        for (var i = 0; i < CompactProperties.Length; i++)
        {
            ref readonly var slot = ref CompactProperties[i];
            if (string.Equals(slot.Name, name, StringComparison.Ordinal))
            {
                // Compact string slots store the string in RefValue with the
                // String kind, so we read it without triggering the lazy box
                // that the Value property would for a scalar.
                if (slot.Kind == LogPropertyKind.String && slot.RefValue is string s)
                {
                    value = s.AsSpan();
                    return true;
                }
                value = default;
                return false;
            }
        }

        value = default;
        return false;
    }

    private static bool AsStringSpan(object? candidate, out ReadOnlySpan<char> value)
    {
        if (candidate is string s)
        {
            value = s.AsSpan();
            return true;
        }
        value = default;
        return false;
    }
}

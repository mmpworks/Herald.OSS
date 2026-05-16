#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using MMP.Herald.Levels;
using MMP.Herald.Templating;

namespace MMP.Herald.Events;

/// <summary>
/// Immutable logging event.
/// Stores both the original template and the fully rendered message.
///
/// <para>
/// <b>GenSource — provenance stamp.</b> Optional string token identifying
/// the source that produced this event. Herald.OSS does not stamp the
/// field through its default factories, so events from the OSS hot path
/// arrive with <c>GenSource = null</c>. A downstream commercial wrapper
/// (or any consumer who needs multi-tenant routing without per-sink code)
/// stamps the field at construction time and pairs it with a
/// <see cref="Pipeline.Kernel.GenSourceGatedSink"/> at sink-composition
/// time. See that type for the gating semantics.
/// </para>
/// </summary>
public sealed record LogEvent(
    DateTimeOffset TimeUtc,
    LogLevel Level,
    LogCategory Category,
    string MessageTemplate,
    string Message,
    IReadOnlyList<LogProperty> Properties,
    IReadOnlyDictionary<string, object?> Context,
    LogEventId? EventId = null,
    string? CausedBy = null,
    string? GenSource = null)
{
    public static IReadOnlyDictionary<string, object?> EmptyContext { get; } =
        new Dictionary<string, object?>();

    public static IReadOnlyList<LogProperty> EmptyProperties { get; } =
        Array.Empty<LogProperty>();

    // Lazy property index: built on first access, O(1) lookups thereafter.
    //
    // Concurrency: every read goes through Volatile.Read and every publish
    // through Volatile.Write so a reader on a weakly-ordered architecture
    // (ARM Linux, Apple Silicon Linux) cannot observe a non-null reference
    // whose Dictionary contents are not yet fully published. On x86/x64 the
    // strong memory model already covers this; the barriers cost nothing on
    // those platforms and close the formal hole on the others.
    //
    // Volatile is the right tool here — not Interlocked.CompareExchange —
    // because the dictionary is built from immutable Properties and is
    // itself immutable once published. Two threads that race the build
    // both produce equal dictionaries; the loser's copy is harmlessly
    // GC'd. CompareExchange would add an interlocked op for no functional
    // benefit on this code path. Compare with HeraldRegistration.Api,
    // which guards a stateful instance whose loser CANNOT be GC'd
    // safely — that one uses CompareExchange. Same engine, different
    // invariants, different primitive.
    private Dictionary<string, LogProperty>? _propertyIndex;

    /// <summary>
    /// Look up a property by name in O(1). Returns null if not found.
    /// The index is built lazily on first call and cached for the lifetime
    /// of this event. Case-insensitive.
    /// </summary>
    public LogProperty? GetProperty(string name) =>
        ResolveIndex() is { } index && index.TryGetValue(name, out var prop)
            ? prop
            : null;

    /// <summary>
    /// Check if a property with the given name exists. O(1) after first access.
    /// </summary>
    public bool HasProperty(string name) =>
        ResolveIndex() is { } index && index.ContainsKey(name);

    // Single read/build helper so the volatile-publication discipline lives
    // in one place. Returns null when the event has no properties — the two
    // public accessors share the empty-event short circuit through this null.
    private Dictionary<string, LogProperty>? ResolveIndex()
    {
        var index = Volatile.Read(ref _propertyIndex);
        if (index is not null) return index;
        if (Properties.Count == 0) return null;

        index = BuildPropertyIndex();
        Volatile.Write(ref _propertyIndex, index);
        return index;
    }

    private Dictionary<string, LogProperty> BuildPropertyIndex()
    {
        var dict = new Dictionary<string, LogProperty>(Properties.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var p in Properties)
            dict.TryAdd(p.Name, p);
        return dict;
    }
}
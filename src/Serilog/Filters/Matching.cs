#nullable enable

using System;
using MMP.Herald.Events;

namespace MMP.Herald.Serilog.Filters;

/// <summary>
/// Predicate factory mirroring Serilog's <c>Serilog.Filters.Matching</c> helpers.
/// Each method returns a <see cref="Func{LogEvent, Boolean}"/> over the native
/// <see cref="MMP.Herald.Events.LogEvent"/> — the exact predicate type that
/// <c>LoggerConfiguration.Filter.ByExcluding(...)</c> and
/// <c>Filter.ByIncludingOnly(...)</c> accept. A migrated
/// <c>.Filter.ByExcluding(Matching.WithProperty&lt;T&gt;(name, predicate))</c>
/// call therefore resolves here with no type bridge.
///
/// <para>
/// Property lookup mirrors Serilog's event model, which carries a single flat
/// property bag. Herald splits an event into <see cref="LogEvent.Properties"/>
/// (template-bound values) and <see cref="LogEvent.Context"/> (enrichment /
/// scope values). To match Serilog's "one bag" semantics, these helpers consult
/// the structured properties first and fall back to the context bag, so an
/// enrichment-supplied property is found the same way Serilog would find it.
/// </para>
/// </summary>
public static class Matching
{
    /// <summary>
    /// Match events that carry a property named <paramref name="propertyName"/>
    /// whose value satisfies <paramref name="predicate"/>.
    /// Mirrors <c>Serilog.Filters.Matching.WithProperty&lt;T&gt;(name, predicate)</c>.
    ///
    /// <para>
    /// The returned predicate is <c>true</c> only when the property is present AND
    /// its resolved value is assignable to <typeparamref name="T"/> AND
    /// <paramref name="predicate"/> returns <c>true</c> for that value. A missing
    /// property, or a value of the wrong type, yields <c>false</c> — identical to
    /// Serilog, where a type mismatch fails the match rather than throwing.
    /// </para>
    /// </summary>
    /// <typeparam name="T">The expected property value type.</typeparam>
    /// <param name="propertyName">The property name to look up. Must not be null.</param>
    /// <param name="predicate">The test applied to the typed value. Must not be null.</param>
    public static Func<LogEvent, bool> WithProperty<T>(string propertyName, Func<T, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(propertyName);
        ArgumentNullException.ThrowIfNull(predicate);

        return logEvent =>
            TryResolveValue(logEvent, propertyName, out var value)
            && value is T typed
            && predicate(typed);
    }

    /// <summary>
    /// Match events that simply carry a property named
    /// <paramref name="propertyName"/>, regardless of value.
    /// Mirrors the non-generic <c>Serilog.Filters.Matching.WithProperty(name)</c>.
    /// </summary>
    /// <param name="propertyName">The property name to look up. Must not be null.</param>
    public static Func<LogEvent, bool> WithProperty(string propertyName)
    {
        ArgumentNullException.ThrowIfNull(propertyName);
        return logEvent => TryResolveValue(logEvent, propertyName, out _);
    }

    // Single resolution helper so both overloads share one lookup discipline:
    // structured Properties first (O(1) via the event's cached index), then the
    // Context enrichment bag. Keeping this in one place is the DRY anchor — the
    // "where does a property live" decision is made exactly once.
    private static bool TryResolveValue(LogEvent logEvent, string propertyName, out object? value)
    {
        if (logEvent.GetProperty(propertyName) is { } property)
        {
            value = property.ResolvedValue;
            return true;
        }

        return logEvent.Context.TryGetValue(propertyName, out value);
    }
}

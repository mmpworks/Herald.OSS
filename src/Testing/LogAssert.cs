#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using MMP.Herald.Events;
using MMP.Herald.Levels;

namespace MMP.Herald.Testing;

/// <summary>
/// Static assertion helpers for verifying log output in unit tests.
/// Each method throws LogAssertionException with a descriptive message on failure.
/// </summary>
public static class LogAssert
{
    public static void ContainsMessage(IReadOnlyList<LogEvent> events, string substring)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(substring);

        if (!events.Any(e => e.Message.Contains(substring, StringComparison.OrdinalIgnoreCase)))
        {
            throw new LogAssertionException(
                $"Expected at least one event with message containing '{substring}', " +
                $"but none of the {events.Count} event(s) matched.");
        }
    }

    public static void ContainsLevel(IReadOnlyList<LogEvent> events, LogLevel level)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(level);

        if (!events.Any(e => e.Level == level))
        {
            throw new LogAssertionException(
                $"Expected at least one event at level '{level.DisplayName}', " +
                $"but none of the {events.Count} event(s) matched.");
        }
    }

    public static void ContainsCategory(IReadOnlyList<LogEvent> events, LogCategory category)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(category);

        if (!events.Any(e => e.Category == category))
        {
            throw new LogAssertionException(
                $"Expected at least one event with category '{category.Value}', " +
                $"but none of the {events.Count} event(s) matched.");
        }
    }

    public static void HasCount(IReadOnlyList<LogEvent> events, int expected)
    {
        ArgumentNullException.ThrowIfNull(events);

        if (events.Count != expected)
        {
            throw new LogAssertionException(
                $"Expected {expected} event(s), but found {events.Count}.");
        }
    }

    public static void HasProperty(
        IReadOnlyList<LogEvent> events,
        string propertyName,
        object? expectedValue = null)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(propertyName);

        var hasProperty = events.Any(e =>
            e.Properties.Any(p =>
                p.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase) &&
                (expectedValue is null || Equals(p.Value, expectedValue))));

        if (!hasProperty)
        {
            var valueClause = expectedValue is not null ? $" with value '{expectedValue}'" : "";
            throw new LogAssertionException(
                $"Expected at least one event with property '{propertyName}'{valueClause}, " +
                $"but none of the {events.Count} event(s) matched.");
        }
    }

    public static void NoneMatch(IReadOnlyList<LogEvent> events, Func<LogEvent, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(predicate);

        var match = events.FirstOrDefault(predicate);

        if (match is not null)
        {
            throw new LogAssertionException(
                $"Expected no events to match the predicate, but found: " +
                $"[{match.Level.DisplayName}] {match.Category.Value}: {match.Message}");
        }
    }

    public static void AllMatch(IReadOnlyList<LogEvent> events, Func<LogEvent, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(predicate);

        var nonMatch = events.FirstOrDefault(e => !predicate(e));

        if (nonMatch is not null)
        {
            throw new LogAssertionException(
                $"Expected all events to match the predicate, but this one did not: " +
                $"[{nonMatch.Level.DisplayName}] {nonMatch.Category.Value}: {nonMatch.Message}");
        }
    }
}

#nullable enable

// Minimal helper for SSE wire level-key regression tests.
// Pattern lifted from LiveLogEntryWireLevelKeyTests.MakeEvent —
// do NOT alter the construction shape; it must match what the SSE path does.

using System.Text.Json;
using MMP.Herald.Addons.ManagementApi;
using MMP.Herald.Events;
using MMP.Herald.Levels;

namespace MMP.Herald.OSS.Tests.Infrastructure;

/// <summary>
/// Creates <see cref="LiveLogEntry"/> instances from a <see cref="LogLevel"/>
/// and JSON-serializes them using the same camelCase policy that the SSE path
/// uses. Returns the raw JSON string for wire-key assertion.
/// </summary>
internal static class SseLevelKeyHarness
{
    private static readonly JsonSerializerOptions CamelCaseOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// Builds a minimal <see cref="LogEvent"/> for <paramref name="level"/>,
    /// wraps it in a <see cref="LiveLogEntry"/>, and returns the camelCase
    /// JSON string. This is the exact serialization shape the SSE broadcast path
    /// uses (see <c>LiveLogCapture</c>).
    /// </summary>
    public static string SerializeEntryJson(LogLevel level)
    {
        var logEvent = BuildEvent(level);
        var entry = LiveLogEntry.FromLogEvent(id: 1, logEvent);
        return JsonSerializer.Serialize(entry, CamelCaseOptions);
    }

    /// <summary>
    /// Constructs a minimal <see cref="LogEvent"/> at <paramref name="level"/>
    /// with a fixed test message. Pattern is identical to
    /// <c>LiveLogEntryWireLevelKeyTests.MakeEvent</c> — kept in sync so both
    /// suites exercise the same construction path.
    /// </summary>
    internal static LogEvent BuildEvent(LogLevel level) =>
        new(
            TimeUtc: System.DateTimeOffset.UtcNow,
            Level: level,
            Category: LogCategory.App,
            MessageTemplate: "test message",
            Message: "test message",
            Properties: LogEvent.EmptyProperties,
            Context: LogEvent.EmptyContext);
}

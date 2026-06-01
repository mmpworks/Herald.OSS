#nullable enable

using System;
using System.Collections.Generic;
using MMP.Herald.Events;
using MMP.Herald.Levels;
using MMP.Herald.Templating;

namespace Herald.OSS.Serilog.Expressions.Tests;

/// <summary>
/// Builds <see cref="LogEvent"/> fixtures for the corpus. Mirrors the minimal
/// event shape Serilog.Expressions tests assume: a level, a message, a
/// timestamp, an optional exception text, and a bag of properties.
/// </summary>
internal static class EventBuilder
{
    public static LogEvent Build(
        LogLevel? level = null,
        string message = "",
        string? exception = null,
        IReadOnlyList<(string Name, object? Value)>? properties = null)
    {
        var props = new List<LogProperty>();
        if (properties is not null)
        {
            foreach (var (name, value) in properties)
                props.Add(new LogProperty(name, value));
        }

        return new LogEvent(
            TimeUtc: new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero),
            Level: level ?? KnownLogLevels.Information,
            Category: LogCategory.None,
            MessageTemplate: message,
            Message: message,
            Properties: props,
            Context: LogEvent.EmptyContext,
            CausedBy: exception);
    }

    public static LogLevel Level(string key) => key.ToLowerInvariant() switch
    {
        "verbose" => KnownLogLevels.Verbose,
        "debug" => KnownLogLevels.Debug,
        "information" => KnownLogLevels.Information,
        "warning" => KnownLogLevels.Warning,
        "error" => KnownLogLevels.Error,
        "fatal" => KnownLogLevels.Fatal,
        "notice" => KnownLogLevels.Notice,
        "success" => KnownLogLevels.Success,
        "security" => KnownLogLevels.Security,
        "metric" => KnownLogLevels.Metric,
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, "unknown level key"),
    };
}

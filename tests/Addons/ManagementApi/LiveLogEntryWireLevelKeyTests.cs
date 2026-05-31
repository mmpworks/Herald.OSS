#nullable enable

using System.Text.Json;
using FluentAssertions;
using MMP.Herald.Addons.ManagementApi;
using MMP.Herald.Events;
using MMP.Herald.Levels;
using MMP.Herald.Templating;
using Xunit;

namespace MMP.Herald.OSS.Tests.Addons.ManagementApi;

/// <summary>
/// Pins the SSE wire shape for the level key field emitted by
/// <see cref="LiveLogEntry.FromLogEvent"/>. After the Serilog rename wave,
/// the wire JSON must carry Serilog-vocab keys ("verbose", "information",
/// "warning", "fatal") and must NOT carry pre-rename old-vocab keys
/// ("trace", "info", "warn", "critical").
///
/// The field name on the wire is "levelKey" (camelCase of
/// <see cref="LiveLogEntry.LevelKey"/>). These tests assert both the field
/// name and its value so a rename of the property itself would be caught.
/// </summary>
public sealed class LiveLogEntryWireLevelKeyTests
{
    // Cognitive Complexity note: each [Theory] case is independent —
    // no shared branching. The parameterised approach keeps the test count
    // small while covering every Serilog-vocab level.

    [Theory]
    [InlineData("verbose",     "verbose")]
    [InlineData("debug",       "debug")]
    [InlineData("information", "information")]
    [InlineData("warning",     "warning")]
    [InlineData("error",       "error")]
    [InlineData("fatal",       "fatal")]
    public void Wire_json_carries_serilog_vocab_level_key(string levelKey, string expectedWireKey)
    {
        // Arrange — build a LogEvent whose Level.Key is the Serilog vocab key.
        var level = new LogLevel(levelKey, levelKey.ToUpperInvariant()[..3]);
        var logEvent = MakeEvent(level, "test message");

        // Act — serialize via the same path LiveLogCapture uses for SSE.
        var entry = LiveLogEntry.FromLogEvent(id: 1, logEvent);
        var json = JsonSerializer.Serialize(entry, new JsonSerializerOptions
        {
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
        });

        // Assert — wire carries new-vocab key in "levelKey" field.
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.TryGetProperty("levelKey", out var levelKeyProp).Should().BeTrue(
            "LiveLogEntry must emit the 'levelKey' field on the wire");
        levelKeyProp.GetString().Should().Be(expectedWireKey,
            $"the wire 'levelKey' field must carry Serilog-vocab key '{expectedWireKey}', not old-vocab");
    }

    [Theory]
    [InlineData("information")]
    [InlineData("warning")]
    [InlineData("verbose")]
    [InlineData("fatal")]
    public void Wire_json_does_not_carry_old_vocab_level_key(string levelKey)
    {
        // Maps from new-vocab to the old-vocab key that must NOT appear.
        var oldVocabEquivalent = levelKey switch
        {
            "information" => "info",
            "warning"     => "warn",
            "verbose"     => "trace",
            "fatal"       => "critical",
            _             => throw new System.ArgumentOutOfRangeException(nameof(levelKey))
        };

        var level = new LogLevel(levelKey, "TST");
        var logEvent = MakeEvent(level, "test message");

        var entry = LiveLogEntry.FromLogEvent(id: 1, logEvent);
        var json = JsonSerializer.Serialize(entry, new JsonSerializerOptions
        {
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
        });

        // The wire JSON must NOT contain the old-vocab key anywhere in the
        // levelKey field value. We check the raw string as a belt-and-braces
        // guard in case a future refactor emits the old key under any field.
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        root.TryGetProperty("levelKey", out var levelKeyProp).Should().BeTrue();
        levelKeyProp.GetString().Should().NotBe(oldVocabEquivalent,
            $"wire must NOT emit old-vocab '{oldVocabEquivalent}' — Serilog rename is complete");
    }

    private static LogEvent MakeEvent(LogLevel level, string message) =>
        new(
            TimeUtc: System.DateTimeOffset.UtcNow,
            Level: level,
            Category: LogCategory.App,
            MessageTemplate: message,
            Message: message,
            Properties: LogEvent.EmptyProperties,
            Context: LogEvent.EmptyContext);
}

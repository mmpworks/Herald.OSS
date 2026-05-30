#nullable enable

// G-LEVEL.2 + G-LEVEL.3 — SSE wire level key regression pins.
// Verifies the SSE wire emits new-vocab keys and does NOT emit old-vocab keys.
// These complement the LevelRenameRegressionTests (G-LEVEL.1/4/5) which
// verify the registry; these verify the wire (broadcast) shape.

using FluentAssertions;
using MMP.Herald.Levels;
using MMP.Herald.OSS.Tests.Infrastructure;
using Xunit;

namespace MMP.Herald.OSS.Tests.Wire;

/// <summary>
/// G-LEVEL.2 + G-LEVEL.3 — pins that the SSE wire emits Serilog-vocab level
/// keys and never the pre-rename old-vocab keys.
///
/// <para>
/// The helper <see cref="SseLevelKeyHarness"/> replicates the exact construction
/// and serialization path used by <c>LiveLogCapture</c> for SSE broadcasting,
/// so a regression here reflects a real wire-format break.
/// </para>
/// </summary>
public sealed class SseLevelKeyRegressionTests
{
    // ── G-LEVEL.2 — SSE emits new-vocab level key ───────────────────────────────

    /// <summary>
    /// For every Serilog-vocab level key the wire JSON must carry
    /// <c>"levelKey":"&lt;key&gt;"</c>. This pins the field name AND value.
    /// </summary>
    [Theory]
    [InlineData("verbose")]
    [InlineData("debug")]
    [InlineData("information")]
    [InlineData("warning")]
    [InlineData("error")]
    [InlineData("fatal")]
    public void Sse_emits_new_vocab_level_key(string levelKey)
    {
        // Arrange — construct a LogLevel whose Key is already the new-vocab key.
        // The display abbreviation (first 3 chars, uppercased) is a plausible
        // but arbitrary choice; it does not affect the wire levelKey field.
        var level = new LogLevel(levelKey, levelKey.ToUpperInvariant()[..3]);

        // Act
        var json = SseLevelKeyHarness.SerializeEntryJson(level);

        // Assert — the wire JSON must contain the field with the new-vocab value.
        json.Should().Contain($"\"levelKey\":\"{levelKey}\"",
            $"the SSE wire must emit 'levelKey' carrying Serilog-vocab key '{levelKey}'");
    }

    // ── G-LEVEL.3 — SSE does NOT emit old-vocab level key ──────────────────────

    /// <summary>
    /// Even if an event arrives with an old-key level object, the wire must not
    /// carry the old-vocab key. In the real pipeline the alias map resolves old
    /// keys before they reach <c>LiveLogCapture</c>; this test pins the wire as
    /// new-vocab-only by constructing the level with the resolved (new) key and
    /// asserting the old key is absent from the output.
    ///
    /// <para>
    /// The test does not simulate alias-map resolution itself — that is covered
    /// by G-LEVEL.1. Instead it verifies that once resolved to a new-vocab key,
    /// the wire JSON contains only the new key, never the old one.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("info",     "information")]
    [InlineData("warn",     "warning")]
    [InlineData("critical", "fatal")]
    [InlineData("trace",    "verbose")]
    public void Sse_does_NOT_emit_old_vocab_level_key(string oldKey, string newKey)
    {
        // Build the level with the NEW vocab key (what the alias map resolves to).
        var level = new LogLevel(newKey, newKey.ToUpperInvariant()[..3]);

        var json = SseLevelKeyHarness.SerializeEntryJson(level);

        // The wire must carry the new key…
        json.Should().Contain($"\"levelKey\":\"{newKey}\"",
            $"the SSE wire must emit the resolved new-vocab key '{newKey}'");

        // …and must NOT carry the old key anywhere in the levelKey value.
        json.Should().NotContain($"\"levelKey\":\"{oldKey}\"",
            $"the SSE wire must NOT emit old-vocab key '{oldKey}' — the Serilog rename is complete");
    }
}

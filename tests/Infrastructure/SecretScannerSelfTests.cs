#nullable enable

using MMP.Herald.Levels;
using MMP.Herald.OSS.Tests.Infrastructure;
using MMP.Herald.Serilog.Events;
using Xunit;
using HeraldLogCategory = MMP.Herald.Events.LogCategory;

namespace MMP.Herald.OSS.Tests.Infrastructure;

/// <summary>
/// Self-tests that pin <see cref="SecretScanner"/> as honest.
///
/// <para>
/// A scanner that always returns empty is worse than no scanner — it gives false
/// confidence that redaction passed. These two tests gate the fixture itself:
/// </para>
/// <list type="bullet">
///   <item>Positive: the secret IS in the event → scanner must find it.</item>
///   <item>Negative: the secret was stripped → scanner must return empty.</item>
/// </list>
/// </summary>
public sealed class SecretScannerSelfTests
{
    // Canonical secret shared with SecretBearingFixture.
    private const string Secret = "hunter2";

    // Build a minimal Serilog mirror LogEvent that holds a single named property.
    private static MMP.Herald.Serilog.Events.LogEvent BuildEventWithProperty(
        string name, LogEventPropertyValue value)
    {
        var native = new MMP.Herald.Events.LogEvent(
            TimeUtc: System.DateTimeOffset.UtcNow,
            Level: KnownLogLevels.Information,
            Category: HeraldLogCategory.App,
            MessageTemplate: "test {Msg}",
            Message: "test",
            Properties: MMP.Herald.Events.LogEvent.EmptyProperties,
            Context: MMP.Herald.Events.LogEvent.EmptyContext);

        var mirror = new MMP.Herald.Serilog.Events.LogEvent(native);
        mirror.AddOrUpdateProperty(new LogEventProperty(name, value));
        return mirror;
    }

    [Fact]
    public void Scanner_finds_secret_in_scalar_property()
    {
        // Arrange: event has a scalar property whose value IS the secret.
        var evt = BuildEventWithProperty("Password", new ScalarValue(Secret));

        // Act
        var found = SecretScanner.FindAll(evt, Secret);

        // Assert: scanner must report at least one location — pin it as non-vacuous.
        Assert.NotEmpty(found);
    }

    [Fact]
    public void Scanner_finds_nothing_when_property_holds_different_value()
    {
        // Arrange: event has a scalar property whose value is NOT the secret.
        // This models the post-redaction state where the secret was stripped and
        // replaced with a benign marker (e.g. "alice", a redacted placeholder, etc.).
        var evt = BuildEventWithProperty("Password", new ScalarValue("alice"));

        // Act
        var found = SecretScanner.FindAll(evt, Secret);

        // Assert: scanner must return empty — "alice" does not contain "hunter2".
        Assert.Empty(found);
    }

    [Fact]
    public void Scanner_finds_secret_nested_in_structure_value()
    {
        // Arrange: secret is nested inside a StructureValue child, not top-level.
        // This is the exact scenario G-SEC.1 guards — a field-name check would miss it.
        var inner = new StructureValue(new[]
        {
            new LogEventProperty("Password", new ScalarValue(Secret)),
            new LogEventProperty("Name", new ScalarValue("alice")),
        });
        var evt = BuildEventWithProperty("User", inner);

        // Act
        var found = SecretScanner.FindAll(evt, Secret);

        // Assert: scanner must descend into StructureValue children.
        Assert.NotEmpty(found);
    }

    [Fact]
    public void Scanner_returns_empty_for_event_with_no_secret()
    {
        // Arrange: completely benign event — no property, message, or exception
        // contains the secret string.
        var evt = BuildEventWithProperty("Name", new ScalarValue("alice"));

        // Act
        var found = SecretScanner.FindAll(evt, Secret);

        // Assert
        Assert.Empty(found);
    }
}


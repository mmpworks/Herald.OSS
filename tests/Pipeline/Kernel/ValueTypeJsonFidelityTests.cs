#nullable enable

using System;
using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using MMP.Herald.Events;
using MMP.Herald.Formatting;
using MMP.Herald.Levels;
using MMP.Herald.Pipeline.Kernel;
using Xunit;

namespace MMP.Herald.OSS.Tests.Pipeline.Kernel;

/// <summary>
/// Pins the JSON rendering of the four Phase 1 value types as they leave the
/// compact fast path through <see cref="Utf8JsonFormatter"/>. The contract is
/// typed fidelity:
/// <list type="bullet">
///   <item>decimal renders as a JSON <b>number</b> (not a quoted string), full precision;</item>
///   <item>Guid renders as its canonical 36-char string;</item>
///   <item>DateTimeOffset renders as an ISO-8601 ("O") string;</item>
///   <item>TimeSpan renders as its standard ("c") string.</item>
/// </list>
///
/// <para>
/// The buffer is filled with compact properties (the typed fast path the
/// kernel produces) and formatted directly, so the test exercises the exact
/// switch arms added in Phase 1 — not the lazy Value/ToString fallback.
/// </para>
/// </summary>
public sealed class ValueTypeJsonFidelityTests
{
    private static readonly Utf8JsonFormatter Formatter =
        new(LogLevelRegistry.CreateDefault());

    /// <summary>
    /// Formats a single compact property into a JSON document and returns the
    /// <c>properties.{name}.value</c> element for assertion.
    /// </summary>
    private static JsonElement FormatValueElement(string name, LogPropertyCompact prop)
    {
        // LogPropertyCompact is a managed type (it carries reference fields), so
        // it can't be stackalloc'd. The codebase's InlineArray buffer gives the
        // same stack-only single-slot span.
        var slot = new LogPropertyBuffer1();
        slot[0] = prop;

        var buffer = new LogEventBuffer(
            timeUtc: DateTimeOffset.UnixEpoch,
            level: KnownLogLevels.Information,
            category: LogCategory.App,
            messageTemplate: "t {" + name + "}",
            message: string.Empty,
            compactProperties: ((ReadOnlySpan<LogPropertyCompact>)slot));

        var output = new ArrayBufferWriter<byte>();
        Formatter.Format(in buffer, output);

        var json = Encoding.UTF8.GetString(output.WrittenSpan);
        using var doc = JsonDocument.Parse(json);
        // Clone so the element survives the using-scope dispose.
        return doc.RootElement
            .GetProperty("properties")
            .GetProperty(name)
            .GetProperty("value")
            .Clone();
    }

    [Fact]
    public void Decimal_renders_as_json_number()
    {
        decimal amount = 1234.5678m;
        var element = FormatValueElement("amt", LogPropertyCompact.From("amt", amount));

        element.ValueKind.Should().Be(JsonValueKind.Number,
            "decimal must render as a JSON number, not a quoted string — " +
            "numeric-aware sinks (Splunk/Loki/ES, OTLP) depend on it");
        element.GetDecimal().Should().Be(amount, "the number must preserve full decimal precision");
    }

    [Fact]
    public void Guid_renders_as_canonical_string()
    {
        var id = Guid.NewGuid();
        var element = FormatValueElement("id", LogPropertyCompact.From("id", id));

        element.ValueKind.Should().Be(JsonValueKind.String);
        element.GetString().Should().Be(id.ToString("D"),
            "Guid must render as its canonical 36-char hyphenated form");
    }

    [Fact]
    public void DateTimeOffset_renders_as_iso8601_string()
    {
        var dto = new DateTimeOffset(2026, 6, 1, 12, 34, 56, TimeSpan.FromHours(2));
        var element = FormatValueElement("when", LogPropertyCompact.From("when", dto));

        element.ValueKind.Should().Be(JsonValueKind.String);
        element.GetString().Should().Be(dto.ToString("O", CultureInfo.InvariantCulture),
            "DateTimeOffset must render as a round-trippable ISO-8601 string with offset");
    }

    [Fact]
    public void TimeSpan_renders_as_standard_string()
    {
        var ts = new TimeSpan(1, 2, 3, 4, 5);
        var element = FormatValueElement("dur", LogPropertyCompact.From("dur", ts));

        element.ValueKind.Should().Be(JsonValueKind.String);
        element.GetString().Should().Be(ts.ToString("c", CultureInfo.InvariantCulture),
            "TimeSpan must render in its standard constant ('c') form");
    }

    [Fact]
    public void Decimal_value_round_trips_through_json()
    {
        // Full mantissa/scale value to confirm WriteNumber doesn't truncate.
        decimal amount = 79228162514264337593543950335m; // decimal.MaxValue
        var element = FormatValueElement("max", LogPropertyCompact.From("max", decimal.MaxValue));

        element.GetDecimal().Should().Be(amount, "decimal.MaxValue must survive the JSON round-trip");
    }
}

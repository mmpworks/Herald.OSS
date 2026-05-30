#nullable enable
// Test double for ISerilogEventView — usable only in the test project.
#if NET9_0_OR_GREATER

using System;
using System.Collections.Generic;
using MMP.Herald.Serilog.Events;
using MMP.Herald.Serilog.Formatting;

namespace MMP.Herald.OSS.Tests.TestSupport;

/// <summary>
/// Lightweight test double for <see cref="ISerilogEventView"/> so renderer tests
/// (P3 Tasks 2–6) and CLEF formatter tests (Tasks 7–8) can run against controlled
/// inputs before the P1 concrete mirror is fully exercised.
///
/// <para>
/// All properties are settable via object-initializer syntax. The defaults produce
/// a minimal valid event (Information-level, empty message, no properties, no exception)
/// that does not throw on any renderer path.
/// </para>
/// </summary>
public sealed class FakeSerilogEventView : ISerilogEventView
{
    public DateTimeOffset Timestamp { get; init; } =
        new DateTimeOffset(2024, 6, 15, 14, 30, 0, TimeSpan.FromHours(5));

    public LogEventLevel Level { get; init; } = LogEventLevel.Information;

    public string MessageTemplate { get; init; } = "";

    public string RenderedMessage { get; init; } = "";

    public IReadOnlyDictionary<string, LogEventPropertyValue> Properties { get; init; } =
        new Dictionary<string, LogEventPropertyValue>(StringComparer.Ordinal);

    public Exception? Exception { get; init; }

    public IReadOnlyList<string> TemplateHoleNames { get; init; } = Array.Empty<string>();

    // ── Factory helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// Create a fake view with simple scalar properties. Useful for
    /// token-renderer tests that only need name→value pairs without a
    /// full destructured tree.
    /// </summary>
    public static FakeSerilogEventView WithScalars(
        string messageTemplate,
        string renderedMessage,
        LogEventLevel level = LogEventLevel.Information,
        params (string Name, object? Value)[] properties)
    {
        var propDict = new Dictionary<string, LogEventPropertyValue>(
            StringComparer.Ordinal);
        var holeNames = new List<string>(properties.Length);

        foreach (var (name, value) in properties)
        {
            propDict[name] = new ScalarValue(value);
            holeNames.Add(name);
        }

        return new FakeSerilogEventView
        {
            Level            = level,
            MessageTemplate  = messageTemplate,
            RenderedMessage  = renderedMessage,
            Properties       = propDict,
            TemplateHoleNames = holeNames,
        };
    }
}

#endif

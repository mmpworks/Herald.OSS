#nullable enable

using System;
using System.Collections.Generic;
using MMP.Herald.Events;
using MMP.Herald.Pipeline;
using MMP.Herald.Templating;

namespace MMP.Herald.Addons.QualityChecks;

/// <summary>
/// Schema definition for a log event type. Associates a LogEventId with
/// its expected properties so events can be validated at runtime.
/// </summary>
public sealed record LogEventSchema(
    LogEventId EventId,
    IReadOnlyList<string> RequiredProperties,
    IReadOnlyList<string>? OptionalProperties = null)
{
    public override string ToString() =>
        $"{EventId}: required=[{string.Join(",", RequiredProperties)}]";
}

/// <summary>
/// Registry of known log event schemas. Maps LogEventId to expected property names.
/// When used as an event processor, validates that events carrying a registered
/// EventId include all required properties and flags violations with a silent property.
///
/// Stripe Principle #4 (Mandatory Structured JSON): enforce that business-critical
/// events always carry the expected structured data, not ad-hoc text.
///
/// Usage:
///   var registry = new LogSchemaRegistry()
///       .Register(GameEvents.CombatHit, required: ["attacker", "damage", "target"])
///       .Register(GameEvents.QuestComplete, required: ["questId", "playerId"],
///           optional: ["duration"]);
///
///   builder.WithEventProcessor("schemaValidator", registry);
///
/// Violations are flagged with a silent _schemaViolation property containing
/// the missing property names. Events always pass through -- this is advisory,
/// not blocking. Use the callback for real-time reporting during development.
/// </summary>
public sealed class LogSchemaRegistry : ILogEventProcessor
{
    private readonly Dictionary<int, LogEventSchema> _schemas = new();
    private readonly Action<LogEventId, IReadOnlyList<string>>? _onViolation;
    private long _violationCount;

    /// <param name="onViolation">Optional callback invoked with (eventId, missingProperties) on violations.</param>
    public LogSchemaRegistry(Action<LogEventId, IReadOnlyList<string>>? onViolation = null)
    {
        _onViolation = onViolation;
    }

    /// <summary>Register a schema for an event type.</summary>
    public LogSchemaRegistry Register(
        LogEventId eventId,
        IReadOnlyList<string> required,
        IReadOnlyList<string>? optional = null)
    {
        ArgumentNullException.ThrowIfNull(eventId);
        ArgumentNullException.ThrowIfNull(required);
        _schemas[eventId.Id] = new LogEventSchema(eventId, required, optional);
        return this;
    }

    /// <summary>Convenience: register with params syntax.</summary>
    public LogSchemaRegistry Register(LogEventId eventId, params string[] required)
    {
        return Register(eventId, (IReadOnlyList<string>)required);
    }

    public LogEvent? Process(LogEvent logEvent)
    {
        if (logEvent.EventId is null)
            return logEvent;

        if (!_schemas.TryGetValue(logEvent.EventId.Id, out var schema))
            return logEvent;

        var missing = FindMissing(logEvent, schema.RequiredProperties);

        if (missing.Count == 0)
            return logEvent;

        System.Threading.Interlocked.Increment(ref _violationCount);
        _onViolation?.Invoke(logEvent.EventId, missing);

        // Flag the violation as a silent property
        var flagged = new LogProperty[logEvent.Properties.Count + 1];
        for (var i = 0; i < logEvent.Properties.Count; i++)
            flagged[i] = logEvent.Properties[i];
        flagged[^1] = LogProperty.Silent("_schemaViolation",
            $"Missing: {string.Join(", ", missing)}");

        return logEvent with { Properties = flagged };
    }

    /// <summary>Total schema violations detected since creation.</summary>
    public long ViolationCount => System.Threading.Interlocked.Read(ref _violationCount);

    /// <summary>All registered schemas.</summary>
    public IReadOnlyCollection<LogEventSchema> Schemas => _schemas.Values;

    /// <summary>Check if a schema is registered for an event ID.</summary>
    public bool HasSchema(LogEventId eventId) => _schemas.ContainsKey(eventId.Id);

    private static List<string> FindMissing(LogEvent logEvent, IReadOnlyList<string> required)
    {
        var missing = new List<string>();

        foreach (var name in required)
        {
            if (!logEvent.HasProperty(name))
                missing.Add(name);
        }

        return missing;
    }
}

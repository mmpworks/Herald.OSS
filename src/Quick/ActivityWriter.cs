#nullable enable

using System.Collections.Generic;
using MMP.Herald.Events;
using MMP.Herald.Levels;
using MMP.Herald.Pipeline;
using MMP.Herald.Templating;

namespace MMP.Herald.Quick;

/// <summary>
/// Domain proxy that wraps a StructuredLogger with a baked-in channel context.
/// Provides domain-friendly method names instead of log levels.
/// Events are stamped with context["channel"] for predicate-based routing
/// to channel-specific sinks.
///
/// Usage:
///   var writer = new ActivityWriter(logger, "conversation");
///   writer.Activity("Mark pleads for help finding {npcId}", new LogProperty("npcId", "Joey"));
/// </summary>
public sealed class ActivityWriter
{
    private readonly StructuredLogger _logger;
    private readonly string _channel;

    /// <summary>
    /// Creates an ActivityWriter that routes events to a named channel.
    /// </summary>
    /// <param name="logger">The underlying structured logger.</param>
    /// <param name="channel">Channel name used for routing (e.g., "conversation").</param>
    public ActivityWriter(StructuredLogger logger, string channel)
    {
        _channel = channel;
        _logger = logger.WithContext(new Dictionary<string, object?>
        {
            ["channel"] = channel
        });
    }

    /// <summary>
    /// The channel name this writer routes to.
    /// </summary>
    public string Channel => _channel;

    /// <summary>
    /// General activity - something happened.
    /// Maps to Info level internally.
    /// </summary>
    public void Activity(string messageTemplate, params LogProperty[] properties)
    {
        _logger.Information(LogCategory.App, messageTemplate,
            properties: properties.Length > 0 ? properties : null);
    }

    /// <summary>
    /// A state value changed (trust, mood, location, etc.).
    /// Maps to Info level internally.
    /// </summary>
    public void StateChange(string messageTemplate, params LogProperty[] properties)
    {
        _logger.Information(LogCategory.App, messageTemplate,
            properties: properties.Length > 0 ? properties : null);
    }

    /// <summary>
    /// A notable transition occurred (tier promotion, overlay applied, quest state change).
    /// Maps to Warn level internally for visibility.
    /// </summary>
    public void Transition(string messageTemplate, params LogProperty[] properties)
    {
        _logger.Warning(LogCategory.App, messageTemplate,
            properties: properties.Length > 0 ? properties : null);
    }

    /// <summary>
    /// An engine decision was made (response mode selected, fact gated, etc.).
    /// Maps to Debug level internally.
    /// </summary>
    public void Decision(string messageTemplate, params LogProperty[] properties)
    {
        _logger.Debug(LogCategory.App, messageTemplate,
            properties: properties.Length > 0 ? properties : null);
    }

    /// <summary>
    /// A final outcome (quest completed, quest failed, NPC died).
    /// Maps to Info level internally.
    /// </summary>
    public void Outcome(string messageTemplate, params LogProperty[] properties)
    {
        _logger.Information(LogCategory.App, messageTemplate,
            properties: properties.Length > 0 ? properties : null);
    }

    /// <summary>
    /// An audit event that bypasses minimum level filtering.
    /// Always recorded regardless of the global or per-sink minimum level.
    /// Use for compliance, security, and events that must never be silenced.
    /// Maps to Info level internally, stamped with context["audit"] = "true".
    /// </summary>
    public void Audit(string messageTemplate, params LogProperty[] properties)
    {
        _logger.Information(LogCategory.App, messageTemplate,
            context: AuditContext,
            properties: properties.Length > 0 ? properties : null);
    }

    private static readonly IReadOnlyDictionary<string, object?> AuditContext =
        new Dictionary<string, object?> { [Services.LogContextKeys.Audit] = "true" };
}

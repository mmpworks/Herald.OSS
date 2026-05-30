#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace MMP.Herald.Serilog.Events;

/// <summary>
/// Serilog-shaped mirror of a Herald LogEvent. Wraps the native event
/// with lazy property projection on first read.
/// Guard 1: only LogEventValueProjector constructs this from a native event.
/// Level extras: security->Warning, others->Information (S-1). True level
/// preserved in Properties as HeraldLevel.
/// </summary>
public sealed class LogEvent
{
    private readonly MMP.Herald.Events.LogEvent _native;
    private Dictionary<string, LogEventPropertyValue>? _projected;

    internal LogEvent(MMP.Herald.Events.LogEvent native)
    {
        ArgumentNullException.ThrowIfNull(native);
        _native = native;
    }
    /// Timestamp: converts UTC to local time (Serilog defaults local, S-2).
    public DateTimeOffset Timestamp => _native.TimeUtc.ToLocalTime();

    /// Serilog level mapping. security->Warning, other extras->Information.
    /// Never throws (S-1 ruling).
    public LogEventLevel Level
    {
        get
        {
            if (MMP.Herald.Serilog.SerilogLevelMap.TryToSerilog(_native.Level, out var mapped))
                return mapped;
            return _native.Level.Key == "security"
                ? LogEventLevel.Warning
                : LogEventLevel.Information;
        }
    }

    public string MessageTemplate => _native.MessageTemplate ?? string.Empty;

    public string RenderMessage() => _native.Message ?? MessageTemplate;

    public Exception? Exception =>
        _native.Context.TryGetValue(MMP.Herald.Services.LogContextKeys.Exception, out var ex)
            ? ex as Exception
            : null;
    /// Projected properties. Lazy build-once, mutable thereafter (Seam C).
    public IReadOnlyDictionary<string, LogEventPropertyValue> Properties
        => EnsureProjected();

    /// Add or replace a property (for custom Serilog enrichers -- S2 seam).
    public void AddOrUpdateProperty(LogEventProperty property)
    {
        ArgumentNullException.ThrowIfNull(property);
        EnsureProjected()[property.Name] = property.Value;
    }

    /// Add a property only if no property with that name already exists.
    public void AddPropertyIfAbsent(LogEventProperty property)
    {
        ArgumentNullException.ThrowIfNull(property);
        EnsureProjected().TryAdd(property.Name, property.Value);
    }

    /// Remove the property with the given name if present.
    public void RemovePropertyIfPresent(string propertyName)
    {
        ArgumentNullException.ThrowIfNull(propertyName);
        _projected?.Remove(propertyName);
    }
    // EnsureProjected is the intentional projection call site.
    // IL2026 suppressed here: the whole projection path is reflection-based by design;
    // the RequiresUnreferencedCode is declared on Project().
    [SuppressMessage("Trimming", "IL2026",
        Justification = "Projection path intentionally uses reflection. Hot path never calls this (Guard 2).")]
    private Dictionary<string, LogEventPropertyValue> EnsureProjected()
    {
        if (_projected is not null) return _projected;

        _projected = LogEventValueProjector.Project(_native);

        // S-1: preserve the true Herald level key when mapping to a Serilog approximation.
        if (!MMP.Herald.Serilog.SerilogLevelMap.TryToSerilog(_native.Level, out _))
            _projected.TryAdd("HeraldLevel", new ScalarValue(_native.Level.Key));

        return _projected;
    }
}

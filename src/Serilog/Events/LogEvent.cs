#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using MMP.Herald.Events;
using MMP.Herald.Serilog.Destructuring;

namespace MMP.Herald.Serilog.Events;

public sealed class LogEvent
{
    private readonly MMP.Herald.Events.LogEvent? _native;
    private readonly LogEventEnrichmentContext? _context;
    private readonly SerilogDestructuringApplicator? _applicator;
    private Dictionary<string, LogEventPropertyValue>? _projected;

    // Guard 1 site
    internal LogEvent(MMP.Herald.Events.LogEvent native)
    {
        ArgumentNullException.ThrowIfNull(native);
        _native = native;
    }

    // S5 seam: with Serilog policy applicator
    internal LogEvent(MMP.Herald.Events.LogEvent native, SerilogDestructuringApplicator applicator)
    {
        ArgumentNullException.ThrowIfNull(native);
        ArgumentNullException.ThrowIfNull(applicator);
        _native = native;
        _applicator = applicator;
    }

    // S2 seam: enrichment-time
    internal LogEvent(LogEventEnrichmentContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    public DateTimeOffset Timestamp => _native is not null
        ? _native.TimeUtc.ToLocalTime()
        : DateTimeOffset.UtcNow;

    public LogEventLevel Level
    {
        get
        {
            var level = _native?.Level ?? _context?.Level;
            if (level is null) return LogEventLevel.Information;
            if (MMP.Herald.Serilog.SerilogLevelMap.TryToSerilog(level, out var mapped))
                return mapped;
            return level.Key == "security"
                ? LogEventLevel.Warning
                : LogEventLevel.Information;
        }
    }

    public string MessageTemplate => _native?.MessageTemplate
        ?? _context?.MessageTemplate
        ?? string.Empty;

    public string RenderMessage() => _native?.Message ?? MessageTemplate;

    public Exception? Exception =>
        _native?.Context.TryGetValue(MMP.Herald.Services.LogContextKeys.Exception, out var ex) == true
            ? ex as Exception
            : null;

    public IReadOnlyDictionary<string, LogEventPropertyValue> Properties
        => EnsureProjected();

    [SuppressMessage("Trimming", "IL2026",
        Justification = "Enricher path is reflection-based by design.")]
    public void AddOrUpdateProperty(LogEventProperty property)
    {
        ArgumentNullException.ThrowIfNull(property);
        EnsureProjected()[property.Name] = property.Value;
        if (_context is not null)
        {
            var rawValue = property.Value is ScalarValue sv ? sv.Value : property.Value;
            _context.UpsertProperty(property.Name, rawValue);
        }
    }

    public void AddPropertyIfAbsent(LogEventProperty property)
    {
        ArgumentNullException.ThrowIfNull(property);
        EnsureProjected().TryAdd(property.Name, property.Value);
        if (_context is not null)
        {
            var alreadyPresent = false;
            foreach (var p in _context.Properties)
            {
                if (string.Equals(p.Name, property.Name, StringComparison.Ordinal))
                {
                    alreadyPresent = true;
                    break;
                }
            }
            if (!alreadyPresent)
            {
                var rawValue = property.Value is ScalarValue sv ? sv.Value : property.Value;
                _context.AddProperty(property.Name, rawValue);
            }
        }
    }

    public void RemovePropertyIfPresent(string propertyName)
    {
        ArgumentNullException.ThrowIfNull(propertyName);
        _projected?.Remove(propertyName);
    }

    [SuppressMessage("Trimming", "IL2026",
        Justification = "Projection path intentionally uses reflection (Guard 2).")]
    private Dictionary<string, LogEventPropertyValue> EnsureProjected()
    {
        if (_projected is not null) return _projected;

        if (_native is not null)
        {
            if (_applicator is not null && _applicator.HasPolicies)
                _projected = ProjectWithPolicies(_native, _applicator);
            else
                _projected = LogEventValueProjector.Project(_native);

            if (!MMP.Herald.Serilog.SerilogLevelMap.TryToSerilog(_native.Level, out _))
                _projected.TryAdd("HeraldLevel", new ScalarValue(_native.Level.Key));
        }
        else
        {
            _projected = new Dictionary<string, LogEventPropertyValue>(StringComparer.Ordinal);
            if (_context is not null)
            {
                foreach (var prop in _context.Properties)
                {
                    var mode = prop.CaptureModeOrDefault;
                    LogEventPropertyValue pv;
                    if (mode == MMP.Herald.Templating.LogPropertyCaptureMode.Destructure)
                        pv = LogEventValueProjector.DefaultValueFactory.CreatePropertyValue(prop.ResolvedValue, destructureObjects: true);
                    else if (mode == MMP.Herald.Templating.LogPropertyCaptureMode.Stringify)
                        pv = new ScalarValue(prop.ResolvedValue?.ToString());
                    else
                        pv = new ScalarValue(prop.ResolvedValue);
                    _projected.TryAdd(prop.Name, pv);
                }
            }
        }

        return _projected!;
    }

    [SuppressMessage("Trimming", "IL2026",
        Justification = "Projection path intentionally uses reflection.")]
    private static Dictionary<string, LogEventPropertyValue> ProjectWithPolicies(
        MMP.Herald.Events.LogEvent native,
        SerilogDestructuringApplicator applicator)
    {
        var result = new Dictionary<string, LogEventPropertyValue>(
            native.Properties.Count, StringComparer.Ordinal);

        foreach (var prop in native.Properties)
        {
            var rawValue = prop.ResolvedValue;
            var mode = prop.CaptureModeOrDefault;
            LogEventPropertyValue value;

            if (mode == MMP.Herald.Templating.LogPropertyCaptureMode.Destructure)
            {
                // Try Serilog policy chain first; fall back to default factory.
                var policyResult = applicator.Apply(rawValue);
                value = policyResult ?? LogEventValueProjector.DefaultValueFactory
                    .CreatePropertyValue(rawValue, destructureObjects: true);
            }
            else if (mode == MMP.Herald.Templating.LogPropertyCaptureMode.Stringify)
            {
                value = new ScalarValue(rawValue?.ToString());
            }
            else
            {
                value = new ScalarValue(rawValue);
            }

            result.TryAdd(prop.Name, value);
        }

        return result;
    }
}

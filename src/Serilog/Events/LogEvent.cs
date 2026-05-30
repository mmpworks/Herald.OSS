#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using MMP.Herald.Events;

namespace MMP.Herald.Serilog.Events;

/// <summary>
/// Serilog-shaped mirror of a Herald LogEvent. Wraps the native event
/// with lazy property projection on first read.
/// Guard 1: only LogEventValueProjector constructs this from a native event.
/// Level extras: security->Warning, others->Information (S-1). True level
/// preserved in Properties as HeraldLevel.
///
/// <para>
/// <b>Enrichment-time variant (S2 seam):</b> the internal constructor that
/// accepts a <see cref="LogEventEnrichmentContext"/> creates a lightweight
/// enrichment-time view used by <c>SerilogEnricherAdapter</c>. In this mode
/// <see cref="_native"/> is null and <see cref="_context"/> carries the
/// mutable context. <see cref="AddOrUpdateProperty"/> in this mode writes
/// directly to the context via <see cref="LogEventEnrichmentContext.UpsertProperty"/>
/// so manual property mutations (enrichers that do not use the factory shim)
/// are still forwarded to the native pipeline.
/// </para>
/// </summary>
public sealed class LogEvent
{
    private readonly MMP.Herald.Events.LogEvent? _native;
    private readonly LogEventEnrichmentContext? _context;
    private Dictionary<string, LogEventPropertyValue>? _projected;

    // ── Post-finalization constructor (Guard 1 site) ─────────────────────────
    internal LogEvent(MMP.Herald.Events.LogEvent native)
    {
        ArgumentNullException.ThrowIfNull(native);
        _native = native;
    }

    // ── Enrichment-time constructor (S2 seam) ────────────────────────────────
    // Used by SerilogEnricherAdapter. No native event exists yet; the context
    // is the live mutable event-creation state.
    internal LogEvent(LogEventEnrichmentContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    // ── Public surface ───────────────────────────────────────────────────────

    /// Timestamp: converts UTC to local time (Serilog defaults local, S-2).
    /// Enrichment-time variant returns UtcNow (event not yet stamped).
    public DateTimeOffset Timestamp => _native is not null
        ? _native.TimeUtc.ToLocalTime()
        : DateTimeOffset.UtcNow;

    /// Serilog level mapping. security->Warning, other extras->Information.
    /// Never throws (S-1 ruling).
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

    /// Projected properties. Lazy build-once, mutable thereafter (Seam C).
    public IReadOnlyDictionary<string, LogEventPropertyValue> Properties
        => EnsureProjected();

    /// Add or replace a property (for custom Serilog enrichers -- S2 seam).
    /// Enrichment-time variant: also forwards to the native context so properties
    /// added by enrichers without using the factory shim are not lost.
    [SuppressMessage("Trimming", "IL2026",
        Justification = "Enricher path is reflection-based by design; same suppression as EnsureProjected.")]
    public void AddOrUpdateProperty(LogEventProperty property)
    {
        ArgumentNullException.ThrowIfNull(property);
        EnsureProjected()[property.Name] = property.Value;

        // Enrichment-time: forward to native context. The factory shim also calls
        // context.AddProperty, so most enrichers are double-covered (harmless);
        // enrichers that bypass the factory and call AddOrUpdateProperty directly
        // would otherwise lose the mutation. UpsertProperty overwrites on duplicate.
        if (_context is not null)
        {
            // Map back from LogEventPropertyValue to raw object for the context.
            var rawValue = property.Value is ScalarValue sv ? sv.Value : property.Value;
            _context.UpsertProperty(property.Name, rawValue);
        }
    }

    /// Add a property only if no property with that name already exists.
    public void AddPropertyIfAbsent(LogEventProperty property)
    {
        ArgumentNullException.ThrowIfNull(property);
        EnsureProjected().TryAdd(property.Name, property.Value);

        // Enrichment-time: forward to native context only when not already present.
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

    /// Remove the property with the given name if present.
    public void RemovePropertyIfPresent(string propertyName)
    {
        ArgumentNullException.ThrowIfNull(propertyName);
        _projected?.Remove(propertyName);
        // Note: native LogProperty list has no remove operation. Enricher-time
        // property removal is an advanced pattern; documented as a known gap.
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    // EnsureProjected is the intentional projection call site.
    // IL2026 suppressed here: the whole projection path is reflection-based by design;
    // the RequiresUnreferencedCode is declared on Project().
    [SuppressMessage("Trimming", "IL2026",
        Justification = "Projection path intentionally uses reflection. Hot path never calls this (Guard 2).")]
    private Dictionary<string, LogEventPropertyValue> EnsureProjected()
    {
        if (_projected is not null) return _projected;

        if (_native is not null)
        {
            _projected = LogEventValueProjector.Project(_native);

            // S-1: preserve the true Herald level key when mapping to a Serilog approximation.
            if (!MMP.Herald.Serilog.SerilogLevelMap.TryToSerilog(_native.Level, out _))
                _projected.TryAdd("HeraldLevel", new ScalarValue(_native.Level.Key));
        }
        else
        {
            // Enrichment-time: start with properties already on the context.
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
}

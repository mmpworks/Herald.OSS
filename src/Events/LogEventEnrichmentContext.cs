#nullable enable

using System;
using System.Collections.Generic;
using MMP.Herald.Levels;
using MMP.Herald.Templating;

namespace MMP.Herald.Events;

/// <summary>
/// Mutable event creation model used by enrichers before the immutable
/// <see cref="LogEvent"/> is finalized.
/// </summary>
public sealed class LogEventEnrichmentContext
{
    private readonly List<LogProperty> _properties;
    private readonly Dictionary<string, object?> _context;

    public LogEventEnrichmentContext(
        LogLevel level,
        LogCategory category,
        string messageTemplate,
        IReadOnlyList<LogProperty>? properties,
        IReadOnlyDictionary<string, object?>? context) {
        Level = level ?? throw new ArgumentNullException(nameof(level));
        Category = category ?? throw new ArgumentNullException(nameof(category));
        MessageTemplate = messageTemplate ?? throw new ArgumentNullException(nameof(messageTemplate));
        _properties = properties is null ? [] : [.. properties];
        _context = context is null
            ? new Dictionary<string, object?>(StringComparer.Ordinal)
            : new Dictionary<string, object?>(context, StringComparer.Ordinal);
    }

    /// <summary>
    /// Internal constructor accepting pre-allocated collections from a pool.
    /// The extra bool parameter disambiguates from the public constructor when
    /// InternalsVisibleTo is active and callers pass null for collections.
    /// The caller is responsible for populating and returning the pooled collections.
    /// </summary>
    internal LogEventEnrichmentContext(
        LogLevel level,
        LogCategory category,
        string messageTemplate,
        List<LogProperty> pooledProperties,
        Dictionary<string, object?> pooledContext,
        bool pooled) {
        Level = level ?? throw new ArgumentNullException(nameof(level));
        Category = category ?? throw new ArgumentNullException(nameof(category));
        MessageTemplate = messageTemplate ?? throw new ArgumentNullException(nameof(messageTemplate));
        _properties = pooledProperties;
        _context = pooledContext;
        _ = pooled; // disambiguation marker only
    }

    public LogLevel Level { get; }

    public LogCategory Category { get; }

    public string MessageTemplate { get; }

    public IReadOnlyList<LogProperty> Properties => _properties;

    public IReadOnlyDictionary<string, object?> Context => _context;

    /// <summary>
    /// True if any enricher called SetContextValue during enrichment.
    /// Used by LogEventFactory to skip redundant context copying.
    /// </summary>
    public bool WasContextModified { get; private set; }

    public void SetContextValue(string name, object? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _context[name] = value;
        WasContextModified = true;
    }

    public void AddProperty(
        string name,
        object? value,
        LogPropertyCaptureMode? captureMode = null,
        string? format = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _properties.Add(new LogProperty(name, value, captureMode, format));
    }

    public void UpsertProperty(
        string name,
        object? value,
        LogPropertyCaptureMode? captureMode = null,
        string? format = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        for (var index = 0; index < _properties.Count; index += 1)
        {
            if (string.Equals(_properties[index].Name, name, StringComparison.Ordinal))
            {
                var existing = _properties[index];
                _properties[index] = new LogProperty(
                    name,
                    value,
                    captureMode ?? existing.CaptureMode,
                    format ?? existing.Format);
                return;
            }
        }

        _properties.Add(new LogProperty(name, value, captureMode, format));
    }
}
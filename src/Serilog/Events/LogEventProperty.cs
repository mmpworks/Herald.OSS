#nullable enable

using System;

namespace MMP.Herald.Serilog.Events;

/// <summary>
/// A named Serilog-shaped property — a name + value pair.
/// Consumer enrichers, formatters, and policies construct these directly;
/// the constructor must be public (InternalsVisibleTo cannot reach external assemblies).
/// </summary>
public sealed class LogEventProperty
{
    public string Name { get; }
    public LogEventPropertyValue Value { get; }

    public LogEventProperty(string name, LogEventPropertyValue value)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(value);
        Name = name;
        Value = value;
    }
}

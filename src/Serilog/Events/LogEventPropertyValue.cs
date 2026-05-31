#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace MMP.Herald.Serilog.Events;

/// <summary>
/// Abstract base for the Serilog-shaped value-node hierarchy.
/// Consumer enrichers and formatters construct these directly — all
/// constructors are public to allow use outside Herald assemblies
/// (InternalsVisibleTo cannot reach external consumers).
/// </summary>
public abstract class LogEventPropertyValue { }

/// <summary>
/// A scalar (primitive or opaque) value. The most common node type —
/// covers all primitives, strings, enums, and any value not further decomposed.
/// </summary>
public sealed class ScalarValue : LogEventPropertyValue
{
    /// <summary>The boxed scalar. May be null.</summary>
    public object? Value { get; }

    public ScalarValue(object? value) => Value = value;
}

/// <summary>
/// A structured object decomposed into named properties, optionally tagged
/// with a type name. Corresponds to Serilog's destructuring ({@}).
/// </summary>
public sealed class StructureValue : LogEventPropertyValue
{
    public IReadOnlyList<LogEventProperty> Properties { get; }

    /// <summary>
    /// Optional CLR type name captured at destructure time.
    /// May differ from Serilog on anonymous types (compare leniently in tests).
    /// </summary>
    public string? TypeTag { get; }

    public StructureValue(IEnumerable<LogEventProperty> properties, string? typeTag = null)
    {
        ArgumentNullException.ThrowIfNull(properties);
        Properties = properties.ToArray();
        TypeTag = typeTag;
    }
}

/// <summary>
/// An ordered sequence of value nodes. Corresponds to IEnumerable members
/// that are not dictionaries with scalar keys.
/// </summary>
public sealed class SequenceValue : LogEventPropertyValue
{
    public IReadOnlyList<LogEventPropertyValue> Elements { get; }

    public SequenceValue(IEnumerable<LogEventPropertyValue> elements)
    {
        ArgumentNullException.ThrowIfNull(elements);
        Elements = elements.ToArray();
    }
}

/// <summary>
/// A dictionary with scalar keys and typed values. Produced when a
/// dictionary with scalar-typed keys is destructured.
/// </summary>
public sealed class DictionaryValue : LogEventPropertyValue
{
    public IReadOnlyList<KeyValuePair<ScalarValue, LogEventPropertyValue>> Elements { get; }

    public DictionaryValue(IEnumerable<KeyValuePair<ScalarValue, LogEventPropertyValue>> elements)
    {
        ArgumentNullException.ThrowIfNull(elements);
        Elements = elements.ToArray();
    }
}

#nullable enable

using System;
using System.Collections.Generic;
using MMP.Herald.Configuration.Json;

namespace MMP.Herald.Quick;

/// <summary>
/// Manages user-defined property styles for a QuickLogBuilder.
/// These are added on top of the built-in default styles at build time.
/// </summary>
public sealed class PropertyStyleSet
{
    private readonly QuickLogBuilder _builder;
    internal readonly List<JsonPropertyStyleConfig> Items = new();

    internal PropertyStyleSet(QuickLogBuilder builder) => _builder = builder;

    // -- Add --

    /// <summary>Add a property style.</summary>
    public QuickLogBuilder Add(string propertyName, string colorName,
        bool bold = false, bool italic = false, string? backgroundColorName = null) {
        Items.Add(new JsonPropertyStyleConfig(propertyName, colorName, bold, italic, backgroundColorName));
        return _builder;
    }

    // -- Set (upsert) --

    /// <summary>Replace or add a property style.</summary>
    public QuickLogBuilder Set(string propertyName, string colorName,
        bool bold = false, bool italic = false, string? backgroundColorName = null) {
        Items.RemoveAll(s => string.Equals(s.PropertyName, propertyName, StringComparison.OrdinalIgnoreCase));
        Items.Add(new JsonPropertyStyleConfig(propertyName, colorName, bold, italic, backgroundColorName));
        return _builder;
    }

    // -- Remove --

    /// <summary>Remove a property style by name.</summary>
    public QuickLogBuilder Remove(string propertyName) {
        Items.RemoveAll(s => string.Equals(s.PropertyName, propertyName, StringComparison.OrdinalIgnoreCase));
        return _builder;
    }

    /// <summary>Remove multiple property styles by name.</summary>
    public QuickLogBuilder Remove(params string[] propertyNames) {
        foreach (var name in propertyNames)
            Items.RemoveAll(s => string.Equals(s.PropertyName, name, StringComparison.OrdinalIgnoreCase));
        return _builder;
    }

    /// <summary>Remove all custom property styles.</summary>
    public QuickLogBuilder Clear() {
        Items.Clear();
        return _builder;
    }

    // -- Query --

    /// <summary>Get a property style by name, or null.</summary>
    public JsonPropertyStyleConfig? Get(string propertyName) =>
        NamedItemQuery.Find(Items, static s => s.PropertyName, propertyName);

    /// <summary>Check if a style exists for the given property name.</summary>
    public bool Has(string propertyName) =>
        NamedItemQuery.Has(Items, static s => s.PropertyName, propertyName);

    /// <summary>Current style count.</summary>
    public int Count => Items.Count;
}

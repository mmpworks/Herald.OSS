#nullable enable

using System;
using System.Collections.Generic;
using MMP.Herald.Configuration.Json;

namespace MMP.Herald.Quick;

/// <summary>
/// Manages user-defined category (channel) styles for a QuickLogBuilder.
/// Added on top of whatever defaults the builder ships at build time.
/// Parallels <see cref="PropertyStyleSet"/>: same CRUD surface, same
/// case-insensitive matching, same chainable-builder return shape.
/// </summary>
public sealed class CategoryStyleSet
{
    private readonly QuickLogBuilder _builder;
    internal readonly List<JsonLogCategoryStyleConfig> Items = new();

    internal CategoryStyleSet(QuickLogBuilder builder) => _builder = builder;

    // -- Add --

    /// <summary>Add a category style.</summary>
    public QuickLogBuilder Add(string categoryName, string? colorName = null,
        bool bold = false, bool italic = false, string? backgroundColorName = null) {
        Items.Add(new JsonLogCategoryStyleConfig(categoryName, colorName, bold, italic, backgroundColorName));
        return _builder;
    }

    // -- Set (upsert) --

    /// <summary>Replace or add a category style.</summary>
    public QuickLogBuilder Set(string categoryName, string? colorName = null,
        bool bold = false, bool italic = false, string? backgroundColorName = null) {
        Items.RemoveAll(s => string.Equals(s.CategoryName, categoryName, StringComparison.OrdinalIgnoreCase));
        Items.Add(new JsonLogCategoryStyleConfig(categoryName, colorName, bold, italic, backgroundColorName));
        return _builder;
    }

    // -- Remove --

    /// <summary>Remove a category style by name.</summary>
    public QuickLogBuilder Remove(string categoryName) {
        Items.RemoveAll(s => string.Equals(s.CategoryName, categoryName, StringComparison.OrdinalIgnoreCase));
        return _builder;
    }

    /// <summary>Remove multiple category styles by name.</summary>
    public QuickLogBuilder Remove(params string[] categoryNames) {
        foreach (var name in categoryNames)
            Items.RemoveAll(s => string.Equals(s.CategoryName, name, StringComparison.OrdinalIgnoreCase));
        return _builder;
    }

    /// <summary>Remove all custom category styles.</summary>
    public QuickLogBuilder Clear() {
        Items.Clear();
        return _builder;
    }

    // -- Query --

    /// <summary>Get a category style by name, or null.</summary>
    public JsonLogCategoryStyleConfig? Get(string categoryName) =>
        NamedItemQuery.Find(Items, static s => s.CategoryName, categoryName);

    /// <summary>Check if a style exists for the given category name.</summary>
    public bool Has(string categoryName) =>
        NamedItemQuery.Has(Items, static s => s.CategoryName, categoryName);

    /// <summary>Current style count.</summary>
    public int Count => Items.Count;
}

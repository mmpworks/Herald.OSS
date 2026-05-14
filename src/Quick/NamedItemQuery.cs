#nullable enable

using System;
using System.Collections.Generic;

namespace MMP.Herald.Quick;

/// <summary>
/// Shared query utility for named item collections.
/// Eliminates duplicated Get/Has/Replace loop patterns across Set classes.
///
/// DRY fix: EnricherSet, EventProcessorSet, SinkProviderSet, ChannelSet,
/// and BridgeSet all had identical query loops. This utility centralizes
/// the pattern so a bug fix or optimization applies everywhere.
/// </summary>
internal static class NamedItemQuery
{
    /// <summary>Get a named item cast to a specific type, or null.</summary>
    public static T? Get<T, TItem>(List<NamedRegistration<TItem>> items, string name)
        where T : class
        where TItem : class
    {
        foreach (var reg in items)
        {
            if (string.Equals(reg.Name, name, StringComparison.OrdinalIgnoreCase) && reg.Value is T typed)
                return typed;
        }
        return null;
    }

    /// <summary>Get the first item matching a type, or null.</summary>
    public static T? GetByType<T, TItem>(List<NamedRegistration<TItem>> items)
        where T : class
        where TItem : class
    {
        foreach (var reg in items)
        {
            if (reg.Value is T typed) return typed;
        }
        return null;
    }

    /// <summary>Check if a named item exists (case-insensitive).</summary>
    public static bool Has<TItem>(List<NamedRegistration<TItem>> items, string name)
        where TItem : class
    {
        foreach (var reg in items)
        {
            if (string.Equals(reg.Name, name, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>Replace a named item. Returns true if found.</summary>
    public static bool Replace<TItem>(List<NamedRegistration<TItem>> items, string name, TItem newValue)
        where TItem : class
    {
        for (var i = 0; i < items.Count; i++)
        {
            if (string.Equals(items[i].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                items[i] = items[i] with { Value = newValue };
                return true;
            }
        }
        return false;
    }

    /// <summary>Remove all items matching a name (case-insensitive).</summary>
    public static void Remove<TItem>(List<NamedRegistration<TItem>> items, string name)
        where TItem : class
    {
        items.RemoveAll(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    // ── Overloads for non-NamedRegistration lists (ChannelSet, etc.) ──

    /// <summary>Check if a named item exists using a name selector.</summary>
    public static bool Has<TItem>(List<TItem> items, Func<TItem, string> nameSelector, string name)
    {
        foreach (var item in items)
        {
            if (string.Equals(nameSelector(item), name, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>Get a named item using a name selector, or null.</summary>
    public static TItem? Find<TItem>(List<TItem> items, Func<TItem, string> nameSelector, string name)
        where TItem : class
    {
        foreach (var item in items)
        {
            if (string.Equals(nameSelector(item), name, StringComparison.OrdinalIgnoreCase))
                return item;
        }
        return null;
    }

    /// <summary>Remove all items matching a name using a name selector.</summary>
    public static void Remove<TItem>(List<TItem> items, Func<TItem, string> nameSelector, string name)
    {
        items.RemoveAll(item => string.Equals(nameSelector(item), name, StringComparison.OrdinalIgnoreCase));
    }
}

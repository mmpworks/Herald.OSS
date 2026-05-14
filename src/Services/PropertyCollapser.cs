#nullable enable

using System;
using System.Collections.Generic;
using MMP.Herald.Templating;

namespace MMP.Herald.Services;

/// <summary>
/// Collapses duplicate properties in a list by keeping the last occurrence of each name.
/// Shared by JsonFormatter and Utf8JsonFormatter to eliminate duplicated collapse logic.
///
/// Hot-path allocation note:
/// the common case is a property list with zero duplicates. The previous
/// shape unconditionally allocated a List and a Dictionary whenever
/// Count &gt; 1 even though collapse was typically a no-op. The fast path
/// now scans for a duplicate first and returns the input as-is when none
/// is found — no allocation.
/// </summary>
public static class PropertyCollapser
{
    // Threshold below which we scan for duplicates with an O(n²) inner
    // compare rather than allocating a HashSet. Eight keeps the scan tight
    // for typical events (1–6 properties) and still bounds the worst case.
    private const int SmallListThreshold = 8;

    public static IReadOnlyList<LogProperty> Collapse(IReadOnlyList<LogProperty> properties)
    {
        if (properties.Count <= 1)
        {
            return properties;
        }

        if (!HasDuplicateNames(properties))
        {
            return properties;
        }

        return CollapseCore(properties);
    }

    private static bool HasDuplicateNames(IReadOnlyList<LogProperty> properties)
    {
        return properties.Count <= SmallListThreshold
            ? HasDuplicateNamesSmall(properties)
            : HasDuplicateNamesLarge(properties);
    }

    // Single-purpose helper for the small-list case. The O(n²) compare is
    // cheaper than a HashSet allocation when n is small.
    private static bool HasDuplicateNamesSmall(IReadOnlyList<LogProperty> properties)
    {
        var count = properties.Count;
        for (var outer = 0; outer < count - 1; outer += 1)
        {
            var name = properties[outer].Name;
            for (var inner = outer + 1; inner < count; inner += 1)
            {
                if (string.Equals(properties[inner].Name, name, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }
        return false;
    }

    // For larger lists we're about to allocate anyway; a HashSet costs less
    // than the O(n²) scan beyond the threshold.
    private static bool HasDuplicateNamesLarge(IReadOnlyList<LogProperty> properties)
    {
        var seen = new HashSet<string>(properties.Count, StringComparer.Ordinal);
        foreach (var property in properties)
        {
            if (!seen.Add(property.Name))
            {
                return true;
            }
        }
        return false;
    }

    private static IReadOnlyList<LogProperty> CollapseCore(IReadOnlyList<LogProperty> properties)
    {
        var collapsed = new List<LogProperty>(properties.Count);
        var indexByName = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var property in properties)
        {
            if (indexByName.TryGetValue(property.Name, out var existingIndex))
            {
                collapsed[existingIndex] = property;
                continue;
            }

            indexByName[property.Name] = collapsed.Count;
            collapsed.Add(property);
        }

        return collapsed;
    }
}

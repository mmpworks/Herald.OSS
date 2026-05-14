// Copyright (c) 2026 MMP LLC
// Licensed under the MIT License. See LICENSE in the project root.
#nullable enable

using System;
using System.Collections.Generic;

namespace MMP.Herald.Configuration.Sinks;

/// <summary>
/// Generic helper: takes the v2 contract from a sink's mmpform
/// <c>__properties</c> block plus a partial map of user-provided values
/// and returns a complete bag with every contract key present. Missing
/// keys fall back to the contract default; user-provided keys win when
/// they are non-null.
///
/// <para>Used by every code path that needs to publish a v2 sink JSON
/// — QuickLogBuilder when it builds the JSON for a fluent-API
/// pipeline, the management API when it serialises current values for
/// the dashboard. Both paths must end up with the same shape so the
/// "JSON for the sink must carry every property" invariant holds end
/// to end.</para>
///
/// <para>The helper does not understand any specific sink. It only
/// knows the contract (key + declared type + default) and how to
/// merge — no per-key business logic lives here.</para>
/// </summary>
public static class SinkPropertyBagBuilder
{
    /// <summary>
    /// Merge <paramref name="userValues"/> into the contract defaults
    /// from <paramref name="contract"/>. The result is keyed in
    /// contract order so the JSON written downstream lists properties
    /// the way the mmpform declares them, regardless of whether the
    /// user supplied any of them.
    /// </summary>
    public static IReadOnlyDictionary<string, object?> Build(
        IReadOnlyList<MmpformPropertyDefinition> contract,
        IReadOnlyDictionary<string, object?>? userValues = null)
    {
        ArgumentNullException.ThrowIfNull(contract);

        // LinkedHashMap-style: preserve declaration order so the JSON
        // the dashboard receives matches the mmpform's reading order.
        var bag = new Dictionary<string, object?>(contract.Count, StringComparer.Ordinal);
        foreach (var entry in contract)
        {
            if (userValues is not null
                && userValues.TryGetValue(entry.Name, out var supplied)
                && supplied is not null)
            {
                bag[entry.Name] = supplied;
            }
            else
            {
                bag[entry.Name] = entry.Default;
            }
        }
        return bag;
    }

    /// <summary>
    /// Convenience overload: parse the mmpform text and merge in one
    /// call. Returns an empty dictionary when the mmpform has no
    /// <c>__properties</c> block.
    /// </summary>
    public static IReadOnlyDictionary<string, object?> Build(
        string? mmpformText,
        IReadOnlyDictionary<string, object?>? userValues = null)
    {
        var contract = MmpformPropertiesParser.Parse(mmpformText);
        return contract.Count == 0
            ? new Dictionary<string, object?>(StringComparer.Ordinal)
            : Build(contract, userValues);
    }
}

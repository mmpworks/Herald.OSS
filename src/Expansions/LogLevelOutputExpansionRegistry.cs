#nullable enable

using System;
using System.Collections.Generic;
using MMP.Herald.Levels;
using MMP.Herald.Output.Aliases;

namespace MMP.Herald.Expansions;
/// <summary>
/// Stores expansions in registration order.
/// </summary>
public sealed class LogLevelOutputExpansionRegistry : ILogLevelOutputExpansionRegistry
{
    private const string AllAliasesKey = "*";

    private readonly Dictionary<string, List<ILogLevelOutputExpansion>> _expansions =
        new(StringComparer.OrdinalIgnoreCase);

    public void Register(LogLevel level, LogOutputAlias alias, ILogLevelOutputExpansion expansion)
    {
        ArgumentNullException.ThrowIfNull(level);
        ArgumentNullException.ThrowIfNull(alias);
        ArgumentNullException.ThrowIfNull(expansion);

        var key = BuildKey(level.Key, alias.Key);

        if (!_expansions.TryGetValue(key, out var list))
        {
            list = [];
            _expansions[key] = list;
        }

        list.Add(expansion);
    }

    public void RegisterForAllAliases(LogLevel level, ILogLevelOutputExpansion expansion)
    {
        ArgumentNullException.ThrowIfNull(level);
        ArgumentNullException.ThrowIfNull(expansion);

        var key = BuildKey(level.Key, AllAliasesKey);

        if (!_expansions.TryGetValue(key, out var list))
        {
            list = [];
            _expansions[key] = list;
        }

        list.Add(expansion);
    }

    public IReadOnlyList<ILogLevelOutputExpansion> Get(LogLevel level, LogOutputAlias alias)
    {
        ArgumentNullException.ThrowIfNull(level);
        ArgumentNullException.ThrowIfNull(alias);

        var output = new List<ILogLevelOutputExpansion>();

        if (_expansions.TryGetValue(BuildKey(level.Key, AllAliasesKey), out var wildcard))
        {
            output.AddRange(wildcard);
        }

        if (_expansions.TryGetValue(BuildKey(level.Key, alias.Key), out var exact))
        {
            output.AddRange(exact);
        }

        return output;
    }

    private static string BuildKey(string levelKey, string aliasKey)
    {
        return $"{levelKey}::{aliasKey}";
    }
}
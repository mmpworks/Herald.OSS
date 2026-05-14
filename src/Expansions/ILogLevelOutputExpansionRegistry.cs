#nullable enable

using System.Collections.Generic;
using MMP.Herald.Levels;
using MMP.Herald.Output.Aliases;

namespace MMP.Herald.Expansions;
/// <summary>
/// Registry for per-level expansions scoped to an alias or all aliases.
/// </summary>
public interface ILogLevelOutputExpansionRegistry
{
    void Register(LogLevel level, LogOutputAlias alias, ILogLevelOutputExpansion expansion);
    void RegisterForAllAliases(LogLevel level, ILogLevelOutputExpansion expansion);
    IReadOnlyList<ILogLevelOutputExpansion> Get(LogLevel level, LogOutputAlias alias);
}
#nullable enable

namespace MMP.Herald.Output.Aliases;
/// <summary>
/// Well-known presentation aliases.
/// </summary>
public static class KnownLogOutputAliases
{
    public static LogOutputAlias Standard { get; } = new("standard");
    public static LogOutputAlias Console { get; } = new("console");
}
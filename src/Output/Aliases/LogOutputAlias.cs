#nullable enable

namespace MMP.Herald.Output.Aliases;
/// <summary>
/// Named output style for presentation-oriented rendering.
/// </summary>
public sealed record LogOutputAlias(string Key)
{
    public override string ToString()
    {
        return Key;
    }
}
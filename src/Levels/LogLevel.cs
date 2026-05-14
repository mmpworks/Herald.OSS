#nullable enable

namespace MMP.Herald.Levels;
/// <summary>
/// Domain value for a log level.
/// Rank is intentionally NOT stored here because ordering is owned by the registry.
/// </summary>
public sealed record LogLevel(string Key, string DisplayName)
{
    public override string ToString()
    {
        return DisplayName;
    }
}
#nullable enable

namespace MMP.Herald.Formatting;

/// <summary>
/// Token types produced by the output template parser.
/// </summary>
public abstract record OutputTemplateToken
{
    /// <summary>A literal text segment.</summary>
    public sealed record Text(string Value) : OutputTemplateToken;

    /// <summary>
    /// A well-known named token with an optional format specifier.
    /// Recognized names: Timestamp, Level, LevelKey, Category, Message,
    /// Properties, Context, Exception, NewLine.
    /// </summary>
    public sealed record Token(string Name, string? Format) : OutputTemplateToken;
}

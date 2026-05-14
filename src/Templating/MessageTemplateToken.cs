#nullable enable

namespace MMP.Herald.Templating;

/// <summary>
/// A token inside a parsed message template.
/// </summary>
public abstract record MessageTemplateToken
{
    /// <summary>
    /// Literal text token.
    /// </summary>
    public sealed record Text(string Value) : MessageTemplateToken;

    /// <summary>
    /// Named property placeholder token.
    /// </summary>
    public sealed record Property(
        string Name,
        LogPropertyCaptureMode CaptureMode,
        string? Format,
        string RawText) : MessageTemplateToken;
}
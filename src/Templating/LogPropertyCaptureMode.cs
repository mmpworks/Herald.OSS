#nullable enable

namespace MMP.Herald.Templating;

/// <summary>
/// Controls how a structured property should be interpreted and rendered.
/// </summary>
public sealed record LogPropertyCaptureMode(string Value)
{
    public static LogPropertyCaptureMode Default { get; } = new("Default");

    public static LogPropertyCaptureMode Destructure { get; } = new("Destructure");

    public static LogPropertyCaptureMode Stringify { get; } = new("Stringify");

    public override string ToString()
    {
        return Value;
    }
}
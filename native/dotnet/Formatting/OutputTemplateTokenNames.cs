#nullable enable

namespace MMP.Herald.Formatting;

/// <summary>
/// Token names recognized by OutputTemplateFormatter in output template strings.
/// Use these when building or validating output templates programmatically.
///
/// Example template: "{Timestamp:O} [{Level}] {Category} - {Message}{NewLine}{Exception}"
/// </summary>
public static class OutputTemplateTokenNames
{
    public const string Timestamp = "timestamp";
    public const string Level = "level";
    public const string LevelKey = "levelkey";
    public const string Category = "category";
    public const string Message = "message";
    public const string Properties = "properties";
    public const string Context = "context";
    public const string Exception = "exception";
    public const string NewLine = "newline";
}

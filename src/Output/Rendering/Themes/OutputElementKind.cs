#nullable enable

namespace MMP.Herald.Output.Rendering.Themes;

/// <summary>
/// Identifies a logical output element for theme styling.
/// Each subtype represents a distinct part of a console log line.
/// Uses sealed record hierarchy instead of enum (per coding conventions: prefer records over enums).
/// Static Instance fields are cached to avoid per-resolve allocations in the render path.
/// The Key property provides a pre-computed type name for dictionary lookups,
/// avoiding GetType().Name reflection on the hot path.
/// </summary>
public abstract record OutputElementKind
{
    /// <summary>
    /// Pre-computed key for dictionary lookups. Avoids GetType().Name reflection per resolve.
    /// </summary>
    public abstract string Key { get; }

    private OutputElementKind() { }

    public sealed record Timestamp : OutputElementKind
    {
        public static readonly Timestamp Instance = new();
        public override string Key => "Timestamp";
    }

    public sealed record LevelText : OutputElementKind
    {
        public static readonly LevelText Instance = new();
        public override string Key => "LevelText";
    }

    public sealed record Category : OutputElementKind
    {
        public static readonly Category Instance = new();
        public override string Key => "Category";
    }

    public sealed record Separator : OutputElementKind
    {
        public static readonly Separator Instance = new();
        public override string Key => "Separator";
    }

    public sealed record MessageText : OutputElementKind
    {
        public static readonly MessageText Instance = new();
        public override string Key => "MessageText";
    }

    public sealed record PropertyName : OutputElementKind
    {
        public static readonly PropertyName Instance = new();
        public override string Key => "PropertyName";
    }

    public sealed record PropertyValue : OutputElementKind
    {
        public static readonly PropertyValue Instance = new();
        public override string Key => "PropertyValue";
    }

    public sealed record StringValue : OutputElementKind
    {
        public static readonly StringValue Instance = new();
        public override string Key => "StringValue";
    }

    public sealed record NumericValue : OutputElementKind
    {
        public static readonly NumericValue Instance = new();
        public override string Key => "NumericValue";
    }

    public sealed record NullValue : OutputElementKind
    {
        public static readonly NullValue Instance = new();
        public override string Key => "NullValue";
    }

    public sealed record ExceptionText : OutputElementKind
    {
        public static readonly ExceptionText Instance = new();
        public override string Key => "ExceptionText";
    }

    public sealed record Punctuation : OutputElementKind
    {
        public static readonly Punctuation Instance = new();
        public override string Key => "Punctuation";
    }
}

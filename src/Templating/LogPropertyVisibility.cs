#nullable enable

namespace MMP.Herald.Templating;

/// <summary>
/// Controls whether a structured property appears in rendered message output.
///
/// Rendered properties appear inline in the message text when their name
/// matches a template placeholder. This is standard behavior.
///
/// Silent properties are carried through the pipeline - available to sinks,
/// signal handlers, JSON serializers, and structured queries - but are not
/// rendered into the human-readable message text. They appear in structured
/// output (JSON, context metadata) but not in console or plain text messages.
///
/// This solves the "structured data without message pollution" problem:
/// attach searchable metadata to events without cluttering the message.
///
/// Usage:
///   // Rendered: appears in "Order placed by Kaelen"
///   new LogProperty("customer", "Kaelen")
///
///   // Silent: carried as metadata, not shown in message text
///   new LogProperty("serverId", "us-east-3", Visibility: LogPropertyVisibility.Silent)
///   LogProperty.Silent("transactionId", "tx-9281")
/// </summary>
public sealed record LogPropertyVisibility(string Value)
{
    /// <summary>
    /// Property appears in rendered message output (default behavior).
    /// </summary>
    public static LogPropertyVisibility Rendered { get; } = new("Rendered");

    /// <summary>
    /// Property is carried through the pipeline but excluded from rendered
    /// message text. Still available in structured output, context metadata,
    /// signal payloads, and JSON serialization.
    /// </summary>
    public static LogPropertyVisibility Silent { get; } = new("Silent");

    public override string ToString() => Value;
}

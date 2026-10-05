#nullable enable

using System;

namespace MMP.Herald.Events;

/// <summary>
/// The text Herald writes when a lazy property factory, the message template, or a sink throws. Logging keeps
/// going, and the text names what failed and the exception TYPE only.
/// </summary>
/// <remarks>
/// The exception message is left out on purpose. On .NET 8 and later a message often quotes its input:
/// <c>int.Parse("999-12-3456")</c> throws "The input string '999-12-3456' was not in a correct format.". A fallback
/// that copied the message would put a raw value into a property, the rendered Message, SelfLog or Trace, where
/// redaction that matches by property name never sees it. The one place for this rule; every fallback calls it.
/// </remarks>
internal static class FallbackText
{
    /// <summary>"[Lazy property 'Ssn' threw FormatException]".</summary>
    public static string LazyPropertyThrew(string propertyName, Exception ex) =>
        $"[Lazy property '{propertyName}' threw {ex.GetType().Name}]";

    /// <summary>"[Template error: FormatException] Patient {Value}". The template is the caller's constant.</summary>
    public static string TemplateError(Exception ex, string messageTemplate) =>
        $"[Template error: {ex.GetType().Name}] {messageTemplate}";

    /// <summary>The exception type for a diagnostic line (SelfLog, Trace).</summary>
    public static string ExceptionType(Exception ex) => ex.GetType().Name;
}

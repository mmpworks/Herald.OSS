#nullable enable

using System;

namespace MMP.Herald.Events;

/// <summary>
/// The text Herald itself generates when a lazy property factory, a value's <c>ToString</c>, the message template,
/// or a sink throws: fallback values and diagnostic lines. The text names what failed and the exception TYPE only.
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

    /// <summary>"[Property 'Value' ToString threw FormatException]": a value whose <c>ToString</c> throws.</summary>
    public static string ValueToStringThrew(string propertyName, Exception ex) =>
        $"[Property '{propertyName}' ToString threw {ex.GetType().Name}]";

    /// <summary>
    /// A value's text for a formatter: <c>null</c> for null, the string itself, the value's <c>ToString</c>, or
    /// <see cref="ValueToStringThrew"/> when <c>ToString</c> throws. This is the second layer of the rule: the event
    /// factory replaces a value that failed once, and every formatter still guards its own call, because any object
    /// can throw on a later call even after an earlier call succeeded.
    /// </summary>
    public static string? ValueText(object? value, string name)
    {
        if (value is null or string) return (string?)value;
        try
        {
            return value.ToString();
        }
        catch (Exception ex)
        {
            return ValueToStringThrew(name, ex);
        }
    }

    /// <summary><see cref="ValueText"/> for <see cref="IFormattable.ToString(string?, IFormatProvider?)"/>.</summary>
    public static string? FormattedValueText(IFormattable value, string? format, IFormatProvider? provider, string name)
    {
        try
        {
            return value.ToString(format, provider);
        }
        catch (Exception ex)
        {
            return ValueToStringThrew(name, ex);
        }
    }

    /// <summary>The exception type for a diagnostic line (SelfLog, Trace, the failure-sink record).</summary>
    public static string ExceptionType(Exception ex) => ex.GetType().Name;
}

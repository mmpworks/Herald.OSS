#nullable enable
#if NET9_0_OR_GREATER

using System;
using System.Collections.Generic;
using MMP.Herald.Serilog.Events;

namespace MMP.Herald.OSS.Tests.Infrastructure;

/// <summary>
/// Full-event secret scanner for redaction tests against the Serilog mirror model.
///
/// <para>
/// A field-name check ("no property named 'password' exists") is not sufficient:
/// a secret can leak into a rendered message, into another property's value after
/// enrichment, or into an exception's <c>ToString()</c>. This scanner walks every
/// reachable string in a <see cref="LogEvent"/> so a test assertion is exhaustive.
/// </para>
///
/// <para>
/// Tree coverage: top-level <see cref="LogEvent.Properties"/>, then recursive descent
/// into <see cref="StructureValue.Properties"/>, <see cref="SequenceValue.Elements"/>,
/// and <see cref="DictionaryValue.Elements"/> (both keys and values). A secret nested
/// three levels deep in a destructured object is still found.
/// </para>
///
/// <para>
/// Comparison is ordinal and case-sensitive — matching the redaction layer so the
/// scanner and the redaction pass agree on whether a string matches.
/// </para>
/// </summary>
public static class SecretScanner
{
    /// <summary>
    /// Returns all locations where <paramref name="secret"/> appears in the event.
    /// An empty list means the event is clean. A non-empty list names every location
    /// so a failing assertion tells the test author exactly where the leak is.
    ///
    /// <para>Locations searched:</para>
    /// <list type="bullet">
    ///   <item>Rendered message (<see cref="LogEvent.RenderMessage"/>)</item>
    ///   <item>Message template (<see cref="LogEvent.MessageTemplate"/>)</item>
    ///   <item>All property values, recursively through the full value-node tree</item>
    ///   <item>Exception text (<c>Exception?.ToString()</c>)</item>
    /// </list>
    /// </summary>
    public static IReadOnlyList<string> FindAll(LogEvent evt, string secret)
    {
        ArgumentNullException.ThrowIfNull(evt);
        ArgumentNullException.ThrowIfNull(secret);

        var locations = new List<string>();

        // Rendered message — primary leak vector when a template contains a placeholder
        // that resolves to the secret string after property substitution.
        var rendered = evt.RenderMessage();
        if (rendered.Contains(secret, StringComparison.Ordinal))
            locations.Add("RenderMessage()");

        // Raw message template — can embed literal secret text directly.
        if (evt.MessageTemplate.Contains(secret, StringComparison.Ordinal))
            locations.Add("MessageTemplate");

        // All structured properties — recurse the full value-node tree.
        foreach (var kvp in evt.Properties)
        {
            WalkValue(kvp.Value, $"Properties[{kvp.Key}]", secret, locations);
        }

        // Exception — ToString() includes the type, message, and inner chain.
        var exText = evt.Exception?.ToString();
        if (exText is not null && exText.Contains(secret, StringComparison.Ordinal))
            locations.Add("Exception.ToString()");

        return locations;
    }

    // Recursive descent through the LogEventPropertyValue tree.
    // Each node type is handled explicitly so a new subtype added in the future
    // causes a compile-time gap rather than a silent skip.
    private static void WalkValue(
        LogEventPropertyValue value,
        string path,
        string secret,
        List<string> locations)
    {
        switch (value)
        {
            case ScalarValue sv:
                // Scalars hold a single boxed value. A string is checked directly;
                // any other type is checked via ToString() — the same path a sink
                // or formatter would use to produce output.
                var str = sv.Value switch
                {
                    string s => s,
                    null     => null,
                    var obj  => obj.ToString()
                };
                if (str is not null && str.Contains(secret, StringComparison.Ordinal))
                    locations.Add(path);
                break;

            case StructureValue structVal:
                // Destructured object: recurse into each named child property.
                foreach (var prop in structVal.Properties)
                    WalkValue(prop.Value, $"{path}.{prop.Name}", secret, locations);
                break;

            case SequenceValue seqVal:
                // Ordered list of values: recurse with a positional path.
                for (var i = 0; i < seqVal.Elements.Count; i++)
                    WalkValue(seqVal.Elements[i], $"{path}[{i}]", secret, locations);
                break;

            case DictionaryValue dictVal:
                // Dictionary with scalar keys: check both keys and values.
                foreach (var entry in dictVal.Elements)
                {
                    WalkValue(entry.Key, $"{path}.key", secret, locations);
                    WalkValue(entry.Value, $"{path}[{entry.Key.Value}]", secret, locations);
                }
                break;

            default:
                // Unknown subtype — fall back to ToString() so a future node kind
                // still gets a best-effort check rather than a silent skip.
                var fallback = value.ToString();
                if (fallback is not null && fallback.Contains(secret, StringComparison.Ordinal))
                    locations.Add($"{path}(unknown:{value.GetType().Name})");
                break;
        }
    }
}

#endif

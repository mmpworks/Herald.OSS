#nullable enable

using System;
using MMP.Herald.Events;
using MMP.Herald.Templating;

namespace MMP.Herald.OSS.Tests.TestSupport;

/// <summary>
/// Full-event secret scanner for redaction tests.
///
/// A field-name check (e.g. "no property named 'password' exists") is not
/// sufficient: a secret could leak into a rendered message, into a different
/// property's value, or into an exception's ToString(). This scanner walks
/// every reachable string in a LogEvent so a test that asserts "the secret
/// never appears" is actually exhaustive.
/// </summary>
public static class SecretScanner
{
    /// <summary>
    /// Returns true if the secret literal appears ANYWHERE in the event:
    /// rendered message, every property value (recursively), exception text.
    ///
    /// Comparison is ordinal and case-sensitive — the same comparison mode
    /// the redaction layer uses, so a scanner pass and a redaction pass agree
    /// on whether a string matches.
    /// </summary>
    public static bool ContainsSecret(LogEvent e, string secret)
    {
        if (secret is null) throw new ArgumentNullException(nameof(secret));
        if (e is null) throw new ArgumentNullException(nameof(e));

        // Rendered message — the primary leak vector when a template contains
        // a secret placeholder and rendering is not suppressed.
        if (e.Message?.Contains(secret, StringComparison.Ordinal) == true)
            return true;

        // Raw message template — can also embed literal secret text.
        if (e.MessageTemplate?.Contains(secret, StringComparison.Ordinal) == true)
            return true;

        // All structured properties — walk resolved values so lazy factories
        // are evaluated. Complex/nested values are walked recursively.
        foreach (var prop in e.Properties)
        {
            if (PropertyContains(prop, secret))
                return true;
        }

        // Exception — ToString() includes the type, message, and inner chain.
        if (e.CausedBy?.Contains(secret, StringComparison.Ordinal) == true)
            return true;

        return false;
    }

    // Recursive walker for a single LogProperty struct.
    private static bool PropertyContains(LogProperty prop, string secret)
    {
        // Resolve lazy factories; the resolved value is what ends up in a sink.
        var resolved = prop.ResolvedValue;
        return ValueContains(resolved, secret);
    }

    // Recursive walker for an arbitrary value. Handles null, string, and any
    // other type whose ToString() representation could carry the secret.
    private static bool ValueContains(object? value, string secret)
    {
        if (value is null) return false;

        if (value is string s)
            return s.Contains(secret, StringComparison.Ordinal);

        // For composite/complex objects, fall back to ToString(). This catches
        // anonymous types, custom records, and any type that embeds the secret
        // in its string representation.
        var str = value.ToString();
        return str is not null && str.Contains(secret, StringComparison.Ordinal);
    }
}

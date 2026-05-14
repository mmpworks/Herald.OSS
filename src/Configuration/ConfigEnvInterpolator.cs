#nullable enable

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using MMP.Herald.Services;

namespace MMP.Herald.Configuration;

/// <summary>
/// Substitutes environment-variable references in a raw JSON config string
/// before deserialization. Keeps secrets out of committable config files:
/// operators write <c>"connectionString": "${DB_URL}"</c> and supply the
/// real value via environment at process boot.
///
/// <para>
/// Two forms:
/// <list type="bullet">
///   <item><c>${ENV_NAME}</c> — replaced with the environment variable value; throws when the variable is unset.</item>
///   <item><c>${ENV_NAME:-default}</c> — replaced with the env var when set, otherwise with the literal default.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>JSON safety.</b> Substituted values pass through
/// <see cref="JsonEscaper.Escape"/> before they land in the JSON string —
/// so a value containing <c>"</c>, <c>\</c>, or any control character
/// produces valid JSON, and a value crafted to inject extra keys
/// (<c>value", "secret": "evil</c>) is neutralised at substitution time.
/// The escaping is a no-op for the overwhelmingly common case where the
/// value is plain text, so production configs see no behaviour change.
/// </para>
///
/// <para>
/// Shared by Herald.Server and Herald.Lean. Previously each facade carried
/// its own copy; hoisting here removes the drift surface.
/// </para>
/// </summary>
public static class ConfigEnvInterpolator
{
    // Named capture groups per the repo's regex policy. name is required,
    // default is everything after ":-" up to the closing brace.
    private static readonly Regex TokenRegex = new(
        @"\$\{(?<name>[A-Za-z_][A-Za-z0-9_]*)(?::-(?<default>[^}]*))?\}",
        RegexOptions.Compiled);

    /// <summary>
    /// Expand every <c>${...}</c> token in <paramref name="raw"/>. Throws
    /// <see cref="InvalidOperationException"/> when one or more required
    /// variables are unset (the form without <c>:-default</c>). The message
    /// lists every unresolved name so operators see the whole batch at once.
    ///
    /// <para>
    /// Substituted values are JSON-escaped before insertion. See the type
    /// summary for the rationale.
    /// </para>
    /// </summary>
    public static string Expand(string raw)
    {
        ArgumentNullException.ThrowIfNull(raw);

        if (!raw.Contains("${", StringComparison.Ordinal))
        {
            return raw;
        }

        var unresolved = new List<string>();

        var expanded = TokenRegex.Replace(raw, match => ResolveToken(match, unresolved));

        if (unresolved.Count > 0)
        {
            var list = string.Join(", ", unresolved);
            throw new InvalidOperationException(
                $"Config references unresolved environment variables: {list}. " +
                "Set the variables or use the ${NAME:-default} form to supply a fallback.");
        }

        return expanded;
    }

    // Resolution order: env var (escaped), default (escaped), unresolved
    // (the original token is returned so the surrounding text stays intact
    // until Expand collects every name and throws).
    private static string ResolveToken(Match match, ICollection<string> unresolved)
    {
        var name = match.Groups["name"].Value;
        var value = Environment.GetEnvironmentVariable(name);
        if (value is not null) return JsonEscaper.Escape(value);

        var defaultGroup = match.Groups["default"];
        if (defaultGroup.Success) return JsonEscaper.Escape(defaultGroup.Value);

        unresolved.Add(name);
        return match.Value;
    }
}

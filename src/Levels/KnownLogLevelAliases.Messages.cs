#nullable enable

using MMP.Herald.Responses;

namespace MMP.Herald.Levels;

/// <summary>
/// Runtime-only half of <see cref="KnownLogLevelAliases"/>.
///
/// <para>
/// Kept out of <c>KnownLogLevelAliases.cs</c> because that file is linked into
/// the netstandard2.0 <c>MMP.Herald.OSS.Generators</c> assembly, and this half
/// needs <see cref="HeraldErrorCodes"/>. Linking that too would compile the same
/// public type into two assemblies and collide for anything referencing both.
/// The generator has no use for a failure message, so the split costs nothing.
/// </para>
/// </summary>
public static partial class KnownLogLevelAliases
{
    /// <summary>
    /// Builds the message for a level key that did not resolve.
    ///
    /// <para>
    /// Task 9 made an old key a loud reject at the registry, but a bare null is
    /// not loud — it tells the caller nothing about what to write instead. When
    /// the key is a deprecated spelling this names its replacement. Every message
    /// carries <see cref="HeraldErrorCodes.LevelKeyNotFound"/> so callers branch
    /// on the code rather than on the text.
    /// </para>
    /// </summary>
    public static string DescribeUnknownKey(string levelKey)
    {
        var head = $"No log level with key '{levelKey}' exists in the registry.";

        var hint = IsAlias(levelKey)
            ? $" '{levelKey}' was renamed in 0.12.0; use '{ToCanonicalKey(levelKey)}'."
            : string.Empty;

        return $"{head}{hint} [Herald {HeraldErrorCodes.LevelKeyNotFound} LevelKeyNotFound]";
    }
}

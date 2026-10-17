#nullable enable

using System;
using System.Collections.Generic;

namespace MMP.Herald.Levels;

/// <summary>
/// The four log-level spellings the 0.12.0 rename replaced, and the canonical
/// key each one means.
///
/// <para>
/// <b>Authoring boundary only.</b> These names work where code is WRITTEN — as
/// constants, in <c>[HeraldLog(Level = "...")]</c>, and through the source
/// generator. They deliberately do NOT resolve at the registry. P0 Task 9
/// removed the wire-side alias map so an old key arriving at
/// <see cref="LogLevelRegistry.GetByKeyOrNull"/> returns null, and
/// <c>LevelRenameRegressionTests</c> pins that as G-LEVEL.1 and G-LEVEL.5. On-disk
/// config is handled separately by the S-3 migration shim. Do not wire this type
/// into the registry: the reconciliation note is explicit that you cannot both
/// loud-reject and accept old keys at the same boundary.
/// </para>
///
/// <para>
/// <b>This is the one list.</b> The HERALD007 analyzer, <c>HeraldLogGenerator</c>
/// and the S-3 config shim all read it. Before this file existed the same four
/// pairs were written out separately in each of them and the copies drifted —
/// the analyzer rejected <c>[HeraldLog(Level = "info")]</c> while the generator
/// compiled it. Add an alias here and every reader picks it up.
/// </para>
///
/// <para>
/// <c>MMP.Herald.OSS.Generators</c> links THIS file into its netstandard2.0
/// assembly, so it stays language-feature-conservative: const strings, arrays,
/// one dictionary. No records, and no dependency on any other Herald type.
/// The runtime-only half of the class lives in
/// <c>KnownLogLevelAliases.Messages.cs</c>, which is not linked because it needs
/// <c>HeraldErrorCodes</c> — a public type that would collide if it were
/// compiled into both assemblies.
/// </para>
/// </summary>
public static partial class KnownLogLevelAliases
{
    /// <summary>Deprecated spelling of <see cref="KnownLogLevelKeys.Verbose"/>.</summary>
    public const string Trace = "trace";

    /// <summary>Deprecated spelling of <see cref="KnownLogLevelKeys.Information"/>.</summary>
    public const string Info = "info";

    /// <summary>Deprecated spelling of <see cref="KnownLogLevelKeys.Warning"/>.</summary>
    public const string Warn = "warn";

    /// <summary>Deprecated spelling of <see cref="KnownLogLevelKeys.Fatal"/>.</summary>
    public const string Critical = "critical";

    /// <summary>
    /// Every alias, paired with the canonical key it means. Index 0 is the
    /// deprecated spelling, index 1 the canonical key.
    /// </summary>
    public static readonly string[][] Pairs =
    {
        new[] { Trace,    KnownLogLevelKeys.Verbose },
        new[] { Info,     KnownLogLevelKeys.Information },
        new[] { Warn,     KnownLogLevelKeys.Warning },
        new[] { Critical, KnownLogLevelKeys.Fatal },
    };

    // Derived from Pairs so the lookup can never disagree with the list.
    private static readonly Dictionary<string, string> _canonicalByAlias = BuildIndex();

    /// <summary>
    /// True when <paramref name="levelKey"/> is a deprecated spelling.
    /// Case-insensitive. Null, empty and whitespace are not aliases.
    /// </summary>
    public static bool IsAlias(string? levelKey) =>
        !string.IsNullOrWhiteSpace(levelKey) && _canonicalByAlias.ContainsKey(levelKey!);

    /// <summary>
    /// Returns the canonical key for a deprecated spelling, or
    /// <paramref name="levelKey"/> unchanged when it is not an alias.
    /// Case-insensitive.
    /// </summary>
    public static string ToCanonicalKey(string levelKey)
    {
        if (string.IsNullOrWhiteSpace(levelKey)) return levelKey;

        return _canonicalByAlias.TryGetValue(levelKey, out var canonical)
            ? canonical
            : levelKey;
    }

    private static Dictionary<string, string> BuildIndex()
    {
        var index = new Dictionary<string, string>(Pairs.Length, StringComparer.OrdinalIgnoreCase);

        foreach (var pair in Pairs)
        {
            index[pair[0]] = pair[1];
        }

        return index;
    }
}

#nullable enable

using System;

namespace MMP.Herald.Serilog.Formatting;

/// <summary>
/// Renders Herald's post-P0 level keys through Serilog's <c>{Level}</c> specifier family.
///
/// <para>
/// Every row in the 6-level core table is oracle-pinned against real Serilog 4.3.1
/// via <c>SerilogLevelMonikerTests.Matches_real_serilog_output</c>. If the oracle ever
/// returns a different value for a given (levelKey, specifier) pair, the oracle wins.
/// </para>
///
/// <para>
/// Herald-extra levels (<c>notice</c>/<c>success</c>/<c>security</c>/<c>metric</c>)
/// have no Serilog equivalent. For width-constrained specifiers (e.g. <c>u3</c>) the
/// fallback is the first <em>width</em> characters of the level key, uppercased, padded
/// with <c>X</c> if the key is shorter than the requested width. This is deterministic
/// and documented in the P3 parity audit (pinned-gap entry).
/// </para>
/// </summary>
public static class SerilogLevelMoniker
{
    // ── Public surface ────────────────────────────────────────────────────────

    /// <summary>
    /// Render <paramref name="levelKey"/> using Serilog's format specifier.
    /// </summary>
    /// <param name="levelKey">
    /// Herald post-P0 level key (lower-case): <c>verbose</c>, <c>debug</c>,
    /// <c>information</c>, <c>warning</c>, <c>error</c>, <c>fatal</c>, plus
    /// extras <c>notice</c>, <c>success</c>, <c>security</c>, <c>metric</c>.
    /// </param>
    /// <param name="format">
    /// The format specifier extracted from the output-template hole (e.g.
    /// <c>"u3"</c>, <c>"w"</c>, <c>"u"</c>, <c>"t"</c>), or <c>null</c> /
    /// empty string to use the default (title-case).
    /// </param>
    /// <returns>The formatted level moniker.</returns>
    public static string Render(string levelKey, string? format)
    {
        // Default path — no format specifier means title-case.
        if (string.IsNullOrEmpty(format))
            return Titlecase(levelKey);

        // Specifier grammar: optional casing char followed by optional width digit(s).
        //   'u' = upper, 'w' = lower, 't' = title (explicit), default = title.
        // Serilog also accepts e.g. 'u3' meaning "uppercase, truncate to 3 chars" which
        // for the standard six levels means return the canonical 3-char abbreviation.
        char casing = char.ToLowerInvariant(format[0]);

        // Parse the optional width suffix (e.g. the "3" in "u3").
        // Declare width as 0; TryParse overwrites it when a digit suffix is present.
        // C# 12 note: kept the explicit pre-init + separate bool because the
        // bool = ... && TryParse(..., out int) && value>0 pattern is not
        // definitively-assigned-in-true-branch in all compiler versions.
        int width = 0;
        bool hasWidth = format.Length > 1
                        && int.TryParse(format.AsSpan(1), out width)
                        && width > 0;

        if (!hasWidth)
        {
            // Casing-only specifier: u / w / t.
            return casing switch
            {
                'u' => levelKey.ToUpperInvariant(),
                'w' => levelKey.ToLowerInvariant(),
                't' => Titlecase(levelKey),
                _   => Titlecase(levelKey), // unknown specifier falls back to default
            };
        }

        // Width specifier — for casing 'u' or 'w' (Serilog only defines u[n]).
        // For standard levels at width 3, return the oracle-pinned 3-char abbreviation.
        // For other widths or extra levels, truncate/pad the full name.
        string abbreviated = casing == 'w'
            ? LowerAbbreviate(levelKey, width)
            : UpperAbbreviate(levelKey, width);

        return abbreviated;
    }

    // ── Core abbreviation logic ───────────────────────────────────────────────

    // Oracle-pinned abbreviation table for the 6 Serilog-standard levels at widths 1–5.
    // Verified against real Serilog 4.3.1 via SerilogLevelMonikerTests (oracle cross-check).
    //
    // Key observations from the oracle run (see tests/Output/Serilog/SerilogLevelAbbreviationProbeTests.cs):
    //   - Width-3 uses well-known canonical abbrevs: VRB/DBG/INF/WRN/ERR/FTL.
    //   - Other widths are NOT simple string truncation (e.g. warning:u2 = "WN", not "WA";
    //     debug:u3 = "DBG", not "DEB"; fatal:u4 = "FATL", not "FATA"; error:u4 = "EROR", not "ERRO").
    //   - Width 5 always equals the full lowercase name in uppercase (all standard level names ≤5 chars
    //     at width 5: "VERBO" is first 5 of "verbose").
    //   - This table is the single source of truth for the standard six levels; extras fall through to
    //     the generic first-N-chars path (which is adequate since extras have no Serilog counterpart).
    //
    // Upgrade note for C# 14: if tuple pattern matching improves, this nested switch
    // could be expressed more compactly. The current form is readable and .NET 8-compatible.
    private static string? TryGetCanonicalAbbreviation(string levelKey, int width)
    {
        return (levelKey, width) switch
        {
            // verbose
            ("verbose", 1) => "V",
            ("verbose", 2) => "VB",
            ("verbose", 3) => "VRB",
            ("verbose", 4) => "VERB",
            ("verbose", 5) => "VERBO",

            // debug
            ("debug", 1) => "D",
            ("debug", 2) => "DE",
            ("debug", 3) => "DBG",
            ("debug", 4) => "DBUG",
            ("debug", 5) => "DEBUG",

            // information
            ("information", 1) => "I",
            ("information", 2) => "IN",
            ("information", 3) => "INF",
            ("information", 4) => "INFO",
            ("information", 5) => "INFOR",

            // warning
            ("warning", 1) => "W",
            ("warning", 2) => "WN",
            ("warning", 3) => "WRN",
            ("warning", 4) => "WARN",
            ("warning", 5) => "WARNI",

            // error
            ("error", 1) => "E",
            ("error", 2) => "ER",
            ("error", 3) => "ERR",
            ("error", 4) => "EROR",
            ("error", 5) => "ERROR",

            // fatal
            ("fatal", 1) => "F",
            ("fatal", 2) => "FA",
            ("fatal", 3) => "FTL",
            ("fatal", 4) => "FATL",
            ("fatal", 5) => "FATAL",

            // extras and unknowns, or widths > 5: fall through to generic truncation
            _ => null,
        };
    }

    // Produce an upper-case abbreviated rendering.
    // Standard levels at width 3 use the oracle-pinned canonical abbreviation.
    // All other cases truncate the upper-case key to the requested width, padding
    // with 'X' when the key is shorter than the width.
    private static string UpperAbbreviate(string levelKey, int width)
    {
        var canonical = TryGetCanonicalAbbreviation(levelKey, width);
        if (canonical is not null)
            return canonical;

        return TruncatePad(levelKey.ToUpperInvariant(), width);
    }

    // Produce a lower-case abbreviated rendering.
    // Standard levels at width 3 use the lower-case form of the canonical abbreviation.
    // All other cases truncate the lower-case key to the requested width.
    private static string LowerAbbreviate(string levelKey, int width)
    {
        var canonical = TryGetCanonicalAbbreviation(levelKey, width);
        if (canonical is not null)
            return canonical.ToLowerInvariant();

        return TruncatePad(levelKey.ToLowerInvariant(), width);
    }

    // Truncate to exactly `width` chars, padding with 'X' (upper) or 'x' (lower)
    // when the source is shorter than the requested width.
    // Padding only arises for pathological short keys — the extra-level keys are
    // all 6+ characters so they never trigger padding at width 3.
    private static string TruncatePad(string source, int width)
    {
        if (source.Length >= width)
            return source.Substring(0, width);

        // Pad to requested width using the case of the existing chars.
        // Upper-case keys (all caps input) pad with 'X'; lower-case with 'x'.
        char pad = char.IsUpper(source.Length > 0 ? source[0] : 'X') ? 'X' : 'x';
        return source.PadRight(width, pad);
    }

    // ── Title-case mapping ────────────────────────────────────────────────────

    // Return the title-case display name for a level key.
    // Oracle-pinned for the six standard levels; extras use a simple
    // first-char-upper heuristic that matches their KnownLogLevels.DisplayName.
    //
    // Upgrade note for C# 14: the switch expression here is fine for the fixed
    // set; revisit if a future level registration API makes this table dynamic.
    private static string Titlecase(string levelKey) => levelKey switch
    {
        "verbose"     => "Verbose",
        "debug"       => "Debug",
        "information" => "Information",
        "warning"     => "Warning",
        "error"       => "Error",
        "fatal"       => "Fatal",
        "notice"      => "Notice",
        "success"     => "Success",
        "security"    => "Security",
        "metric"      => "Metric",
        _ when string.IsNullOrEmpty(levelKey) => levelKey,
        _ => char.ToUpperInvariant(levelKey[0]) + levelKey.Substring(1),
    };
}

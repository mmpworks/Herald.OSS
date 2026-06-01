#nullable enable

using System;
using System.Globalization;
using MMP.Herald.Levels;
using MMP.Herald.Serilog;
using MMP.Herald.Serilog.Events;

namespace Herald.OSS.Serilog.Expressions.Eval;

/// <summary>
/// Coercion rules for the expression evaluator. Centralises the
/// numeric-vs-string-vs-boolean decisions and the three-valued (Kleene) logic
/// so the divergence-risk semantics live in one auditable place.
///
/// <para>
/// Every helper treats <see cref="Undefined.Value"/> as a first-class outcome,
/// never as null and never as false (except at the final boolean coercion).
/// </para>
/// </summary>
internal static class Coerce
{
    /// <summary>True when the value is the undefined sentinel.</summary>
    public static bool IsUndefined(object? value) => ReferenceEquals(value, Undefined.Value);

    /// <summary>
    /// Coerce a value to a definite boolean for the top-level filter decision.
    /// Only a boxed <c>true</c> admits the event; <c>null</c>, undefined, and
    /// non-boolean values all read as <c>false</c>. This is the single point
    /// where undefined collapses to false — never mid-tree.
    /// </summary>
    public static bool ToFilterBool(object? value) => value is bool b && b;

    /// <summary>
    /// Kleene truthiness for use inside <c>and</c>/<c>or</c>/<c>not</c> and the
    /// ternary condition. Returns a nullable bool: <c>true</c>/<c>false</c> for
    /// definite booleans, and <c>null</c> for "indeterminate" — produced by
    /// undefined or by a non-boolean operand. The logical operators interpret
    /// that null per Kleene rules; they never silently treat it as false.
    /// </summary>
    public static bool? Truthiness(object? value) => value switch
    {
        bool b => b,
        _ => null, // undefined, null, or non-boolean → indeterminate
    };

    /// <summary>
    /// Try to read both operands as doubles for numeric comparison/arithmetic.
    /// Strings are NOT auto-parsed here — Serilog compares a number and a
    /// string lexically, not numerically. Only genuinely numeric CLR values
    /// (and the boxed double the parser emits for numeric literals) qualify.
    /// </summary>
    public static bool TryBothAsDouble(object? left, object? right, out double l, out double r)
    {
        l = 0;
        r = 0;
        return TryAsDouble(left, out l) && TryAsDouble(right, out r);
    }

    /// <summary>Read a single CLR value as a double when it is numeric.</summary>
    public static bool TryAsDouble(object? value, out double d)
    {
        switch (value)
        {
            case double dv: d = dv; return true;
            case float fv: d = fv; return true;
            case int iv: d = iv; return true;
            case long lv: d = lv; return true;
            case short sv: d = sv; return true;
            case byte yv: d = yv; return true;
            case decimal mv: d = (double)mv; return true;
            default:
                d = 0;
                return false;
        }
    }

    /// <summary>
    /// Equality with Serilog semantics: numeric when both sides are numbers,
    /// otherwise <b>case-SENSITIVE</b> string comparison by default. The
    /// <paramref name="caseInsensitive"/> flag (the <c>ci</c> modifier) forces
    /// an ordinal-ignore-case compare. Returns null (indeterminate) when either
    /// side is undefined — the comparison can't be made.
    /// </summary>
    public static bool? Equal(object? left, object? right, bool caseInsensitive)
    {
        if (IsUndefined(left) || IsUndefined(right))
            return null;

        if (left is null || right is null)
            return left is null && right is null;

        if (TryBothAsDouble(left, right, out var l, out var r))
            return l.Equals(r);

        if (left is bool lb && right is bool rb)
            return lb == rb;

        var ls = AsString(left);
        var rs = AsString(right);
        var comparison = caseInsensitive
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(ls, rs, comparison);
    }

    /// <summary>
    /// Ordering comparison (&lt; &lt;= &gt; &gt;=). Numeric when both sides are
    /// numbers, otherwise an ordinal (or ordinal-ignore-case) string compare.
    /// Returns null when undefined or when the values aren't comparable
    /// (e.g. one side null) so the operator yields undefined, not a wrong bool.
    /// </summary>
    public static int? Compare(object? left, object? right, bool caseInsensitive)
    {
        if (IsUndefined(left) || IsUndefined(right) || left is null || right is null)
            return null;

        if (TryBothAsDouble(left, right, out var l, out var r))
            return l.CompareTo(r);

        var comparison = caseInsensitive
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Compare(AsString(left), AsString(right), comparison);
    }

    /// <summary>
    /// F1 — severity-rank comparison for <c>@Level</c>.
    ///
    /// <para>
    /// Serilog's <c>@Level</c> is an enum; a bare string compare on Herald's
    /// level key would silently order levels alphabetically ("Error" &lt;
    /// "Warning"), dropping the wrong events in production. Instead, both sides
    /// map through <see cref="SerilogLevelMap"/> to the canonical Serilog
    /// ordinal (Verbose=0 … Fatal=5) and compare by rank.
    /// </para>
    ///
    /// <para>
    /// Returns null (→ undefined) when either side is not one of the six
    /// Serilog-mapped levels — a Herald-only level (notice/success/security/
    /// metric) or an unknown name has no Serilog rank, so a rank comparison is
    /// genuinely indeterminate rather than wrong.
    /// </para>
    /// </summary>
    public static int? CompareLevelRank(object? left, object? right)
    {
        if (!TryLevelOrdinal(left, out var l) || !TryLevelOrdinal(right, out var r))
            return null;
        return l.CompareTo(r);
    }

    /// <summary>
    /// F1 — <c>@Level</c> equality (<c>=</c>, <c>!=</c>) by NAME IDENTITY.
    ///
    /// <para>
    /// Equality does not need a Serilog rank — two levels are equal when they
    /// resolve to the same Herald level, regardless of whether that level has a
    /// Serilog ordinal. So a Herald-only level (notice/success/security/metric)
    /// compares by identity here, where <see cref="CompareLevelRank"/> would
    /// return null (no rank relationship to the Serilog scale).
    /// </para>
    ///
    /// <para>
    /// Both sides resolve through <see cref="LookupHeraldLevel"/> (key or
    /// display-name, case-insensitive) to a canonical <see cref="LogLevel.Key"/>,
    /// then compare ordinal-ignore-case — level identity is case-insensitive, so
    /// <c>@Level = 'Notice'</c> and <c>@Level = 'notice'</c> both match a Notice
    /// event. Returns null (→ undefined) when either side is not a known level
    /// name, matching the indeterminate semantics of the rank path.
    /// </para>
    /// </summary>
    public static bool? EqualLevelIdentity(object? left, object? right)
    {
        if (!TryLevelKey(left, out var lk) || !TryLevelKey(right, out var rk))
            return null;
        return string.Equals(lk, rk, StringComparison.OrdinalIgnoreCase);
    }

    // Resolve a value to its canonical Herald level key (case-insensitive on the
    // operand, by key or display-name). Unknown / non-string values return false.
    private static bool TryLevelKey(object? value, out string key)
    {
        key = string.Empty;
        if (value is not string name)
            return false;

        var heraldLevel = LookupHeraldLevel(name);
        if (heraldLevel is null)
            return false;

        key = heraldLevel.Key;
        return true;
    }

    // Map a value to its Serilog level ordinal. Accepts a level key string
    // (how @Level resolves — evt.Level.Key) and routes it through the canonical
    // Herald→Serilog map. Unknown / Herald-only levels return false.
    private static bool TryLevelOrdinal(object? value, out int ordinal)
    {
        ordinal = 0;
        if (value is not string key)
            return false;

        var heraldLevel = LookupHeraldLevel(key);
        if (heraldLevel is null)
            return false;

        if (SerilogLevelMap.TryToSerilog(heraldLevel, out var serilogLevel))
        {
            ordinal = (int)serilogLevel;
            return true;
        }
        return false;
    }

    // Resolve a level key (case-insensitive) to a known Herald LogLevel.
    // Comparison operands name levels by key on either side — the literal
    // 'Warning' and the runtime evt.Level.Key both arrive here.
    private static LogLevel? LookupHeraldLevel(string key)
    {
        foreach (var registered in DefaultLevels)
        {
            if (string.Equals(registered.Key, key, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(registered.DisplayName, key, StringComparison.OrdinalIgnoreCase))
            {
                return registered;
            }
        }
        return null;
    }

    // The six Serilog-mapped levels plus Herald's extras, so a literal naming a
    // Herald-only level resolves to a LogLevel (and then correctly produces a
    // null rank because it has no Serilog ordinal).
    private static readonly LogLevel[] DefaultLevels =
    [
        KnownLogLevels.Verbose,
        KnownLogLevels.Debug,
        KnownLogLevels.Information,
        KnownLogLevels.Warning,
        KnownLogLevels.Error,
        KnownLogLevels.Fatal,
        KnownLogLevels.Notice,
        KnownLogLevels.Success,
        KnownLogLevels.Security,
        KnownLogLevels.Metric,
    ];

    /// <summary>Render a value as a string with invariant-culture number formatting.</summary>
    public static string AsString(object? value) => value switch
    {
        null => string.Empty,
        string s => s,
        bool b => b ? "True" : "False",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };
}

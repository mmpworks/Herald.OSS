#nullable enable

using System;
using System.Text;
using System.Text.RegularExpressions;

namespace Herald.OSS.Serilog.Expressions.Eval;

/// <summary>
/// Compiled SQL-style <c>like</c> matcher. The wildcard pattern is translated
/// to a regex <b>once at config time</b> and reused per event — the hot path
/// is a single <see cref="Regex.IsMatch(string)"/>, never a re-parse of the
/// pattern string.
///
/// <para>
/// Wildcards follow Serilog.Expressions: <c>%</c> matches any run of
/// characters (including empty), <c>_</c> matches exactly one character. Every
/// other character is matched literally — regex metacharacters in the pattern
/// are escaped so a user's <c>like</c> string is never reinterpreted as regex.
/// </para>
///
/// <para>
/// The compiled regex carries the same 200&#160;ms catastrophic-backtracking
/// timeout the Query evaluator uses for its regex operator. The pattern is
/// anchored start-to-end so <c>like</c> is a full-string match, matching
/// Serilog semantics rather than substring containment.
/// </para>
/// </summary>
internal sealed class LikeMatcher
{
    // Mirrors QueryEvaluator.RegexTimeout — user-supplied patterns are a DoS
    // surface, so every compiled matcher is bounded.
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(200);

    private readonly Regex _regex;

    /// <summary>The original wildcard pattern, preserved for diagnostics.</summary>
    public string Pattern { get; }

    /// <summary>True when matching is case-insensitive (the <c>ci</c> modifier).</summary>
    public bool CaseInsensitive { get; }

    public LikeMatcher(string pattern, bool caseInsensitive)
    {
        Pattern = pattern ?? throw new ArgumentNullException(nameof(pattern));
        CaseInsensitive = caseInsensitive;

        var options = RegexOptions.CultureInvariant | RegexOptions.Singleline;
        if (caseInsensitive)
            options |= RegexOptions.IgnoreCase;

        _regex = new Regex(Translate(pattern), options, MatchTimeout);
    }

    /// <summary>
    /// True when <paramref name="input"/> matches the wildcard pattern in full.
    /// A timeout is treated as no-match rather than throwing, matching the
    /// Query evaluator's defensive posture on user-supplied patterns.
    /// </summary>
    public bool IsMatch(string input)
    {
        try
        {
            return _regex.IsMatch(input);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    // Translate a SQL-wildcard pattern to an anchored regex. Every character
    // is escaped to its literal regex form except the two wildcards. Kept
    // inline — the surface is tiny and a helper class would add indirection.
    private static string Translate(string pattern)
    {
        var sb = new StringBuilder(pattern.Length + 4);
        sb.Append('^');
        foreach (var c in pattern)
        {
            switch (c)
            {
                case '%':
                    sb.Append(".*");
                    break;
                case '_':
                    sb.Append('.');
                    break;
                default:
                    sb.Append(Regex.Escape(c.ToString()));
                    break;
            }
        }
        sb.Append('$');
        return sb.ToString();
    }
}

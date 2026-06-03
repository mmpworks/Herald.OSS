#nullable enable

using System;
using System.Collections.Generic;

namespace Herald.OSS.Serilog.Expressions.Eval;

/// <summary>
/// A built-in function: takes already-evaluated argument values and returns a
/// result value (which may be <see cref="Undefined.Value"/>).
/// </summary>
internal delegate object? BuiltinFunction(IReadOnlyList<object?> args);

/// <summary>
/// The Tier-1 built-in function catalogue. Names resolve to delegates at
/// <b>parse</b> time via <see cref="TryResolve"/> — an unknown function name
/// fails at config, not per event. Tier-3 long-tail functions (Round, Now,
/// UtcDateTime, …) are intentionally absent; they fail-loud as unknown.
///
/// <para>
/// Most string functions propagate undefined: applied to an undefined or null
/// target they return undefined rather than fabricating a result. This keeps
/// <c>StartsWith(@Properties['absent'], 'x')</c> filtering the event out
/// instead of silently reading as false-from-empty-string.
/// </para>
/// </summary>
internal static class BuiltinFunctions
{
    private static readonly IReadOnlyDictionary<string, BuiltinFunction> Table =
        new Dictionary<string, BuiltinFunction>(StringComparer.OrdinalIgnoreCase)
        {
            ["StartsWith"] = StartsWith,
            ["EndsWith"] = EndsWith,
            ["Contains"] = Contains,
            ["IndexOf"] = IndexOf,
            ["Length"] = Length,
            ["Substring"] = Substring,
            ["ToString"] = ToStringFn,
            ["Coalesce"] = Coalesce,
            ["ElementAt"] = ElementAt,
            ["TypeOf"] = TypeOf,
            ["IsDefined"] = IsDefined,
        };

    /// <summary>
    /// Resolve a function name to its delegate. Case-insensitive (Serilog
    /// function names are). Returns false for unknown names so the parser can
    /// raise a config-time failure.
    /// </summary>
    public static bool TryResolve(string name, out BuiltinFunction function) =>
        Table.TryGetValue(name, out function!);

    // --- string predicates -------------------------------------------------

    private static object? StartsWith(IReadOnlyList<object?> a) =>
        StringPredicate(a, static (s, sub, cmp) => s.StartsWith(sub, cmp));

    private static object? EndsWith(IReadOnlyList<object?> a) =>
        StringPredicate(a, static (s, sub, cmp) => s.EndsWith(sub, cmp));

    private static object? Contains(IReadOnlyList<object?> a) =>
        StringPredicate(a, static (s, sub, cmp) => s.IndexOf(sub, cmp) >= 0);

    // StartsWith/EndsWith/Contains share the same shape: two string args, an
    // optional ci flag (third arg, boolean). Undefined/null target → undefined.
    private static object? StringPredicate(
        IReadOnlyList<object?> a,
        Func<string, string, StringComparison, bool> apply)
    {
        if (a.Count < 2) return Undefined.Value;
        if (Coerce.IsUndefined(a[0]) || Coerce.IsUndefined(a[1])) return Undefined.Value;
        if (a[0] is null || a[1] is null) return Undefined.Value;

        var s = Coerce.AsString(a[0]);
        var sub = Coerce.AsString(a[1]);
        var cmp = CaseFlag(a, 2)
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return apply(s, sub, cmp);
    }

    private static object? IndexOf(IReadOnlyList<object?> a)
    {
        if (a.Count < 2 || Coerce.IsUndefined(a[0]) || Coerce.IsUndefined(a[1])) return Undefined.Value;
        if (a[0] is null || a[1] is null) return Undefined.Value;

        var cmp = CaseFlag(a, 2) ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return (double)Coerce.AsString(a[0]).IndexOf(Coerce.AsString(a[1]), cmp);
    }

    private static object? Length(IReadOnlyList<object?> a)
    {
        if (a.Count < 1 || Coerce.IsUndefined(a[0]) || a[0] is null) return Undefined.Value;
        return a[0] switch
        {
            string s => (double)s.Length,
            System.Collections.ICollection c => (double)c.Count,
            _ => (double)Coerce.AsString(a[0]).Length,
        };
    }

    private static object? Substring(IReadOnlyList<object?> a)
    {
        if (a.Count < 2 || Coerce.IsUndefined(a[0]) || a[0] is null) return Undefined.Value;
        if (!Coerce.TryAsDouble(a[1], out var startD)) return Undefined.Value;

        var s = Coerce.AsString(a[0]);
        var start = (int)startD;
        if (start < 0 || start > s.Length) return Undefined.Value;

        if (a.Count >= 3 && Coerce.TryAsDouble(a[2], out var lenD))
        {
            var len = (int)lenD;
            if (len < 0 || start + len > s.Length) return Undefined.Value;
            return s.Substring(start, len);
        }
        return s.Substring(start);
    }

    // --- value functions ---------------------------------------------------

    private static object? ToStringFn(IReadOnlyList<object?> a)
    {
        if (a.Count < 1 || Coerce.IsUndefined(a[0]) || a[0] is null) return Undefined.Value;
        return Coerce.AsString(a[0]);
    }

    private static object? Coalesce(IReadOnlyList<object?> a)
    {
        foreach (var v in a)
        {
            if (!Coerce.IsUndefined(v) && v is not null)
                return v;
        }
        return Undefined.Value;
    }

    private static object? ElementAt(IReadOnlyList<object?> a)
    {
        if (a.Count < 2 || Coerce.IsUndefined(a[0]) || a[0] is null) return Undefined.Value;
        if (!Coerce.TryAsDouble(a[1], out var idxD)) return Undefined.Value;

        var idx = (int)idxD;
        if (a[0] is System.Collections.IList list)
            return idx >= 0 && idx < list.Count ? list[idx] : Undefined.Value;
        if (a[0] is string s)
            return idx >= 0 && idx < s.Length ? s[idx].ToString() : Undefined.Value;
        return Undefined.Value;
    }

    private static object? TypeOf(IReadOnlyList<object?> a)
    {
        if (a.Count < 1 || Coerce.IsUndefined(a[0])) return "undefined";
        return a[0] switch
        {
            null => "Null",
            bool => "System.Boolean",
            string => "System.String",
            double or float or int or long or short or byte or decimal => "System.Double",
            _ => a[0]!.GetType().FullName ?? "object",
        };
    }

    private static object? IsDefined(IReadOnlyList<object?> a)
    {
        if (a.Count < 1) return false;
        return !Coerce.IsUndefined(a[0]);
    }

    // A trailing boolean argument at position `index` toggles case-insensitive
    // comparison — the function-call form of the `ci` collation modifier.
    private static bool CaseFlag(IReadOnlyList<object?> a, int index) =>
        a.Count > index && a[index] is bool b && b;
}

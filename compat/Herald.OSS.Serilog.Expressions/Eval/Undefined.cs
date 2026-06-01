#nullable enable

namespace Herald.OSS.Serilog.Expressions.Eval;

/// <summary>
/// The <c>undefined</c> sentinel — distinct from <c>null</c>.
///
/// <para>
/// Serilog.Expressions has three-valued logic. A property that is present but
/// null is <c>null</c>; a property that is <b>absent</b> is <c>undefined</c>.
/// They behave differently: <c>@Properties['absent'] = 'x'</c> is undefined
/// (the comparison can't be made), and <c>not(undefined)</c> stays undefined
/// rather than flipping to true. Collapsing missing → null → false naively is
/// the exact silent-divergence the port must avoid, so the evaluator carries a
/// dedicated sentinel and never substitutes null for it.
/// </para>
///
/// <para>
/// A boolean filter coerces a final <c>undefined</c> result to <c>false</c>
/// (the event is not admitted), but only at the very top — never mid-tree.
/// </para>
/// </summary>
internal sealed class Undefined
{
    /// <summary>The single shared sentinel instance. Reference-compared.</summary>
    public static readonly Undefined Value = new();

    private Undefined() { }

    public override string ToString() => "undefined";
}

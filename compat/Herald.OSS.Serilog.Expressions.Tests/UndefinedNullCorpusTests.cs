#nullable enable

using System.Collections.Generic;
using FluentAssertions;
using Herald.OSS.Serilog.Expressions.Filtering;
using Xunit;

namespace Herald.OSS.Serilog.Expressions.Tests;

/// <summary>
/// Undefined-vs-null three-valued (Kleene) logic. A missing property is
/// <c>undefined</c>, distinct from a present-but-null property. Collapsing
/// missing → null → false naively is a silent divergence; this corpus pins the
/// distinction.
/// </summary>
public sealed class UndefinedNullCorpusTests
{
    private static bool AdmitsInclude(string expression, params (string, object?)[] props)
    {
        var filter = Filter.ByIncludingOnly(expression);
        return filter.Allow(EventBuilder.Build(properties: new List<(string, object?)>(props)));
    }

    [Fact]
    public void Absent_property_compare_is_undefined_not_match()
    {
        // @Properties['absent'] = 'x' is undefined → ByIncludingOnly rejects.
        AdmitsInclude("@Properties['absent'] = 'x'").Should().BeFalse();
    }

    [Fact]
    public void Absent_property_bare_name_is_undefined()
    {
        AdmitsInclude("Missing = 'x'").Should().BeFalse();
    }

    [Fact]
    public void Present_property_matches()
    {
        AdmitsInclude("@Properties['k'] = 'x'", ("k", "x")).Should().BeTrue();
    }

    [Fact]
    public void Not_of_undefined_stays_undefined_not_true()
    {
        // not(undefined) must NOT flip to true. ByIncludingOnly("not(absent='x')")
        // would WRONGLY admit if not(undefined) became true. It must stay
        // undefined → reject.
        AdmitsInclude("not (@Properties['absent'] = 'x')").Should().BeFalse();
    }

    [Fact]
    public void Undefined_and_false_is_false()
    {
        // Kleene: undefined and false = false. The whole expression is false →
        // reject, and crucially it does not throw or admit.
        AdmitsInclude("@Properties['absent'] = 'x' and false").Should().BeFalse();
    }

    [Fact]
    public void Undefined_and_true_is_undefined()
    {
        // Kleene: undefined and true = undefined → reject (not admit).
        AdmitsInclude("@Properties['absent'] = 'x' and true").Should().BeFalse();
    }

    [Fact]
    public void Undefined_or_true_is_true()
    {
        // Kleene: undefined or true = true → admit.
        AdmitsInclude("@Properties['absent'] = 'x' or true").Should().BeTrue();
    }

    [Fact]
    public void Undefined_or_false_is_undefined()
    {
        // Kleene: undefined or false = undefined → reject.
        AdmitsInclude("@Properties['absent'] = 'x' or false").Should().BeFalse();
    }

    [Fact]
    public void IsDefined_distinguishes_present_from_absent()
    {
        AdmitsInclude("IsDefined(@Properties['k'])", ("k", "v")).Should().BeTrue();
        AdmitsInclude("IsDefined(@Properties['absent'])").Should().BeFalse();
    }

    [Fact]
    public void Null_value_is_not_undefined()
    {
        // A present-but-null property is null, not undefined. is null is true;
        // IsDefined is true (it exists).
        AdmitsInclude("@Properties['k'] is null", ("k", null)).Should().BeTrue();
        AdmitsInclude("IsDefined(@Properties['k'])", ("k", null)).Should().BeTrue();
    }

    [Fact]
    public void Absent_is_not_null_because_it_is_undefined()
    {
        // is null tests CLR null specifically; an absent (undefined) property is
        // NOT null.
        AdmitsInclude("@Properties['absent'] is null").Should().BeFalse();
    }

    [Fact]
    public void Coalesce_falls_through_undefined()
    {
        // absent ?? 'fallback' = 'fallback'
        AdmitsInclude("Coalesce(@Properties['absent'], 'fallback') = 'fallback'").Should().BeTrue();
        AdmitsInclude("(@Properties['absent'] ?? 'fallback') = 'fallback'").Should().BeTrue();
    }
}

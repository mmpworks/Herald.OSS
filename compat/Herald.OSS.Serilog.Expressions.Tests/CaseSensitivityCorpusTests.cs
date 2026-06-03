#nullable enable

using System.Collections.Generic;
using FluentAssertions;
using Herald.OSS.Serilog.Expressions.Filtering;
using Xunit;

namespace Herald.OSS.Serilog.Expressions.Tests;

/// <summary>
/// Case sensitivity. Serilog.Expressions <c>=</c> is case-SENSITIVE by default;
/// the <c>ci</c> modifier forces case-insensitive. This inverts Herald's Query
/// DSL default (which is case-insensitive), so the corpus pins the Serilog
/// behaviour for this compat surface.
/// </summary>
public sealed class CaseSensitivityCorpusTests
{
    private static bool Admits(string expression, params (string, object?)[] props)
    {
        var filter = Filter.ByIncludingOnly(expression);
        return filter.Allow(EventBuilder.Build(properties: new List<(string, object?)>(props)));
    }

    [Fact]
    public void Equality_is_case_sensitive_by_default()
    {
        Admits("@Properties['k'] = 'abc'", ("k", "abc")).Should().BeTrue();
        Admits("@Properties['k'] = 'ABC'", ("k", "abc")).Should().BeFalse();
        Admits("@Properties['k'] = 'Abc'", ("k", "abc")).Should().BeFalse();
    }

    [Fact]
    public void Ci_modifier_forces_case_insensitive_equality()
    {
        Admits("@Properties['k'] = 'ABC' ci", ("k", "abc")).Should().BeTrue();
        Admits("@Properties['k'] = 'aBc' ci", ("k", "ABC")).Should().BeTrue();
    }

    [Fact]
    public void Literal_case_compare_respects_default_and_ci()
    {
        Admits("'A' = 'a'").Should().BeFalse();
        Admits("'A' = 'a' ci").Should().BeTrue();
    }

    [Fact]
    public void Like_is_case_sensitive_by_default()
    {
        Admits("@Properties['p'] like 'Hello%'", ("p", "Hello World")).Should().BeTrue();
        Admits("@Properties['p'] like 'hello%'", ("p", "Hello World")).Should().BeFalse();
    }

    [Fact]
    public void Like_ci_modifier_forces_case_insensitive()
    {
        Admits("@Properties['p'] like 'hello%' ci", ("p", "Hello World")).Should().BeTrue();
    }

    [Fact]
    public void In_is_case_sensitive_by_default()
    {
        Admits("@Properties['k'] in ['A', 'B']", ("k", "A")).Should().BeTrue();
        Admits("@Properties['k'] in ['A', 'B']", ("k", "a")).Should().BeFalse();
        Admits("@Properties['k'] in ['A', 'B'] ci", ("k", "a")).Should().BeTrue();
    }
}

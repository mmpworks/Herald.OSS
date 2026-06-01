#nullable enable

using System.Collections.Generic;
using FluentAssertions;
using Herald.OSS.Serilog.Expressions.Filtering;
using Xunit;

namespace Herald.OSS.Serilog.Expressions.Tests;

/// <summary>
/// Common operators: arithmetic, numeric vs string comparison, <c>in</c>,
/// ternary, coalesce, the high-frequency builtins, and the <c>@</c>-accessors.
/// </summary>
public sealed class OperatorCorpusTests
{
    private static bool Admits(string expression, params (string, object?)[] props)
    {
        var filter = Filter.ByIncludingOnly(expression);
        return filter.Allow(EventBuilder.Build(
            message: "hello world",
            properties: new List<(string, object?)>(props)));
    }

    private static bool AdmitsMsg(string expression, string message)
    {
        var filter = Filter.ByIncludingOnly(expression);
        return filter.Allow(EventBuilder.Build(message: message));
    }

    [Theory]
    // Numeric comparison — properties carrying numeric CLR values.
    [InlineData("@Properties['n'] > 3", true)]
    [InlineData("@Properties['n'] >= 5", true)]
    [InlineData("@Properties['n'] < 5", false)]
    [InlineData("@Properties['n'] = 5", true)]
    public void Numeric_comparison(string expression, bool expected) =>
        Admits(expression, ("n", 5)).Should().Be(expected);

    [Theory]
    // Arithmetic produces values that comparisons then test.
    [InlineData("@Properties['n'] + 1 = 6", true)]
    [InlineData("@Properties['n'] * 2 = 10", true)]
    [InlineData("@Properties['n'] - 2 > 2", true)]
    [InlineData("@Properties['n'] % 2 = 1", true)]
    [InlineData("10 / @Properties['n'] = 2", true)]
    [InlineData("-@Properties['n'] = -5", true)]
    public void Arithmetic(string expression, bool expected) =>
        Admits(expression, ("n", 5)).Should().Be(expected);

    [Fact]
    public void Divide_by_zero_is_undefined_not_throw()
    {
        // x / 0 → undefined → the equality is undefined → reject (no crash).
        Admits("@Properties['n'] / 0 = 0", ("n", 5)).Should().BeFalse();
    }

    [Theory]
    [InlineData("@Properties['k'] in ['a', 'b', 'c']", true)]
    [InlineData("@Properties['k'] in ['x', 'y']", false)]
    public void In_membership(string expression, bool expected) =>
        Admits(expression, ("k", "b")).Should().Be(expected);

    [Theory]
    [InlineData("@Properties['n'] > 3 ? true : false", true)]
    [InlineData("@Properties['n'] < 3 ? true : false", false)]
    public void Ternary(string expression, bool expected) =>
        Admits(expression, ("n", 5)).Should().Be(expected);

    [Theory]
    [InlineData("StartsWith(@Message, 'hello')", "hello world", true)]
    [InlineData("StartsWith(@Message, 'world')", "hello world", false)]
    [InlineData("EndsWith(@Message, 'world')", "hello world", true)]
    [InlineData("Contains(@Message, 'lo wo')", "hello world", true)]
    [InlineData("Length(@Message) = 11", "hello world", true)]
    [InlineData("Substring(@Message, 0, 5) = 'hello'", "hello world", true)]
    [InlineData("IndexOf(@Message, 'world') = 6", "hello world", true)]
    public void Builtins_over_message(string expression, string message, bool expected) =>
        AdmitsMsg(expression, message).Should().Be(expected);

    [Fact]
    public void Accessor_message_resolves()
    {
        AdmitsMsg("@Message = 'exact'", "exact").Should().BeTrue();
    }

    [Fact]
    public void Exception_accessor_resolves_and_is_undefined_when_absent()
    {
        var withEx = Filter.ByIncludingOnly("@Exception is null");
        withEx.Allow(EventBuilder.Build(exception: "boom")).Should().BeFalse();
        // Absent exception → @Exception is undefined → is null is false; but the
        // event has no exception so IsDefined is the right probe.
        var hasEx = Filter.ByIncludingOnly("IsDefined(@Exception)");
        hasEx.Allow(EventBuilder.Build(exception: "boom")).Should().BeTrue();
        hasEx.Allow(EventBuilder.Build()).Should().BeFalse();
    }

    [Theory]
    [InlineData("@Properties['k'] = 'a' and @Properties['n'] > 3", true)]
    [InlineData("@Properties['k'] = 'a' and @Properties['n'] > 9", false)]
    [InlineData("@Properties['k'] = 'z' or @Properties['n'] > 3", true)]
    [InlineData("not (@Properties['n'] > 9)", true)]
    public void Boolean_composition(string expression, bool expected) =>
        Admits(expression, ("k", "a"), ("n", 5)).Should().Be(expected);

    [Theory]
    [InlineData("@Properties['n'] != 5", false)]
    [InlineData("@Properties['n'] != 6", true)]
    public void NotEqual(string expression, bool expected) =>
        Admits(expression, ("n", 5)).Should().Be(expected);
}

#nullable enable

using System.Collections.Generic;
using FluentAssertions;
using Herald.OSS.Serilog.Expressions.Filtering;
using Herald.OSS.Serilog.Expressions.Parsing;
using MMP.Herald.Filters;
using Xunit;

namespace Herald.OSS.Serilog.Expressions.Tests;

/// <summary>
/// The public API surface: <c>Filter.ByExcluding</c> / <c>ByIncludingOnly</c>
/// returning an <see cref="ILogFilter"/>, and config-time fail-loud behaviour.
/// </summary>
public sealed class FilterApiTests
{
    private static MMP.Herald.Events.LogEvent EventWith(string path) =>
        EventBuilder.Build(properties: new List<(string, object?)> { ("RequestPath", path) });

    [Fact]
    public void ByExcluding_admits_all_except_matches()
    {
        var filter = Filter.ByExcluding("RequestPath like '/health%'");
        filter.Allow(EventWith("/health/ready")).Should().BeFalse(); // matches → excluded
        filter.Allow(EventWith("/api/orders")).Should().BeTrue();    // no match → admitted
    }

    [Fact]
    public void ByIncludingOnly_admits_only_matches()
    {
        var filter = Filter.ByIncludingOnly("RequestPath like '/api%'");
        filter.Allow(EventWith("/api/orders")).Should().BeTrue();
        filter.Allow(EventWith("/health")).Should().BeFalse();
    }

    [Fact]
    public void Factory_returns_ILogFilter()
    {
        Filter.ByExcluding("@Level = 'Error'").Should().BeAssignableTo<ILogFilter>();
        Filter.ByIncludingOnly("@Level = 'Error'").Should().BeAssignableTo<ILogFilter>();
    }

    [Theory]
    [InlineData("@Level >=")]            // dangling operator
    [InlineData("@Bogus = 'x'")]         // unknown accessor
    [InlineData("NotAFunction(@Level)")] // unknown function
    [InlineData("(@Level = 'Error'")]    // unbalanced paren
    public void Invalid_expression_fails_at_config_time(string expression)
    {
        // Fail-loud at construction — never a silent no-op, never deferred to
        // first log call.
        var act = () => Filter.ByIncludingOnly(expression);
        act.Should().Throw<ExpressionParseException>();
    }

    [Fact]
    public void Like_with_nonliteral_pattern_fails_at_config_time()
    {
        // The like pattern must be a literal so the matcher compiles once.
        var act = () => Filter.ByIncludingOnly("@Message like @Message");
        act.Should().Throw<ExpressionParseException>();
    }

    [Fact]
    public void Whitespace_expression_rejected()
    {
        var act = () => Filter.ByExcluding("   ");
        act.Should().Throw<System.ArgumentException>();
    }
}

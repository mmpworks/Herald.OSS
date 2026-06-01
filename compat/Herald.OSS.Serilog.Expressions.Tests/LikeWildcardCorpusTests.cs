#nullable enable

using System.Collections.Generic;
using FluentAssertions;
using Herald.OSS.Serilog.Expressions.Filtering;
using Xunit;

namespace Herald.OSS.Serilog.Expressions.Tests;

/// <summary>
/// SQL-style <c>like</c> wildcards: <c>%</c> matches any run (including empty),
/// <c>_</c> matches exactly one character. <c>like</c> is a full-string match,
/// not substring containment. Regex metacharacters in the pattern are matched
/// literally.
/// </summary>
public sealed class LikeWildcardCorpusTests
{
    private static bool Admits(string expression, string value)
    {
        var filter = Filter.ByIncludingOnly(expression);
        return filter.Allow(EventBuilder.Build(
            properties: new List<(string, object?)> { ("p", value) }));
    }

    [Theory]
    // % — any run of characters.
    [InlineData("@Properties['p'] like '/health%'", "/health", true)]
    [InlineData("@Properties['p'] like '/health%'", "/health/ready", true)]
    [InlineData("@Properties['p'] like '/health%'", "/healthz", true)]
    [InlineData("@Properties['p'] like '/health%'", "/api/health", false)]
    [InlineData("@Properties['p'] like '%health%'", "/api/health/x", true)]
    // _ — exactly one character.
    [InlineData("@Properties['p'] like 'a_c'", "abc", true)]
    [InlineData("@Properties['p'] like 'a_c'", "ac", false)]
    [InlineData("@Properties['p'] like 'a_c'", "abbc", false)]
    // Full-string match — not substring.
    [InlineData("@Properties['p'] like 'cat'", "cat", true)]
    [InlineData("@Properties['p'] like 'cat'", "category", false)]
    // Regex metacharacters are literal.
    [InlineData("@Properties['p'] like 'a.c'", "a.c", true)]
    [InlineData("@Properties['p'] like 'a.c'", "abc", false)]
    [InlineData("@Properties['p'] like 'price$%'", "price$10", true)]
    public void Like_wildcards_match_sql_semantics(string expression, string value, bool expected) =>
        Admits(expression, value).Should().Be(expected);

    [Theory]
    [InlineData("@Properties['p'] not like '/health%'", "/health", false)]
    [InlineData("@Properties['p'] not like '/health%'", "/api", true)]
    public void Not_like_negates(string expression, string value, bool expected) =>
        Admits(expression, value).Should().Be(expected);
}

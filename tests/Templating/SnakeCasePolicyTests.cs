#nullable enable

using System.Collections.Generic;
using FluentAssertions;
using MMP.Herald.Templating;
using MMP.Herald.Templating.NamingPolicies;
using Xunit;

namespace MMP.Herald.OSS.Tests.Templating;

/// <summary>
/// Verifies <see cref="SnakeCasePolicy"/> against the spec — template tokens
/// win, converted to <c>snake_case</c>, with uppercase-run coalescing
/// (<c>HTTPClient</c> → <c>http_client</c>, not <c>h_t_t_p_client</c>).
/// </summary>
public sealed class SnakeCasePolicyTests
{
    [Theory]
    [MemberData(nameof(EdgeCases))]
    public void Resolves_token_per_spec(string token, string argExpr, string expectedSnake)
    {
        var (tokens, argExprs) = PolicyTestData.Pair(token, argExpr);

        var result = SnakeCasePolicy.Instance.ResolveAll(tokens, argExprs);

        result.Should().ContainSingle().Which.Should().Be(expectedSnake);
    }

    [Theory]
    [InlineData("user_id",           "user_id")]
    [InlineData("already_snake",     "already_snake")]
    [InlineData("user_id_value",     "user_id_value")]
    public void Idempotent_on_already_snake_input(string source, string expected)
    {
        var tokens = new[]
        {
            new MessageTemplateToken.Property(source, LogPropertyCaptureMode.Default, null, "{" + source + "}"),
        };
        var args = new[] { "v" };

        var result = SnakeCasePolicy.Instance.ResolveAll(tokens, args);

        result.Should().ContainSingle().Which.Should().Be(expected);
    }

    [Fact]
    public void Falls_back_to_arg_expr_when_token_is_empty()
    {
        var empty = new MessageTemplateToken.Property[0];
        var args = new[] { "userIdValue" };

        var result = SnakeCasePolicy.Instance.ResolveAll(empty, args);

        result.Should().ContainSingle().Which.Should().Be("user_id_value");
    }

    [Fact]
    public void Id_is_snake()
    {
        SnakeCasePolicy.Instance.Id.Should().Be("snake");
    }

    public static IEnumerable<object[]> EdgeCases()
    {
        foreach (var row in PolicyTestData.EdgeCases)
        {
            yield return new object[] { row.Token, row.Arg, row.Snake };
        }
    }
}

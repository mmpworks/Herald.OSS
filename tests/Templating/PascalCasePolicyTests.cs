#nullable enable

using System.Collections.Generic;
using FluentAssertions;
using MMP.Herald.Templating;
using MMP.Herald.Templating.NamingPolicies;
using Xunit;

namespace MMP.Herald.OSS.Tests.Templating;

/// <summary>
/// Verifies <see cref="PascalCasePolicy"/> behaves per the ratified spec
/// against the shared <see cref="PolicyTestData.EdgeCases"/> matrix.
/// </summary>
public sealed class PascalCasePolicyTests
{
    [Theory]
    [MemberData(nameof(EdgeCases))]
    public void Resolves_token_per_spec(string token, string argExpr, string expectedPascal)
    {
        var (tokens, argExprs) = PolicyTestData.Pair(token, argExpr);

        var result = PascalCasePolicy.Instance.ResolveAll(tokens, argExprs);

        result.Should().ContainSingle().Which.Should().Be(expectedPascal);
    }

    [Fact]
    public void Falls_back_to_arg_expr_when_token_is_empty()
    {
        // Empty-token case: when the template has fewer tokens than args, the
        // policy must derive a name from the caller-argument-expression.
        var empty = new MessageTemplateToken.Property[0];
        var args = new[] { "userId" };

        var result = PascalCasePolicy.Instance.ResolveAll(empty, args);

        result.Should().ContainSingle().Which.Should().Be("UserId");
    }

    [Fact]
    public void Falls_back_to_argN_when_both_token_and_arg_expr_are_empty()
    {
        var empty = new MessageTemplateToken.Property[0];
        var args = new[] { string.Empty };

        var result = PascalCasePolicy.Instance.ResolveAll(empty, args);

        result.Should().ContainSingle().Which.Should().Be("Arg1");
    }

    [Fact]
    public void Returned_array_has_one_entry_per_arg_slot()
    {
        var tokens = new[]
        {
            new MessageTemplateToken.Property("A", LogPropertyCaptureMode.Default, null, "{A}"),
            new MessageTemplateToken.Property("B", LogPropertyCaptureMode.Default, null, "{B}"),
        };
        var args = new[] { "first", "second", "third" };

        var result = PascalCasePolicy.Instance.ResolveAll(tokens, args);

        result.Should().HaveCount(3);
        result[0].Should().Be("A");
        result[1].Should().Be("B");
        result[2].Should().Be("Third"); // falls back to argExpr, PascalCased
    }

    [Fact]
    public void Id_is_pascal()
    {
        PascalCasePolicy.Instance.Id.Should().Be("pascal");
    }

    public static IEnumerable<object[]> EdgeCases()
    {
        foreach (var row in PolicyTestData.EdgeCases)
        {
            yield return new object[] { row.Token, row.Arg, row.Pascal };
        }
    }
}

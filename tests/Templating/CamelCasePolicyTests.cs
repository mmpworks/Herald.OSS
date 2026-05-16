#nullable enable

using System.Collections.Generic;
using FluentAssertions;
using MMP.Herald.Templating;
using MMP.Herald.Templating.NamingPolicies;
using Xunit;

namespace MMP.Herald.OSS.Tests.Templating;

/// <summary>
/// Verifies <see cref="CamelCasePolicy"/> behaves per the ratified spec —
/// caller-argument-expression name wins, falling back to template token when
/// the call site supplied no expression. Preserves pre-1.0 Herald behavior.
/// </summary>
public sealed class CamelCasePolicyTests
{
    [Theory]
    [MemberData(nameof(EdgeCases))]
    public void Resolves_name_per_spec(string token, string argExpr, string expectedCamel)
    {
        var (tokens, argExprs) = PolicyTestData.Pair(token, argExpr);

        var result = CamelCasePolicy.Instance.ResolveAll(tokens, argExprs);

        result.Should().ContainSingle().Which.Should().Be(expectedCamel);
    }

    [Fact]
    public void Falls_back_to_token_when_arg_expr_is_empty()
    {
        // When the caller-argument-expression is empty (legacy callers, or
        // hand-rolled IL), Camel must still produce a name — and the closest
        // signal available is the template token.
        var tokens = new[]
        {
            new MessageTemplateToken.Property("FallbackName", LogPropertyCaptureMode.Default, null, "{FallbackName}"),
        };
        var args = new[] { string.Empty };

        var result = CamelCasePolicy.Instance.ResolveAll(tokens, args);

        result.Should().ContainSingle().Which.Should().Be("FallbackName");
    }

    [Fact]
    public void Falls_back_to_argN_when_both_signals_are_empty()
    {
        var empty = new MessageTemplateToken.Property[0];
        var args = new[] { string.Empty };

        var result = CamelCasePolicy.Instance.ResolveAll(empty, args);

        result.Should().ContainSingle().Which.Should().Be("arg1");
    }

    [Fact]
    public void Id_is_camel()
    {
        CamelCasePolicy.Instance.Id.Should().Be("camel");
    }

    public static IEnumerable<object[]> EdgeCases()
    {
        foreach (var row in PolicyTestData.EdgeCases)
        {
            yield return new object[] { row.Token, row.Arg, row.Camel };
        }
    }
}

// Copyright (c) 2026 MMPWorks LLC
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.
#nullable enable

using System;
using System.Collections.Generic;
using FluentAssertions;
using MMP.Herald.Templating;
using MMP.Herald.Templating.NamingPolicies;
using Xunit;

namespace MMP.Herald.OSS.Tests.Templating;

/// <summary>
/// Drives every row in <see cref="CompileTimeNameResolverFixtures"/> through
/// the runtime <see cref="IPropertyNamingPolicy.ResolveAll"/> implementations
/// (<see cref="PascalCasePolicy"/>, <see cref="SnakeCasePolicy"/>,
/// <see cref="CamelCasePolicy"/>).
///
/// <para>
/// This is the second half of the drift-detection pair. The build-time path
/// and the runtime path must produce byte-identical output for every fixture
/// row both can reach. Runtime-only rows (empty-CAE fallback) exercise the
/// asymmetric axis the build-time path cannot reach.
/// </para>
///
/// <para>
/// The cache is not exercised here — these tests drive <c>ResolveAll</c>
/// directly so the policy logic is isolated from cache behaviour (covered
/// separately in <c>NameResolverCacheTests</c>).
/// </para>
/// </summary>
public sealed class PolicyResolveAllFixtureTests
{
    public static IEnumerable<object[]> AllRows()
    {
        foreach (var row in CompileTimeNameResolverFixtures.All)
        {
            yield return new object[] { row };
        }
    }

    [Theory]
    [MemberData(nameof(AllRows))]
    public void ResolveAll_matches_fixture(CompileTimeNameResolverFixtures.Row row)
    {
        // Parse the template the same way the runtime cache would. The
        // runtime ResolveAll signature takes MessageTemplateToken.Property —
        // the cache extracts those after parsing, and that's exactly what we
        // replicate here so the test isolates the policy logic from the
        // parser/extractor.
        var parser = new MessageTemplateParser();
        var parsed = parser.Parse(row.Template);

        var tokensList = new List<MessageTemplateToken.Property>();
        foreach (var t in parsed.Tokens)
        {
            if (t is MessageTemplateToken.Property p) tokensList.Add(p);
        }
        var tokens = tokensList.ToArray();

        IPropertyNamingPolicy policy = row.PolicyId switch
        {
            "pascal" => PascalCasePolicy.Instance,
            "snake"  => SnakeCasePolicy.Instance,
            "camel"  => CamelCasePolicy.Instance,
            _ => throw new InvalidOperationException(
                $"Fixture row '{row.Description}' uses unsupported policy id '{row.PolicyId}'"),
        };

        var actual = policy.ResolveAll(tokens.AsSpan(), row.CaeNames.AsSpan());

        actual.Should().Equal(
            row.Expected,
            "fixture '{0}' under runtime policy {1}", row.Description, row.PolicyId);
    }
}

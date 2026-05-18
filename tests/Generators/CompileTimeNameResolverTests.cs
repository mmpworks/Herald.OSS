// Copyright (c) 2026 MMPWorks LLC
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.
#nullable enable

using System.Collections.Generic;
using FluentAssertions;
using MMP.Herald.Generators;
using MMP.Herald.OSS.Tests.Templating;
using Xunit;

namespace MMP.Herald.OSS.Tests.Generators;

/// <summary>
/// Drives every row in <see cref="CompileTimeNameResolverFixtures"/> through
/// the build-time <see cref="CompileTimeNameResolver.Resolve"/>.
///
/// <para>
/// Runtime-only fixture rows (empty-CAE fallback) are filtered out — the
/// build-time path always has a non-empty CAE for every slot
/// (<c>[HeraldLog]</c> parameter names or interceptor call-site expression
/// text). The runtime-side counterpart test in
/// <c>PolicyResolveAllFixtureTests</c> exercises those rows instead.
/// </para>
/// </summary>
public sealed class CompileTimeNameResolverTests
{
    public static IEnumerable<object[]> BuildTimeRows()
    {
        foreach (var row in CompileTimeNameResolverFixtures.All)
        {
            if (row.RuntimeOnly) continue;
            yield return new object[] { row };
        }
    }

    [Theory]
    [MemberData(nameof(BuildTimeRows))]
    public void Resolve_matches_fixture(CompileTimeNameResolverFixtures.Row row)
    {
        var policy = MapPolicyId(row.PolicyId);

        var actual = CompileTimeNameResolver.Resolve(row.Template, row.CaeNames, policy);

        actual.Should().Equal(
            row.Expected,
            "fixture '{0}' under policy {1}", row.Description, row.PolicyId);
    }

    private static BuiltinPolicyKind MapPolicyId(string id) => id switch
    {
        "pascal" => BuiltinPolicyKind.Pascal,
        "snake"  => BuiltinPolicyKind.Snake,
        "camel"  => BuiltinPolicyKind.Camel,
        _ => BuiltinPolicyKind.Custom,
    };
}

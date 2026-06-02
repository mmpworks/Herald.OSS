#nullable enable

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MMP.Herald.Templating;
using MMP.Herald.Templating.NamingPolicies;
using Xunit;

namespace MMP.Herald.OSS.Tests.Templating;

/// <summary>
/// Behavior of the process-static <see cref="NameResolverCache"/>.
///
/// <para>
/// Tests reset the cache at the start of each test so the cap-hit warning
/// rearms cleanly. The cache is process-static, so cross-test isolation
/// relies on this reset.
/// </para>
/// </summary>
[Collection(MMP.Herald.OSS.Tests.Helpers.DefaultHostCollection.Name)]
public sealed class NameResolverCacheTests
{
    public NameResolverCacheTests()
    {
        NameResolverCache.Reset();
    }

    [Fact]
    public void Cache_hit_returns_same_array_reference()
    {
        var policy = PascalCasePolicy.Instance;
        var template = "user {UserId} signed in";
        var (tokens, argExprs) = SinglePair("UserId", "userId");

        var first = NameResolverCache.Resolve(policy, template, tokens, argExprs);
        var second = NameResolverCache.Resolve(policy, template, tokens, argExprs);

        // The whole point of the cache is to hand back the same string[] by
        // reference on subsequent calls — no allocation on hit.
        ReferenceEquals(first, second).Should().BeTrue(
            "cache hit must return the same array reference (zero allocation on hot path)");
    }

    [Fact]
    public void Different_policies_get_independent_cache_entries()
    {
        var template = "user {UserId} signed in";
        var (tokens, argExprs) = SinglePair("UserId", "userId");

        var pascalResult = NameResolverCache.Resolve(
            PascalCasePolicy.Instance, template, tokens, argExprs);
        var camelResult = NameResolverCache.Resolve(
            CamelCasePolicy.Instance, template, tokens, argExprs);

        pascalResult[0].Should().Be("UserId");
        camelResult[0].Should().Be("userId");
        ReferenceEquals(pascalResult, camelResult).Should().BeFalse();
    }

    [Fact]
    public void Different_templates_get_independent_cache_entries()
    {
        var policy = PascalCasePolicy.Instance;
        var (tokens, argExprs) = SinglePair("UserId", "userId");

        var first = NameResolverCache.Resolve(policy, "template a", tokens, argExprs);
        var second = NameResolverCache.Resolve(policy, "template b", tokens, argExprs);

        ReferenceEquals(first, second).Should().BeFalse();
    }

    [Fact]
    public void Concurrent_first_resolve_on_same_template_returns_consistent_array()
    {
        // Two threads racing on the cold miss for the same key — GetOrAdd
        // may compute the array twice but only one wins. Both callers must
        // see the same final reference.
        var policy = PascalCasePolicy.Instance;
        var template = "concurrent {UserId} test";
        var (tokens, argExprs) = SinglePair("UserId", "userId");

        var barrier = new Barrier(participantCount: 8);
        var results = new string[8][];

        Parallel.For(0, 8, i =>
        {
            barrier.SignalAndWait();
            results[i] = NameResolverCache.Resolve(policy, template, tokens, argExprs);
        });

        // Every result must equal the first; the cache converges on one array.
        var canonical = results[0];
        foreach (var r in results)
        {
            ReferenceEquals(r, canonical).Should().BeTrue(
                "all threads must end up reading the same cached array reference");
        }
    }

    [Fact]
    public void Throws_when_policy_returns_null()
    {
        var policy = new BrokenPolicy(returnNull: true);
        var (tokens, argExprs) = SinglePair("UserId", "userId");

        var act = () => NameResolverCache.Resolve(policy, "template", tokens, argExprs);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*broken-null*returned null*");
    }

    [Fact]
    public void Throws_when_policy_returns_wrong_length_array()
    {
        var policy = new BrokenPolicy(returnTooShort: true);
        var (tokens, argExprs) = SinglePair("UserId", "userId");

        var act = () => NameResolverCache.Resolve(policy, "template", tokens, argExprs);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*broken-too-short*length*");
    }

    [Fact]
    public void Throws_when_policy_returns_null_entry()
    {
        var policy = new BrokenPolicy(returnNullEntry: true);
        var (tokens, argExprs) = SinglePair("UserId", "userId");

        var act = () => NameResolverCache.Resolve(policy, "template", tokens, argExprs);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*broken-null-entry*null or empty*");
    }

    [Fact]
    public void Reset_clears_entries_and_rearms_cap_hit_event()
    {
        NameResolverCache.Resolve(
            PascalCasePolicy.Instance,
            "template a",
            new MessageTemplateToken.Property[0],
            new[] { "v" });
        NameResolverCache.Count.Should().BeGreaterThan(0);

        NameResolverCache.Reset();

        NameResolverCache.Count.Should().Be(0);
    }

    private static (MessageTemplateToken.Property[], string[]) SinglePair(string token, string argExpr)
    {
        var tokens = new[]
        {
            new MessageTemplateToken.Property(token, LogPropertyCaptureMode.Default, null, "{" + token + "}"),
        };
        return (tokens, new[] { argExpr });
    }

    /// <summary>
    /// Test policy that deliberately violates the <c>ResolveAll</c> contract
    /// to verify the cache's validation surface throws loudly.
    /// </summary>
    private sealed class BrokenPolicy : IPropertyNamingPolicy
    {
        private readonly bool _returnNull;
        private readonly bool _returnTooShort;
        private readonly bool _returnNullEntry;

        public BrokenPolicy(bool returnNull = false, bool returnTooShort = false, bool returnNullEntry = false)
        {
            _returnNull = returnNull;
            _returnTooShort = returnTooShort;
            _returnNullEntry = returnNullEntry;
            Id = returnNull ? "broken-null"
               : returnTooShort ? "broken-too-short"
               : "broken-null-entry";
        }

        public string Id { get; }

        public string[] ResolveAll(
            ReadOnlySpan<MessageTemplateToken.Property> tokens,
            ReadOnlySpan<string> argExprs)
        {
            if (_returnNull) return null!;
            if (_returnTooShort) return Array.Empty<string>();
            if (_returnNullEntry) return new string[] { null! };
            return new string[argExprs.Length];
        }
    }
}

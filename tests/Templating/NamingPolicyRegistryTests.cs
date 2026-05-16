#nullable enable

using System;
using FluentAssertions;
using MMP.Herald.Templating;
using MMP.Herald.Templating.NamingPolicies;
using Xunit;

namespace MMP.Herald.OSS.Tests.Templating;

/// <summary>
/// Behavior of the static <see cref="NamingPolicyRegistry"/>: built-ins are
/// pre-registered, idempotent re-registration is allowed for the same type,
/// duplicate ids with different types throw, unknown ids throw with a useful
/// message.
/// </summary>
public sealed class NamingPolicyRegistryTests
{
    [Theory]
    [InlineData("pascal", typeof(PascalCasePolicy))]
    [InlineData("camel",  typeof(CamelCasePolicy))]
    [InlineData("snake",  typeof(SnakeCasePolicy))]
    public void Builtins_are_registered_eagerly(string id, Type expectedType)
    {
        var resolved = NamingPolicyRegistry.Resolve(id);

        resolved.Should().BeOfType(expectedType);
        resolved.Id.Should().Be(id);
    }

    [Fact]
    public void TryResolve_returns_false_for_unknown_id()
    {
        var found = NamingPolicyRegistry.TryResolve("never-registered-policy-id", out var policy);

        found.Should().BeFalse();
        policy.Should().BeNull();
    }

    [Fact]
    public void Resolve_throws_UnknownNamingPolicyException_for_unknown_id()
    {
        var act = () => NamingPolicyRegistry.Resolve("missing-policy");

        act.Should().Throw<UnknownNamingPolicyException>()
            .Where(ex => ex.PolicyId == "missing-policy")
            .WithMessage("*missing-policy*Register*");
    }

    [Fact]
    public void Register_is_idempotent_for_same_type()
    {
        // Built-ins already registered. Re-registering the same singleton is
        // explicitly allowed — supports module-initializer races and repeated
        // bootstrap calls.
        var act = () => NamingPolicyRegistry.Register(PascalCasePolicy.Instance);

        act.Should().NotThrow();
    }

    [Fact]
    public void Register_throws_DuplicatePolicyIdException_for_id_collision_with_different_type()
    {
        // A custom policy that lies about its Id ("pascal") and is therefore
        // a different Type from PascalCasePolicy. The registry must reject it
        // loudly — silent acceptance would corrupt JSON round-trip.
        var imposter = new FakePolicy("pascal");

        var act = () => NamingPolicyRegistry.Register(imposter);

        act.Should().Throw<DuplicatePolicyIdException>()
            .Where(ex => ex.PolicyId == "pascal"
                      && ex.ExistingType == typeof(PascalCasePolicy)
                      && ex.AttemptedType == typeof(FakePolicy));
    }

    [Fact]
    public void Register_null_throws_ArgumentNullException()
    {
        var act = () => NamingPolicyRegistry.Register(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void RegisteredIds_includes_all_three_builtins()
    {
        var ids = NamingPolicyRegistry.RegisteredIds;

        ids.Should().Contain(new[] { "pascal", "camel", "snake" });
    }

    /// <summary>
    /// Test-only policy used to verify id-collision rejection. Reports an
    /// arbitrary id; the resolution behavior is a no-op.
    /// </summary>
    private sealed class FakePolicy : IPropertyNamingPolicy
    {
        public FakePolicy(string id) { Id = id; }
        public string Id { get; }
        public string[] ResolveAll(
            ReadOnlySpan<MessageTemplateToken.Property> tokens,
            ReadOnlySpan<string> argExprs)
            => new string[argExprs.Length];
    }
}

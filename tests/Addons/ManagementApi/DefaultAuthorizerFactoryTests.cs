#nullable enable

using FluentAssertions;
using MMP.Herald.Addons.ManagementApi;
using MMP.Herald.Quick;
using Xunit;

namespace MMP.Herald.OSS.Tests.Addons.ManagementApi;

/// <summary>
/// Pins the ADR-001 Seam 1 contract:
/// <see cref="HeraldManagementApi.DefaultAuthorizerFactory"/> is a
/// process-wide replaceable default consulted by
/// <see cref="HeraldManagementApi.FromRegistration"/> when no
/// explicit authorizer is passed.
///
/// <para>
/// The seam exists so a host building many APIs through
/// <c>FromRegistration</c> wires the authorizer once at boot
/// instead of threading it through every call site. The tests
/// here cover all three branches of the precedence chain
/// (explicit > factory > reject-all), normalisation of a null
/// assignment, and the global-state isolation an xUnit run
/// requires.
/// </para>
/// </summary>
[Collection(nameof(DefaultAuthorizerFactoryTests))]
[CollectionDefinition(nameof(DefaultAuthorizerFactoryTests), DisableParallelization = true)]
public sealed class DefaultAuthorizerFactoryTests : System.IDisposable
{
    // The factory is process-wide static state. Snapshot the current
    // value on construction, restore it on disposal so tests stay
    // isolated even when a future test reassigns it.
    private readonly System.Func<HeraldRegistration, IManagementApiAuthorizer> _original;

    public DefaultAuthorizerFactoryTests()
    {
        _original = HeraldManagementApi.DefaultAuthorizerFactory;
    }

    public void Dispose()
    {
        HeraldManagementApi.DefaultAuthorizerFactory = _original;
    }

    [Fact]
    public void Default_factory_returns_RejectAllAuthorizer_instance()
    {
        // The starting value of the seam matters: an unconfigured
        // host that calls FromRegistration without wiring the
        // factory must land on the reject-all default.
        var entry = BuildRegistration();

        var authorizer = HeraldManagementApi.DefaultAuthorizerFactory(entry);

        authorizer.Should().BeSameAs(RejectAllAuthorizer.Instance,
            "the initial factory must return the safe default");
    }

    [Fact]
    public void FromRegistration_with_no_arguments_consults_the_factory()
    {
        // The factory is the seam that replaces "thread an
        // authorizer through every FromRegistration call". Pin the
        // observable behaviour: reassigning the factory changes
        // what FromRegistration produces with no per-site edits.
        var entry = BuildRegistration();
        HeraldManagementApi.DefaultAuthorizerFactory = static _ => AllowAllAuthorizer.Instance;

        var api    = HeraldManagementApi.FromRegistration(entry);
        var result = api.SetMinimumLevel("info");

        result.Success.Should().BeTrue(
            "the factory-supplied AllowAllAuthorizer must be in effect when no explicit argument was passed");
    }

    [Fact]
    public void Explicit_authorizer_argument_overrides_the_factory()
    {
        // Precedence rule: explicit > factory > reject-all. Pin
        // the highest-precedence branch by setting the factory to
        // allow-all and passing reject-all explicitly — the
        // explicit argument must win and the API must reject.
        var entry = BuildRegistration();
        HeraldManagementApi.DefaultAuthorizerFactory = static _ => AllowAllAuthorizer.Instance;

        var api    = HeraldManagementApi.FromRegistration(entry, RejectAllAuthorizer.Instance);
        var result = api.SetMinimumLevel("info");

        result.Success.Should().BeFalse(
            "the explicit authorizer argument must win over the factory");
    }

    [Fact]
    public void Setting_factory_to_null_normalises_to_RejectAll()
    {
        // Defence-in-depth: a misuse (someone clearing the factory
        // back to "nothing") must not open the API. The setter
        // collapses null to the safe default — same shape as the
        // Authorizer instance setter.
        HeraldManagementApi.DefaultAuthorizerFactory = static _ => AllowAllAuthorizer.Instance;
        HeraldManagementApi.DefaultAuthorizerFactory = null!;

        var entry      = BuildRegistration();
        var authorizer = HeraldManagementApi.DefaultAuthorizerFactory(entry);

        authorizer.Should().BeSameAs(RejectAllAuthorizer.Instance,
            "a null assignment must normalise back to the safe default, not leave the previous factory in place");
    }

    [Fact]
    public void Factory_receives_the_registration_it_is_resolving_for()
    {
        // The factory takes a HeraldRegistration so a host can
        // resolve the authorizer per-tenant / per-pipeline if it
        // wants to. Pin the parameter pass-through so a future
        // refactor doesn't drop the argument and silently force
        // every registration onto the same authorizer.
        var entry = BuildRegistration("payments");
        HeraldRegistration? seen = null;
        HeraldManagementApi.DefaultAuthorizerFactory = reg =>
        {
            seen = reg;
            return AllowAllAuthorizer.Instance;
        };

        _ = HeraldManagementApi.FromRegistration(entry);

        seen.Should().BeSameAs(entry,
            "the factory must receive the same registration FromRegistration is building for");
    }

    private static HeraldRegistration BuildRegistration(string name = "test")
    {
        var builder = QuickLogBuilder.Create().WithConsoleSink();
        var result  = builder.BuildAndCommit();
        return new HeraldRegistration(name, builder, result);
    }
}

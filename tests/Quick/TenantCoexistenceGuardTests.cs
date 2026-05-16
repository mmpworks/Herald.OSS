#nullable enable

using System;
using FluentAssertions;
using MMP.Herald.OSS.Tests.Helpers;
using MMP.Herald.Quick;
using Xunit;

namespace MMP.Herald.OSS.Tests.Quick;

/// <summary>
/// Pins the <see cref="HeraldRegistryInstance.AllowDefaultAndScopedCoexistence"/>
/// strict-mode guard. With the flag off (default true), the registry
/// accepts any mix; with the flag flipped to false, default and
/// non-default tenants are mutually exclusive.
/// </summary>
[Collection(DefaultHostCollection.Name)]
public sealed class TenantCoexistenceGuardTests : IDisposable
{
    private const string TenantA = "coexist-tenant-a";
    private const string TenantB = "coexist-tenant-b";
    private const string PipelineName = "coexist-pipeline";
    private const string PipelineName2 = "coexist-pipeline-2";

    private readonly bool _priorFlag;

    public TenantCoexistenceGuardTests()
    {
        _priorFlag = HeraldRegistry.AllowDefaultAndScopedCoexistence;
    }

    public void Dispose()
    {
        // Always restore the flag BEFORE attempting removals so cleanup
        // doesn't trip the guard.
        HeraldRegistry.AllowDefaultAndScopedCoexistence = _priorFlag;

        HeraldRegistry.Remove(HeraldTenant.Default, PipelineName);
        HeraldRegistry.Remove(HeraldTenant.Default, PipelineName2);
        HeraldRegistry.Remove(TenantA, PipelineName);
        HeraldRegistry.Remove(TenantA, PipelineName2);
        HeraldRegistry.Remove(TenantB, PipelineName);
    }

    [Fact]
    public void Default_flag_is_true_preserving_existing_behavior()
    {
        HeraldRegistry.AllowDefaultAndScopedCoexistence.Should().BeTrue();
    }

    [Fact]
    public void With_flag_on_default_and_non_default_can_coexist()
    {
        // Sanity: with the flag at its default, registrations can be mixed.
        var b1 = QuickLogBuilder.Create(PipelineName).WithConsoleSink();
        var r1 = b1.BuildAndCommit();
        HeraldRegistry.Register(b1, r1);

        var b2 = QuickLogBuilder.Create(PipelineName2).WithConsoleSink();
        var r2 = b2.BuildAndCommit();
        HeraldRegistry.Register(TenantA, PipelineName2, b2, r2);

        HeraldRegistry.Get(HeraldTenant.Default, PipelineName).Should().NotBeNull();
        HeraldRegistry.Get(TenantA, PipelineName2).Should().NotBeNull();
    }

    [Fact]
    public void With_flag_off_register_into_default_throws_when_non_default_exists()
    {
        // Establish a non-default registration first.
        var b1 = QuickLogBuilder.Create(PipelineName).WithConsoleSink();
        var r1 = b1.BuildAndCommit();
        HeraldRegistry.Register(TenantA, PipelineName, b1, r1);

        // Flip the guard. Now registering into default must throw.
        HeraldRegistry.AllowDefaultAndScopedCoexistence = false;

        var b2 = QuickLogBuilder.Create(PipelineName2).WithConsoleSink();
        var r2 = b2.BuildAndCommit();
        var act = () => HeraldRegistry.Register(b2, r2);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*default tenant*non-default tenants*");
        HeraldRegistry.Get(HeraldTenant.Default, PipelineName2).Should().BeNull();
    }

    [Fact]
    public void With_flag_off_register_into_non_default_throws_when_default_exists()
    {
        var b1 = QuickLogBuilder.Create(PipelineName).WithConsoleSink();
        var r1 = b1.BuildAndCommit();
        HeraldRegistry.Register(b1, r1);

        HeraldRegistry.AllowDefaultAndScopedCoexistence = false;

        var b2 = QuickLogBuilder.Create(PipelineName2).WithConsoleSink();
        var r2 = b2.BuildAndCommit();
        var act = () => HeraldRegistry.Register(TenantA, PipelineName2, b2, r2);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*default tenant has registrations*");
        HeraldRegistry.Get(TenantA, PipelineName2).Should().BeNull();
    }

    [Fact]
    public void With_flag_off_two_non_default_tenants_can_coexist()
    {
        HeraldRegistry.AllowDefaultAndScopedCoexistence = false;

        var b1 = QuickLogBuilder.Create(PipelineName).WithConsoleSink();
        var r1 = b1.BuildAndCommit();
        HeraldRegistry.Register(TenantA, PipelineName, b1, r1);

        var b2 = QuickLogBuilder.Create(PipelineName).WithConsoleSink();
        var r2 = b2.BuildAndCommit();
        var act = () => HeraldRegistry.Register(TenantB, PipelineName, b2, r2);

        act.Should().NotThrow(
            "two non-default tenants are the EXPECTED multi-tenant shape — the guard only excludes default ↔ non-default mixing");
    }

    [Fact]
    public void With_flag_off_two_default_registrations_can_coexist()
    {
        HeraldRegistry.AllowDefaultAndScopedCoexistence = false;

        var b1 = QuickLogBuilder.Create(PipelineName).WithConsoleSink();
        var r1 = b1.BuildAndCommit();
        HeraldRegistry.Register(b1, r1);

        var b2 = QuickLogBuilder.Create(PipelineName2).WithConsoleSink();
        var r2 = b2.BuildAndCommit();
        var act = () => HeraldRegistry.Register(b2, r2);

        act.Should().NotThrow(
            "two default-tenant registrations are the single-tenant shape — the guard only excludes mixing");
    }

    [Fact]
    public void With_flag_off_clearing_default_unblocks_non_default_register()
    {
        var b1 = QuickLogBuilder.Create(PipelineName).WithConsoleSink();
        var r1 = b1.BuildAndCommit();
        HeraldRegistry.Register(b1, r1);

        HeraldRegistry.AllowDefaultAndScopedCoexistence = false;

        // Remove the default registration; the guard now permits non-default.
        HeraldRegistry.Remove(HeraldTenant.Default, PipelineName);

        var b2 = QuickLogBuilder.Create(PipelineName2).WithConsoleSink();
        var r2 = b2.BuildAndCommit();
        var act = () => HeraldRegistry.Register(TenantA, PipelineName2, b2, r2);

        act.Should().NotThrow();
        HeraldRegistry.Get(TenantA, PipelineName2).Should().NotBeNull();
    }

    [Fact]
    public void With_flag_off_try_register_propagates_guard_throw()
    {
        var b1 = QuickLogBuilder.Create(PipelineName).WithConsoleSink();
        var r1 = b1.BuildAndCommit();
        HeraldRegistry.Register(TenantA, PipelineName, b1, r1);

        HeraldRegistry.AllowDefaultAndScopedCoexistence = false;

        var b2 = QuickLogBuilder.Create(PipelineName2).WithConsoleSink();
        var r2 = b2.BuildAndCommit();
        var act = () => HeraldRegistry.TryRegister(HeraldTenant.Default, PipelineName2, b2, r2);

        act.Should().Throw<InvalidOperationException>(
            "the guard is an authorization failure, not a name-collision — it propagates out of TryRegister");
        HeraldRegistry.Get(HeraldTenant.Default, PipelineName2).Should().BeNull();
    }
}

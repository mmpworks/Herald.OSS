#nullable enable

using System;
using FluentAssertions;
using MMP.Herald.OSS.Tests.Helpers;
using MMP.Herald.Quick;
using Xunit;

namespace MMP.Herald.OSS.Tests.Quick;

/// <summary>
/// Leak-prevention contract for tenant-scoped lookups against
/// <see cref="HeraldRegistry"/>. The single-arg lookup overloads
/// (<c>Get(name)</c>, <c>Require(name)</c>, etc.) MUST resolve through
/// <see cref="HeraldTenantScope.Current"/> so a scoped caller cannot
/// accidentally read a different tenant's bucket. A miss inside a scope
/// returns null — never silently falls through to the default tenant.
/// </summary>
[Collection(DefaultHostCollection.Name)]
public sealed class HeraldTenantScopeRegistryContractTests : IDisposable
{
    private const string TenantA = "contract-tenant-a";
    private const string TenantB = "contract-tenant-b";
    private const string PipelineName = "contract-pipeline";

    public void Dispose()
    {
        // AsyncLocal scope is process-global; reset it so a leak from this
        // test cannot affect any later test in the same process.
        HeraldTenantScope.Current = HeraldTenant.Default;

        HeraldRegistry.Remove(TenantA, PipelineName);
        HeraldRegistry.Remove(TenantB, PipelineName);
        HeraldRegistry.Remove(HeraldTenant.Default, PipelineName);
    }

    [Fact]
    public void Scoped_get_returns_scoped_tenant_registration()
    {
        var builderA = QuickLogBuilder.Create(PipelineName).WithConsoleSink();
        var resultA = builderA.BuildAndCommit();
        HeraldRegistry.Register(TenantA, PipelineName, builderA, resultA);

        using (HeraldTenantScope.Push(TenantA))
        {
            var resolved = HeraldRegistry.Get(PipelineName);
            resolved.Should().NotBeNull("a scoped single-arg Get must resolve through HeraldTenantScope.Current");
            resolved!.Result.Should().BeSameAs(resultA);
        }
    }

    [Fact]
    public void Scoped_get_returns_null_when_scope_has_no_registration_even_if_default_does()
    {
        // Default tenant DOES have a pipeline at this name — but the scoped
        // caller belongs to TenantA, which does NOT. A leaking implementation
        // would substitute the default's pipeline; the contract says null.
        var builderDefault = QuickLogBuilder.Create(PipelineName).WithConsoleSink();
        var resultDefault = builderDefault.BuildAndCommit();
        HeraldRegistry.Register(HeraldTenant.Default, PipelineName, builderDefault, resultDefault);

        using (HeraldTenantScope.Push(TenantA))
        {
            var resolved = HeraldRegistry.Get(PipelineName);
            resolved.Should().BeNull("a scoped miss must NOT fall back to the default tenant — that would be a leak");
        }
    }

    [Fact]
    public void Unscoped_get_returns_default_registration()
    {
        // Single-tenant deployments never set the scope; reads must resolve
        // to HeraldTenant.Default so legacy code keeps working unchanged.
        var builder = QuickLogBuilder.Create(PipelineName).WithConsoleSink();
        var result = builder.BuildAndCommit();
        HeraldRegistry.Register(builder, result);

        var resolved = HeraldRegistry.Get(PipelineName);

        resolved.Should().NotBeNull();
        resolved!.Result.Should().BeSameAs(result);
    }

    [Fact]
    public void Scoped_get_distinguishes_between_two_tenants_with_same_pipeline_name()
    {
        var builderA = QuickLogBuilder.Create(PipelineName).WithConsoleSink();
        var resultA = builderA.BuildAndCommit();
        HeraldRegistry.Register(TenantA, PipelineName, builderA, resultA);

        var builderB = QuickLogBuilder.Create(PipelineName).WithConsoleSink();
        var resultB = builderB.BuildAndCommit();
        HeraldRegistry.Register(TenantB, PipelineName, builderB, resultB);

        using (HeraldTenantScope.Push(TenantA))
        {
            HeraldRegistry.Get(PipelineName)!.Result.Should().BeSameAs(resultA);
        }

        using (HeraldTenantScope.Push(TenantB))
        {
            HeraldRegistry.Get(PipelineName)!.Result.Should().BeSameAs(resultB);
        }
    }

    [Fact]
    public void Scoped_require_throws_when_scope_has_no_registration_even_if_default_does()
    {
        var builderDefault = QuickLogBuilder.Create(PipelineName).WithConsoleSink();
        var resultDefault = builderDefault.BuildAndCommit();
        HeraldRegistry.Register(HeraldTenant.Default, PipelineName, builderDefault, resultDefault);

        using (HeraldTenantScope.Push(TenantA))
        {
            var act = () => HeraldRegistry.Require(PipelineName);
            act.Should().Throw<InvalidOperationException>(
                "a scoped Require must surface the miss rather than silently substituting the default tenant");
        }
    }

    [Fact]
    public void Scoped_contains_returns_false_when_scope_has_no_registration_even_if_default_does()
    {
        var builderDefault = QuickLogBuilder.Create(PipelineName).WithConsoleSink();
        var resultDefault = builderDefault.BuildAndCommit();
        HeraldRegistry.Register(HeraldTenant.Default, PipelineName, builderDefault, resultDefault);

        using (HeraldTenantScope.Push(TenantA))
        {
            HeraldRegistry.Contains(PipelineName).Should().BeFalse(
                "a scoped Contains must answer for the scoped tenant, not the default");
        }
    }

    [Fact]
    public void Scoped_register_lands_in_scoped_tenant_bucket_not_default()
    {
        var builder = QuickLogBuilder.Create(PipelineName).WithConsoleSink();
        var result = builder.BuildAndCommit();

        using (HeraldTenantScope.Push(TenantA))
        {
            HeraldRegistry.Register(PipelineName, builder, result);
        }

        // The scoped Register must put the entry in TenantA's bucket, not
        // default's. A scope-blind Register would silently land the
        // registration in default and break leak symmetry with the read path.
        HeraldRegistry.Get(TenantA, PipelineName).Should().NotBeNull(
            "a scoped Register must land in the scoped tenant's bucket");
        HeraldRegistry.Get(HeraldTenant.Default, PipelineName).Should().BeNull(
            "a scoped Register must NOT silently land in the default tenant — that breaks read/write symmetry");
    }

    [Fact]
    public void Scoped_try_register_lands_in_scoped_tenant_bucket_not_default()
    {
        var builder = QuickLogBuilder.Create(PipelineName).WithConsoleSink();
        var result = builder.BuildAndCommit();

        bool succeeded;
        using (HeraldTenantScope.Push(TenantA))
        {
            succeeded = HeraldRegistry.TryRegister(PipelineName, builder, result);
        }

        succeeded.Should().BeTrue();
        HeraldRegistry.Get(TenantA, PipelineName).Should().NotBeNull();
        HeraldRegistry.Get(HeraldTenant.Default, PipelineName).Should().BeNull();
    }
}

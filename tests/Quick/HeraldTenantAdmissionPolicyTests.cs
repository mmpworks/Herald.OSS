#nullable enable

using System;
using System.Collections.Generic;
using FluentAssertions;
using MMP.Herald.OSS.Tests.Helpers;
using MMP.Herald.Quick;
using Xunit;

namespace MMP.Herald.OSS.Tests.Quick;

/// <summary>
/// Pins the architectural seam Herald.OSS ships for downstream commercial
/// wrappers: <see cref="HeraldTenant.TenantAdmissionPolicy"/>. OSS itself
/// ships a no-op policy that admits every valid tenant; an Enterprise build
/// replaces the delegate at startup with a license-aware policy that throws
/// on non-default tenants when the license is not granted. The seam exists
/// so the gate plugs in without touching OSS source.
/// </summary>
[Collection(DefaultHostCollection.Name)]
public sealed class HeraldTenantAdmissionPolicyTests : IDisposable
{
    private const string TenantA = "policy-tenant-a";
    private const string PipelineName = "policy-pipeline";

    // Capture whatever delegate is installed at the start of the test so the
    // Dispose can restore it. Important when other tests in the suite have
    // already swapped the policy out — the restore must not assume "no-op".
    private readonly Action<string> _originalPolicy = HeraldTenant.TenantAdmissionPolicy;

    public void Dispose()
    {
        HeraldTenant.TenantAdmissionPolicy = _originalPolicy;
        HeraldRegistry.Remove(TenantA, PipelineName);
        HeraldRegistry.Remove(HeraldTenant.Default, PipelineName);
    }

    [Fact]
    public void Default_policy_admits_any_valid_tenant()
    {
        // OSS default: no-op. Non-default tenant registration must succeed.
        var builder = QuickLogBuilder.Create(PipelineName).WithConsoleSink();
        var result = builder.BuildAndCommit();

        var act = () => HeraldRegistry.Register(TenantA, PipelineName, builder, result);

        act.Should().NotThrow(
            "the OSS default admission policy is a no-op; non-default tenants must register without error");
        HeraldRegistry.Get(TenantA, PipelineName).Should().NotBeNull();
    }

    [Fact]
    public void Replaced_policy_receives_normalised_tenant_id()
    {
        var observed = new List<string>();
        HeraldTenant.TenantAdmissionPolicy = tenant => observed.Add(tenant);

        var builder = QuickLogBuilder.Create(PipelineName).WithConsoleSink();
        var result = builder.BuildAndCommit();

        // Pass a mixed-case tenant id; Normalize lowercases it, and the
        // policy must see the canonical form so license checks compare
        // against a single string shape.
        HeraldRegistry.Register("Policy-Tenant-A", PipelineName, builder, result);

        observed.Should().ContainSingle().Which.Should().Be(TenantA,
            "the admission policy must receive the normalised tenant id, not the raw caller input");
    }

    [Fact]
    public void Throwing_policy_blocks_register_publish()
    {
        HeraldTenant.TenantAdmissionPolicy = tenant =>
        {
            if (!HeraldTenant.IsDefault(tenant))
                throw new InvalidOperationException($"Tenant '{tenant}' rejected by test policy");
        };

        var builder = QuickLogBuilder.Create(PipelineName).WithConsoleSink();
        var result = builder.BuildAndCommit();

        var act = () => HeraldRegistry.Register(TenantA, PipelineName, builder, result);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*rejected by test policy*");

        // The registration MUST NOT have been published. A rejecting policy
        // that left a half-published entry would defeat the gate.
        HeraldRegistry.Get(TenantA, PipelineName).Should().BeNull(
            "a throwing policy must abort the registration before the dictionary publish");
    }

    [Fact]
    public void Throwing_policy_blocks_try_register_publish_and_propagates()
    {
        HeraldTenant.TenantAdmissionPolicy = tenant =>
        {
            if (!HeraldTenant.IsDefault(tenant))
                throw new InvalidOperationException("rejected");
        };

        var builder = QuickLogBuilder.Create(PipelineName).WithConsoleSink();
        var result = builder.BuildAndCommit();

        // TryRegister's bool return is for name-collision detection, not
        // for authorisation rejection. A throwing policy must propagate.
        var act = () => HeraldRegistry.TryRegister(TenantA, PipelineName, builder, result);

        act.Should().Throw<InvalidOperationException>();
        HeraldRegistry.Get(TenantA, PipelineName).Should().BeNull();
    }

    [Fact]
    public void Default_tenant_path_invokes_policy_too()
    {
        // The policy hook fires for EVERY registration including default.
        // OSS's no-op default does not differentiate; a custom policy that
        // wants the IsDefault short-circuit must include it itself.
        var observed = new List<string>();
        HeraldTenant.TenantAdmissionPolicy = tenant => observed.Add(tenant);

        var builder = QuickLogBuilder.Create(PipelineName).WithConsoleSink();
        var result = builder.BuildAndCommit();
        HeraldRegistry.Register(builder, result);

        observed.Should().ContainSingle().Which.Should().Be(HeraldTenant.Default);
    }
}

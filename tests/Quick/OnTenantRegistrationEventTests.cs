#nullable enable

using System;
using System.Collections.Generic;
using FluentAssertions;
using MMP.Herald.OSS.Tests.Helpers;
using MMP.Herald.Quick;
using Xunit;

namespace MMP.Herald.OSS.Tests.Quick;

/// <summary>
/// Pins the <see cref="HeraldRegistryInstance.OnTenantRegistration"/>
/// architectural seam. A downstream host subscribes to receive
/// <c>(tenant, name)</c> for every successful registration so it can
/// drive an audit log, a license counter, or other observability
/// without modifying OSS source.
/// </summary>
[Collection(DefaultHostCollection.Name)]
public sealed class OnTenantRegistrationEventTests : IDisposable
{
    private const string TenantA = "obs-tenant-a";
    private const string PipelineName = "obs-pipeline";
    private const string PipelineName2 = "obs-pipeline-2";

    private readonly List<(string Tenant, string Name)> _observed = new();
    private readonly Action<string, string> _handler;

    public OnTenantRegistrationEventTests()
    {
        _handler = (tenant, name) => _observed.Add((tenant, name));
        HeraldRegistry.OnTenantRegistration += _handler;
    }

    public void Dispose()
    {
        HeraldRegistry.OnTenantRegistration -= _handler;
        HeraldRegistry.Remove(TenantA, PipelineName);
        HeraldRegistry.Remove(TenantA, PipelineName2);
        HeraldRegistry.Remove(HeraldTenant.Default, PipelineName);
    }

    [Fact]
    public void Register_fires_event_with_default_tenant_and_name()
    {
        var builder = QuickLogBuilder.Create(PipelineName).WithConsoleSink();
        var result = builder.BuildAndCommit();

        HeraldRegistry.Register(builder, result);

        _observed.Should().ContainSingle()
            .Which.Should().Be((HeraldTenant.Default, PipelineName));
    }

    [Fact]
    public void Register_with_explicit_tenant_passes_normalised_id()
    {
        var builder = QuickLogBuilder.Create(PipelineName).WithConsoleSink();
        var result = builder.BuildAndCommit();

        // Caller passes a mixed-case id; the event handler must see the
        // canonical lowercase form so any downstream comparison works.
        HeraldRegistry.Register("Obs-Tenant-A", PipelineName, builder, result);

        _observed.Should().ContainSingle()
            .Which.Tenant.Should().Be(TenantA);
    }

    [Fact]
    public void Try_register_fires_only_when_publish_succeeds()
    {
        var builder = QuickLogBuilder.Create(PipelineName).WithConsoleSink();
        var result = builder.BuildAndCommit();

        var firstSucceeded = HeraldRegistry.TryRegister(TenantA, PipelineName, builder, result);
        var secondSucceeded = HeraldRegistry.TryRegister(TenantA, PipelineName, builder, result);

        firstSucceeded.Should().BeTrue();
        secondSucceeded.Should().BeFalse(
            "TryRegister returns false on name collision");
        _observed.Should().ContainSingle("the colliding TryRegister must NOT fire the event");
    }

    [Fact]
    public void Register_upsert_fires_event_once_per_call()
    {
        var builder = QuickLogBuilder.Create(PipelineName).WithConsoleSink();
        var result = builder.BuildAndCommit();

        // First registration publishes; second registration upserts and
        // also publishes. Each Register call must fire the event once.
        HeraldRegistry.Register(TenantA, PipelineName, builder, result);
        HeraldRegistry.Register(TenantA, PipelineName, builder, result);

        _observed.Should().HaveCount(2);
        _observed.Should().AllSatisfy(entry => entry.Should().Be((TenantA, PipelineName)));
    }

    [Fact]
    public void Multiple_subscribers_all_receive_notification()
    {
        var second = new List<(string Tenant, string Name)>();
        Action<string, string> secondHandler = (t, n) => second.Add((t, n));
        HeraldRegistry.OnTenantRegistration += secondHandler;
        try
        {
            var builder = QuickLogBuilder.Create(PipelineName).WithConsoleSink();
            var result = builder.BuildAndCommit();
            HeraldRegistry.Register(TenantA, PipelineName, builder, result);

            _observed.Should().ContainSingle();
            second.Should().ContainSingle();
        }
        finally
        {
            HeraldRegistry.OnTenantRegistration -= secondHandler;
        }
    }

    [Fact]
    public void Unsubscribing_stops_notifications()
    {
        // Drop the default handler that the ctor wired in so subsequent
        // registers stay silent.
        HeraldRegistry.OnTenantRegistration -= _handler;

        var builder = QuickLogBuilder.Create(PipelineName).WithConsoleSink();
        var result = builder.BuildAndCommit();
        HeraldRegistry.Register(TenantA, PipelineName, builder, result);

        _observed.Should().BeEmpty();

        // Re-wire so Dispose's symmetric -= still finds something to remove.
        HeraldRegistry.OnTenantRegistration += _handler;
    }

    [Fact]
    public void Two_distinct_pipelines_fire_two_events()
    {
        var builderA = QuickLogBuilder.Create(PipelineName).WithConsoleSink();
        var resultA = builderA.BuildAndCommit();
        var builderB = QuickLogBuilder.Create(PipelineName2).WithConsoleSink();
        var resultB = builderB.BuildAndCommit();

        HeraldRegistry.Register(TenantA, PipelineName, builderA, resultA);
        HeraldRegistry.Register(TenantA, PipelineName2, builderB, resultB);

        _observed.Should().HaveCount(2);
        _observed.Should().Contain((TenantA, PipelineName));
        _observed.Should().Contain((TenantA, PipelineName2));
    }
}

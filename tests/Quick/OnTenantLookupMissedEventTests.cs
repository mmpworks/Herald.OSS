#nullable enable

using System;
using System.Collections.Generic;
using FluentAssertions;
using MMP.Herald.OSS.Tests.Helpers;
using MMP.Herald.Quick;
using Xunit;

namespace MMP.Herald.OSS.Tests.Quick;

/// <summary>
/// Pins the <see cref="HeraldRegistryInstance.OnTenantLookupMissed"/>
/// architectural seam. A downstream host subscribes to receive
/// <c>(tenant, name)</c> for every <c>Get</c> / <c>Require</c> miss so
/// it can detect cross-tenant probes without modifying OSS source.
/// </summary>
[Collection(DefaultHostCollection.Name)]
public sealed class OnTenantLookupMissedEventTests : IDisposable
{
    private const string TenantA = "miss-tenant-a";
    private const string PipelineName = "miss-pipeline";

    private readonly List<(string Tenant, string Name)> _observed = new();
    private readonly Action<string, string> _handler;

    public OnTenantLookupMissedEventTests()
    {
        _handler = (tenant, name) => _observed.Add((tenant, name));
        HeraldRegistry.OnTenantLookupMissed += _handler;
    }

    public void Dispose()
    {
        HeraldRegistry.OnTenantLookupMissed -= _handler;
        HeraldRegistry.Remove(TenantA, PipelineName);
        HeraldRegistry.Remove(HeraldTenant.Default, PipelineName);
    }

    [Fact]
    public void Get_miss_fires_event_with_normalised_tenant_id()
    {
        var resolved = HeraldRegistry.Get("Miss-Tenant-A", PipelineName);

        resolved.Should().BeNull();
        _observed.Should().ContainSingle()
            .Which.Should().Be((TenantA, PipelineName));
    }

    [Fact]
    public void Require_miss_fires_event_before_throwing()
    {
        var act = () => HeraldRegistry.Require(TenantA, PipelineName);

        act.Should().Throw<InvalidOperationException>();
        _observed.Should().ContainSingle()
            .Which.Should().Be((TenantA, PipelineName));
    }

    [Fact]
    public void Get_hit_does_not_fire_event()
    {
        var builder = QuickLogBuilder.Create(PipelineName).WithConsoleSink();
        var result = builder.BuildAndCommit();
        HeraldRegistry.Register(TenantA, PipelineName, builder, result);

        var resolved = HeraldRegistry.Get(TenantA, PipelineName);

        resolved.Should().NotBeNull();
        _observed.Should().BeEmpty(
            "a successful lookup must NOT fire the miss event");
    }

    [Fact]
    public void Scoped_miss_when_default_has_registration_fires_with_scoped_tenant()
    {
        // Default has the pipeline; the scoped caller does not. The leak-
        // prevention contract returns null; the miss event fires with the
        // SCOPED tenant id so a subscriber can detect the cross-tenant
        // probe signal.
        var builder = QuickLogBuilder.Create(PipelineName).WithConsoleSink();
        var result = builder.BuildAndCommit();
        HeraldRegistry.Register(HeraldTenant.Default, PipelineName, builder, result);

        using (HeraldTenantScope.Push(TenantA))
        {
            var resolved = HeraldRegistry.Get(PipelineName);
            resolved.Should().BeNull();
        }

        _observed.Should().ContainSingle()
            .Which.Tenant.Should().Be(TenantA,
                "the event payload's tenant must be the scoped tenant — that's the signal a probe detector needs");
    }

    [Fact]
    public void Contains_miss_does_not_fire_event()
    {
        // Contains is a probe, not a routing failure. By design it does
        // NOT fire OnTenantLookupMissed so a subscriber that only wants
        // routing-failure signals does not get drowned in probe traffic.
        HeraldRegistry.Contains(TenantA, PipelineName).Should().BeFalse();

        _observed.Should().BeEmpty();
    }

    [Fact]
    public void Multiple_subscribers_all_receive_miss()
    {
        var second = new List<(string Tenant, string Name)>();
        Action<string, string> secondHandler = (t, n) => second.Add((t, n));
        HeraldRegistry.OnTenantLookupMissed += secondHandler;
        try
        {
            HeraldRegistry.Get(TenantA, PipelineName);

            _observed.Should().ContainSingle();
            second.Should().ContainSingle();
        }
        finally
        {
            HeraldRegistry.OnTenantLookupMissed -= secondHandler;
        }
    }

    [Fact]
    public void Unsubscribing_stops_notifications()
    {
        HeraldRegistry.OnTenantLookupMissed -= _handler;

        HeraldRegistry.Get(TenantA, PipelineName);

        _observed.Should().BeEmpty();

        HeraldRegistry.OnTenantLookupMissed += _handler;
    }
}

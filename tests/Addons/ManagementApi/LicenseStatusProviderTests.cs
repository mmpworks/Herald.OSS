#nullable enable

using System;
using FluentAssertions;
using MMP.Herald.Addons.ManagementApi;
using MMP.Herald.Quick;
using Xunit;

namespace MMP.Herald.OSS.Tests.Addons.ManagementApi;

/// <summary>
/// Pins the ADR-001 Seam 7 contract:
/// <see cref="HeraldManagementApi.LicenseStatusProvider"/> is an
/// optional pluggable delegate returning a
/// <see cref="LicenseStatusSnapshot"/>. The Dashboard (and any other
/// observer) reads license state through the OSS facade without
/// importing any Server-internal type.
///
/// <para>
/// Tests cover: default-null behaviour, snapshot pass-through,
/// null-returning delegate handled gracefully, and the ability to
/// swap the delegate mid-lifetime (license loader booting after the
/// API has been created).
/// </para>
/// </summary>
public sealed class LicenseStatusProviderTests
{
    [Fact]
    public void LicenseStatusProvider_default_is_null()
    {
        // Pin the default-null shape so a fresh API has nothing to
        // report. Observers must tolerate a null provider — the
        // Dashboard renders an "unknown" badge instead of
        // crashing.
        var builder = QuickLogBuilder.Create().WithConsoleSink();
        var result  = builder.BuildAndCommit();
        var api     = new HeraldManagementApi(builder, result, AllowAllAuthorizer.Instance);

        api.LicenseStatusProvider.Should().BeNull(
            "default-null shape preserves the no-license-reporting baseline for Community deployments");
    }

    [Fact]
    public void LicenseStatusProvider_returns_snapshot_passed_through_unchanged()
    {
        // Pin the happy-path round-trip: the snapshot the delegate
        // returns is the exact snapshot the observer sees. Required
        // by Dashboard caching — a snapshot that compares equal to
        // the previous one skips a re-render.
        var expected = new LicenseStatusSnapshot(
            Kind:      "valid",
            Edition:   "Enterprise",
            ExpiresAt: new DateTimeOffset(2099, 1, 1, 0, 0, 0, TimeSpan.Zero),
            Reason:    null);

        var builder = QuickLogBuilder.Create().WithConsoleSink();
        var result  = builder.BuildAndCommit();
        var api     = new HeraldManagementApi(builder, result, AllowAllAuthorizer.Instance)
        {
            LicenseStatusProvider = () => expected,
        };

        var observed = api.LicenseStatusProvider!.Invoke();

        observed.Should().Be(expected,
            "the snapshot the delegate returns must arrive at the observer unchanged");
    }

    [Fact]
    public void LicenseStatusProvider_can_return_null_to_signal_no_snapshot_available()
    {
        // Pin the "delegate can return null" contract: a license
        // loader still booting, a transient failure to read the
        // license file, or a host that just doesn't want to report
        // right now all return null. Observers must tolerate it.
        var builder = QuickLogBuilder.Create().WithConsoleSink();
        var result  = builder.BuildAndCommit();
        var api     = new HeraldManagementApi(builder, result, AllowAllAuthorizer.Instance)
        {
            LicenseStatusProvider = () => null,
        };

        var observed = api.LicenseStatusProvider!.Invoke();

        observed.Should().BeNull(
            "a null return means 'no snapshot available right now' — observers render an unknown badge");
    }

    [Fact]
    public void LicenseStatusProvider_can_be_swapped_after_construction()
    {
        // Pin the setter contract: a host that boots without a
        // license loader and wires one later (background license
        // bootstrap, hot-reload) can plug the delegate in
        // post-construction. Without this, every host would have
        // to know its license state at boot.
        var first = new LicenseStatusSnapshot("trial", "Community", null, "in trial");
        var second = new LicenseStatusSnapshot("valid", "Enterprise", null, null);

        var builder = QuickLogBuilder.Create().WithConsoleSink();
        var result  = builder.BuildAndCommit();
        var api     = new HeraldManagementApi(builder, result, AllowAllAuthorizer.Instance);

        api.LicenseStatusProvider.Should().BeNull("starts null");

        api.LicenseStatusProvider = () => first;
        api.LicenseStatusProvider!.Invoke().Should().Be(first);

        api.LicenseStatusProvider = () => second;
        api.LicenseStatusProvider!.Invoke().Should().Be(second,
            "swapping the delegate mid-lifetime updates what observers see");

        api.LicenseStatusProvider = null;
        api.LicenseStatusProvider.Should().BeNull("clearing the delegate is the symmetric undo");
    }
}

#nullable enable

using System;
using System.Collections.Generic;
using FluentAssertions;
using MMP.Herald.Diagnostics;
using MMP.Herald.Templating;
using MMP.Herald.Templating.NamingPolicies;
using Xunit;

namespace MMP.Herald.OSS.Tests.Templating;

/// <summary>
/// Pins the principal-review #16 fix: <see cref="NameResolverCache"/>
/// surfaces a repeating <see cref="HeraldRuntimeMessages"/> notice on
/// cap-hit instead of the prior fire-exactly-once cliff. Multi-tenant
/// hosts that crossed the cap once keep getting the signal so the silent
/// "zero-allocation accept path" claim regression is observable.
///
/// <para>
/// Tests serialise on the shared
/// <see cref="MMP.Herald.OSS.Tests.Helpers.DefaultHostCollection"/> — the
/// same collection as <see cref="NameResolverCacheTests"/> — so the
/// process-static cache reset and the process-wide runtime-message channel
/// are not raced against parallel suites.
/// </para>
/// </summary>
[Collection(MMP.Herald.OSS.Tests.Helpers.DefaultHostCollection.Name)]
public sealed class NameResolverCacheCapHitNoticeTests : IDisposable
{
    private readonly List<RuntimeNotice> _observed = new();
    private readonly Action<RuntimeNotice> _handler;

    public NameResolverCacheCapHitNoticeTests()
    {
        NameResolverCache.Reset();
        HeraldRuntimeMessages.ClearRecent();
        _handler = notice => _observed.Add(notice);
        HeraldRuntimeMessages.OnNotice += _handler;
    }

    public void Dispose()
    {
        HeraldRuntimeMessages.OnNotice -= _handler;
        NameResolverCache.Reset();
        HeraldRuntimeMessages.ClearRecent();
    }

    [Fact]
    public void Cap_hit_publishes_warning_runtime_notice()
    {
        // Drive the cache to its CapacityLimit with unique templates, then
        // one more unique template trips the cap and surfaces the notice.
        FillCacheToCap();
        TripCapWithUniqueTemplate(suffix: "trip-a");

        _observed.Should().NotBeEmpty("first cap-hit must surface a notice");
        var first = _observed[0];
        first.Severity.Should().Be(NoticeSeverity.Warning);
        first.Source.Should().Be(nameof(NameResolverCache));
        first.Message.Should().Contain(NameResolverCache.CapacityLimit.ToString());
    }

    [Fact]
    public void Cap_hit_notice_is_throttled_within_one_second_window()
    {
        // Throttle is 1 s. Two cap-hits inside the window must yield exactly
        // one notice — closes the "flood the channel under burst miss"
        // failure mode the throttle exists to prevent.
        FillCacheToCap();
        TripCapWithUniqueTemplate(suffix: "trip-b1");
        TripCapWithUniqueTemplate(suffix: "trip-b2");
        TripCapWithUniqueTemplate(suffix: "trip-b3");

        _observed.Should().HaveCount(1,
            "throttle window collapses a burst of cap-hits into a single notice");
    }

    [Fact]
    public void Reset_rearms_the_cap_hit_notice()
    {
        // First trip — notice fires.
        FillCacheToCap();
        TripCapWithUniqueTemplate(suffix: "trip-c1");
        _observed.Should().HaveCount(1);

        // After Reset, the cache is empty so a new trip cycle exercises
        // the path fresh. The throttle must rearm so the next cap-hit
        // produces a notice immediately, not after another second.
        NameResolverCache.Reset();
        FillCacheToCap();
        TripCapWithUniqueTemplate(suffix: "trip-c2");

        _observed.Should().HaveCount(2,
            "Reset must rearm the throttle so the next cap-hit fires immediately");
    }

    private static void FillCacheToCap()
    {
        var policy = PascalCasePolicy.Instance;
        var (tokens, argExprs) = SinglePair("UserId", "userId");
        for (var i = 0; i < NameResolverCache.CapacityLimit; i++)
        {
            // string.Intern: the cache keys the template by reference identity
            // and only inserts interned templates (typed-args literals are
            // compile-time interned). These runtime-built fill keys must be
            // interned to land in the cache and drive it to its cap.
            NameResolverCache.Resolve(policy, string.Intern($"fill-template-{i}"), tokens, argExprs);
        }
    }

    private static void TripCapWithUniqueTemplate(string suffix)
    {
        var policy = PascalCasePolicy.Instance;
        var (tokens, argExprs) = SinglePair("UserId", "userId");
        // Unique-by-suffix template so the cache cannot hit on this key
        // and the cold-miss path runs through the cap check. Interned so the
        // insert guard treats it like a real (literal) typed-args template.
        NameResolverCache.Resolve(policy, string.Intern($"over-cap-template-{suffix}"), tokens, argExprs);
    }

    private static (MessageTemplateToken.Property[], string[]) SinglePair(string token, string argExpr)
    {
        var tokens = new[]
        {
            new MessageTemplateToken.Property(token, LogPropertyCaptureMode.Default, null, "{" + token + "}"),
        };
        return (tokens, new[] { argExpr });
    }
}

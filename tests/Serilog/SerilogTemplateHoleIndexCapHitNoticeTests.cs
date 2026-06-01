#nullable enable
#if NET9_0_OR_GREATER

using System;
using System.Collections.Generic;
using FluentAssertions;
using MMP.Herald.Diagnostics;
using MMP.Herald.Serilog;
using Xunit;

namespace MMP.Herald.OSS.Tests.Serilog;

/// <summary>
/// G1.2: <see cref="SerilogTemplateHoleIndex"/> surfaces a throttled
/// <see cref="HeraldRuntimeMessages"/> notice on cap-hit, mirroring the
/// <c>NameResolverCache</c> cap-hit notice. Once the index reaches
/// <see cref="SerilogTemplateHoleIndex.CapacityLimit"/>, every subsequent unique
/// interned template re-parses on each dispatch — the notice keeps that cliff
/// observable instead of letting it go silent.
///
/// <para>
/// Tests serialise on a dedicated collection so the process-static singleton's
/// cache reset and the process-wide runtime-message channel are not raced by
/// parallel suites.
/// </para>
/// </summary>
[Collection(nameof(SerilogTemplateHoleIndexCapHitNoticeTests))]
[CollectionDefinition(nameof(SerilogTemplateHoleIndexCapHitNoticeTests), DisableParallelization = true)]
public sealed class SerilogTemplateHoleIndexCapHitNoticeTests : IDisposable
{
    private readonly List<RuntimeNotice> _observed = new();
    private readonly Action<RuntimeNotice> _handler;

    public SerilogTemplateHoleIndexCapHitNoticeTests()
    {
        SerilogTemplateHoleIndex.Instance.Reset();
        HeraldRuntimeMessages.ClearRecent();
        _handler = notice => _observed.Add(notice);
        HeraldRuntimeMessages.OnNotice += _handler;
    }

    public void Dispose()
    {
        HeraldRuntimeMessages.OnNotice -= _handler;
        SerilogTemplateHoleIndex.Instance.Reset();
        HeraldRuntimeMessages.ClearRecent();
    }

    [Fact]
    public void Cap_hit_publishes_warning_runtime_notice()
    {
        FillIndexToCap();
        TripCapWithUniqueTemplate(suffix: "trip-a");

        _observed.Should().NotBeEmpty("first cap-hit must surface a notice");
        var first = _observed[0];
        first.Severity.Should().Be(NoticeSeverity.Warning);
        first.Source.Should().Be(nameof(SerilogTemplateHoleIndex));
        first.Message.Should().Contain(SerilogTemplateHoleIndex.CapacityLimit.ToString());
    }

    [Fact]
    public void Cap_hit_notice_is_throttled_within_one_second_window()
    {
        // Throttle is 1 s. Three cap-hits inside the window collapse to exactly
        // one notice — closes the "flood the channel under burst miss" mode.
        FillIndexToCap();
        TripCapWithUniqueTemplate(suffix: "trip-b1");
        TripCapWithUniqueTemplate(suffix: "trip-b2");
        TripCapWithUniqueTemplate(suffix: "trip-b3");

        _observed.Should().HaveCount(1,
            "throttle window collapses a burst of cap-hits into a single notice");
    }

    [Fact]
    public void Reset_rearms_the_cap_hit_notice()
    {
        FillIndexToCap();
        TripCapWithUniqueTemplate(suffix: "trip-c1");
        _observed.Should().HaveCount(1);

        // After Reset the index is empty and the throttle is rearmed, so the
        // next cap-hit fires immediately rather than after another second.
        SerilogTemplateHoleIndex.Instance.Reset();
        FillIndexToCap();
        TripCapWithUniqueTemplate(suffix: "trip-c2");

        _observed.Should().HaveCount(2,
            "Reset must rearm the throttle so the next cap-hit fires immediately");
    }

    // Drive the index to CapacityLimit with unique interned templates. Only
    // interned templates are eligible for caching (the index keys by reference
    // identity), so the fill keys are interned to actually land in the cache.
    private static void FillIndexToCap()
    {
        for (var i = 0; i < SerilogTemplateHoleIndex.CapacityLimit; i++)
        {
            SerilogTemplateHoleIndex.Instance.ResolveEntry(
                string.Intern($"hole-fill {{V{i}}}"));
        }
    }

    // A unique interned template the cache cannot hold (it is full): the
    // cold-miss path runs through the cap check and surfaces the notice.
    private static void TripCapWithUniqueTemplate(string suffix)
    {
        SerilogTemplateHoleIndex.Instance.ResolveEntry(
            string.Intern($"over-cap {{V_{suffix}}}"));
    }

    // ── G2.1: concurrent cap-fill stays approximately bounded ─────────────────
    // The frozen-at-cap insert is "check Count < cap, then TryAdd" — deliberately
    // NOT atomic, to keep the hot-path read lock-free. Under concurrency several
    // threads can pass the Count check before any adds, so the cache can overshoot
    // the cap by a small, bounded amount (roughly the number of racing threads).
    // This test pins that the cache is APPROXIMATELY bounded: it never grows
    // without limit, it lands at-or-above the cap, and the overshoot stays within
    // a generous concurrency slack. A regression that drops the cap check entirely
    // would blow far past this bound.
    [Fact]
    public void Concurrent_cap_fill_stays_approximately_bounded()
    {
        const int threads = 8;
        // Each thread tries to insert far more unique interned templates than the
        // cap, all racing on the same singleton.
        const int perThreadAttempts = SerilogTemplateHoleIndex.CapacityLimit;

        var tasks = new System.Threading.Tasks.Task[threads];
        for (var t = 0; t < threads; t++)
        {
            var threadId = t;
            tasks[t] = System.Threading.Tasks.Task.Run(() =>
            {
                for (var i = 0; i < perThreadAttempts; i++)
                {
                    // Distinct interned templates so every call is a cold miss
                    // that contends on the Count-check + TryAdd path.
                    SerilogTemplateHoleIndex.Instance.ResolveEntry(
                        string.Intern($"race-{threadId}-{i} {{V}}"));
                }
            });
        }

        System.Threading.Tasks.Task.WaitAll(tasks);

        // At or above the cap (the fill far exceeded it).
        SerilogTemplateHoleIndex.Instance.Count.Should().BeGreaterThanOrEqualTo(
            SerilogTemplateHoleIndex.CapacityLimit,
            "the fill far exceeds the cap, so the cache must be full");

        // Bounded: the overshoot from the non-atomic check is small relative to
        // the cap. A generous slack (well under one extra cap's worth) confirms
        // the cap check is doing its job under contention — not a tight fit, a
        // "did not run away" guard.
        SerilogTemplateHoleIndex.Instance.Count.Should().BeLessThan(
            SerilogTemplateHoleIndex.CapacityLimit + 1024,
            "frozen-at-cap tolerates a small concurrency overshoot but must not grow unbounded");
    }
}

#endif

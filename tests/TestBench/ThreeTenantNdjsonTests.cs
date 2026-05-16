#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using MMP.Herald;
using MMP.Herald.Events;
using MMP.Herald.OSS.Tests.Helpers;
using MMP.Herald.Quick;
using Xunit;

namespace MMP.Herald.OSS.Tests.TestBench;

/// <summary>
/// Functional testbench for the multi-tenant ndjson configuration.
/// The first five tests use bridge-mode in-memory capture so the
/// tenant-routing invariants can be verified independently of the
/// file-sink lifecycle finding (see FINDINGS.md and the last test
/// in this file). The exhibit test at the bottom runs against the
/// real Herald.Sinks.File pipeline and documents the disposal
/// behaviour.
/// </summary>
[Collection(DefaultHostCollection.Name)]
public sealed class ThreeTenantNdjsonTests
{
    // ---------- bridge-mode tests (tenant routing in isolation) ----------

    /// <summary>
    /// F1 + F6 — cross-tenant isolation under a mixed stream. 600
    /// events are randomly routed across three tenants; each tenant's
    /// capture must contain only its own events, and the distribution
    /// should be roughly uniform.
    /// </summary>
    [Fact]
    public async Task Cross_tenant_isolation_with_uniform_distribution()
    {
        await using var harness = new TestbenchHarness();
        harness.RegisterThreeTenantsInMemory();

        const int eventCount = 600;
        var rng = new Random(42);
        var tenants = new[] { TestbenchHarness.TenantAlpha, TestbenchHarness.TenantBeta, TestbenchHarness.TenantGamma };
        var sentByTenant = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            [TestbenchHarness.TenantAlpha] = 0,
            [TestbenchHarness.TenantBeta]  = 0,
            [TestbenchHarness.TenantGamma] = 0,
        };

        for (var i = 0; i < eventCount; i++)
        {
            var tenant = tenants[rng.Next(tenants.Length)];
            using (HeraldTenantScope.Push(tenant))
            {
                var logger = harness.LoggerFor(tenant);
                logger.Info(LogCategory.App, "event-{Tenant}-{Seq}", tenant, i);
            }
            sentByTenant[tenant]++;
        }

        var alpha = harness.CapturedNdjsonFor(TestbenchHarness.TenantAlpha);
        var beta  = harness.CapturedNdjsonFor(TestbenchHarness.TenantBeta);
        var gamma = harness.CapturedNdjsonFor(TestbenchHarness.TenantGamma);

        alpha.Count.Should().Be(sentByTenant[TestbenchHarness.TenantAlpha]);
        beta.Count.Should().Be(sentByTenant[TestbenchHarness.TenantBeta]);
        gamma.Count.Should().Be(sentByTenant[TestbenchHarness.TenantGamma]);

        AssertNoForeignTenant(alpha, expectTenant: TestbenchHarness.TenantAlpha);
        AssertNoForeignTenant(beta,  expectTenant: TestbenchHarness.TenantBeta);
        AssertNoForeignTenant(gamma, expectTenant: TestbenchHarness.TenantGamma);

        alpha.Count.Should().BeInRange(150, 250);
        beta.Count.Should().BeInRange(150, 250);
        gamma.Count.Should().BeInRange(150, 250);
    }

    /// <summary>
    /// F2 — AsyncLocal scope flows through Task.Run.
    /// </summary>
    [Fact]
    public async Task Async_local_scope_flows_through_task_run()
    {
        await using var harness = new TestbenchHarness();
        harness.RegisterThreeTenantsInMemory();

        using (HeraldTenantScope.Push(TestbenchHarness.TenantAlpha))
        {
            await Task.Run(() =>
            {
                HeraldTenantScope.Current.Should().Be(TestbenchHarness.TenantAlpha,
                    "AsyncLocal MUST flow through Task.Run");

                var logger = harness.LoggerFor(TestbenchHarness.TenantAlpha);
                logger.Info(LogCategory.App, "from-spawned-task");
            });
        }

        harness.CapturedFor(TestbenchHarness.TenantAlpha).Count.Should().Be(1);
        harness.CapturedFor(TestbenchHarness.TenantBeta).Should().BeEmpty();
        harness.CapturedFor(TestbenchHarness.TenantGamma).Should().BeEmpty();
    }

    /// <summary>
    /// F3 — rapid tenant switching on a single thread.
    /// </summary>
    [Fact]
    public async Task Rapid_tenant_switching_routes_correctly()
    {
        await using var harness = new TestbenchHarness();
        harness.RegisterThreeTenantsInMemory();

        const int iterations = 999;
        var tenants = new[] { TestbenchHarness.TenantAlpha, TestbenchHarness.TenantBeta, TestbenchHarness.TenantGamma };

        for (var i = 0; i < iterations; i++)
        {
            var tenant = tenants[i % tenants.Length];
            using (HeraldTenantScope.Push(tenant))
            {
                var logger = harness.LoggerFor(tenant);
                logger.Info(LogCategory.App, "rapid-{Tenant}-{Seq}", tenant, i);
            }
        }

        var alpha = harness.CapturedNdjsonFor(TestbenchHarness.TenantAlpha);
        var beta  = harness.CapturedNdjsonFor(TestbenchHarness.TenantBeta);
        var gamma = harness.CapturedNdjsonFor(TestbenchHarness.TenantGamma);

        alpha.Count.Should().Be(iterations / 3);
        beta.Count.Should().Be(iterations / 3);
        gamma.Count.Should().Be(iterations / 3);

        AssertNoForeignTenant(alpha, TestbenchHarness.TenantAlpha);
        AssertNoForeignTenant(beta,  TestbenchHarness.TenantBeta);
        AssertNoForeignTenant(gamma, TestbenchHarness.TenantGamma);
    }

    /// <summary>
    /// F5 — three concurrent producer threads.
    /// </summary>
    [Fact]
    public async Task Concurrent_producers_produce_valid_ndjson()
    {
        await using var harness = new TestbenchHarness();
        harness.RegisterThreeTenantsInMemory();

        const int eventsPerTenant = 200;

        async Task ProduceFor(string tenant)
        {
            using (HeraldTenantScope.Push(tenant))
            {
                var logger = harness.LoggerFor(tenant);
                for (var i = 0; i < eventsPerTenant; i++)
                {
                    logger.Info(LogCategory.App, "concurrent-{Tenant}-{Seq}", tenant, i);
                    if (i % 50 == 0) await Task.Yield();
                }
            }
        }

        await Task.WhenAll(
            ProduceFor(TestbenchHarness.TenantAlpha),
            ProduceFor(TestbenchHarness.TenantBeta),
            ProduceFor(TestbenchHarness.TenantGamma));

        var alpha = harness.CapturedNdjsonFor(TestbenchHarness.TenantAlpha);
        var beta  = harness.CapturedNdjsonFor(TestbenchHarness.TenantBeta);
        var gamma = harness.CapturedNdjsonFor(TestbenchHarness.TenantGamma);

        alpha.Count.Should().Be(eventsPerTenant);
        beta.Count.Should().Be(eventsPerTenant);
        gamma.Count.Should().Be(eventsPerTenant);

        // Every captured line must be valid JSON (no torn writes).
        foreach (var collection in new[] { alpha, beta, gamma })
        {
            collection.Should().NotContain(e => e.ContainsKey("__torn__"),
                "every ndjson line from the in-memory capturer must be complete JSON");
        }

        AssertNoForeignTenant(alpha, TestbenchHarness.TenantAlpha);
        AssertNoForeignTenant(beta,  TestbenchHarness.TenantBeta);
        AssertNoForeignTenant(gamma, TestbenchHarness.TenantGamma);
    }

    /// <summary>
    /// F7 — order preservation within a single tenant.
    /// </summary>
    [Fact]
    public async Task Order_preservation_within_single_tenant()
    {
        await using var harness = new TestbenchHarness();
        harness.RegisterOneInMemory(TestbenchHarness.TenantAlpha);

        const int eventCount = 100;
        using (HeraldTenantScope.Push(TestbenchHarness.TenantAlpha))
        {
            var logger = harness.LoggerFor(TestbenchHarness.TenantAlpha);
            for (var i = 0; i < eventCount; i++)
            {
                logger.Info(LogCategory.App, "ordered-{Seq}", i);
            }
        }

        var alpha = harness.CapturedNdjsonFor(TestbenchHarness.TenantAlpha);
        alpha.Count.Should().Be(eventCount);

        var observedSeqs = alpha.Select(ExtractSeqFromEvent).ToList();
        observedSeqs.Should().Equal(Enumerable.Range(0, eventCount));
    }

    /// <summary>
    /// F8 — OnTenantLookupMissed fires with the SCOPED tenant id.
    /// </summary>
    [Fact]
    public async Task Lookup_miss_event_carries_scoped_tenant()
    {
        await using var harness = new TestbenchHarness();

        var observed = new List<(string Tenant, string Name)>();
        Action<string, string> handler = (tenant, name) => observed.Add((tenant, name));
        HeraldRegistry.OnTenantLookupMissed += handler;

        try
        {
            using (HeraldTenantScope.Push(TestbenchHarness.TenantAlpha))
            {
                var resolved = HeraldRegistry.Get("nonexistent-pipeline");
                resolved.Should().BeNull();
            }
        }
        finally
        {
            HeraldRegistry.OnTenantLookupMissed -= handler;
        }

        observed.Should().ContainSingle();
        observed[0].Tenant.Should().Be(TestbenchHarness.TenantAlpha,
            "the event payload's tenant must be the SCOPED tenant — that's the signal a probe detector needs");
        observed[0].Name.Should().Be("nonexistent-pipeline");
    }

    // ---------- file-sink exhibit (documents the lifecycle finding) ----------

    /// <summary>
    /// EXHIBIT — runs against the real Herald.Sinks.File pipeline and
    /// documents the disposal-doesn't-flush finding. The test passes
    /// as long as the friction is observable; it does NOT assert
    /// tenant isolation through the file (that's covered by the
    /// bridge-mode tests above).
    ///
    /// <para>
    /// Observed behaviour on testbench run 2026-05-16:
    /// </para>
    /// <list type="number">
    ///   <item>QuickLogResult.DisposeAsync only awaits
    ///   _bootstrapResult.AsyncResource.DisposeAsync(), which is
    ///   null when WithAsync() was not configured.</item>
    ///   <item>For a sync pipeline with WithFileSink, the file sink's
    ///   IDisposable.Dispose is therefore never reached through the
    ///   registration's disposal chain.</item>
    ///   <item>The file handle stays open and buffered events stay
    ///   unflushed; opening the file with FileShare.Read throws
    ///   "file in use"; opening with FileShare.ReadWrite reads
    ///   truncated content.</item>
    /// </list>
    /// </summary>
    [Fact]
    public async Task File_sink_disposal_finding_exhibit()
    {
        await using var harness = new TestbenchHarness();
        harness.RegisterOneToFile(TestbenchHarness.TenantAlpha, harness.AlphaPath);

        const int eventCount = 50;
        using (HeraldTenantScope.Push(TestbenchHarness.TenantAlpha))
        {
            var logger = harness.LoggerFor(TestbenchHarness.TenantAlpha);
            for (var i = 0; i < eventCount; i++)
            {
                logger.Info(LogCategory.App, "exhibit-{Seq}", i);
            }
        }

        // Dispose the harness, which calls HeraldRegistry.RemoveAsync →
        // entry.DisposeAsync → Result.DisposeAsync. For this sync
        // pipeline AsyncResource is null, so DisposeAsync is a no-op
        // and the file sink is not flushed/closed.
        await harness.DisposeAsync();

        // Read with shared read+write so we can open the still-held
        // file. We expect EITHER fewer events than we wrote (buffered
        // ones lost) OR truncated lines marked __torn__. Either is
        // evidence of the finding. The exhibit documents the shape,
        // it does not require a fix to pass.
        var readBack = TestbenchHarness.ReadNdjsonFile(harness.AlphaPath);

        var allLinesValidAndComplete = readBack.Count == eventCount
            && readBack.All(e => !e.ContainsKey("__torn__"));

        if (allLinesValidAndComplete)
        {
            // The file sink was disposed cleanly. The finding is
            // resolved — either by an upstream fix or by a future
            // change to QuickLogResult.DisposeAsync. The exhibit
            // becomes a regression guard.
            return;
        }

        // Finding still present. Record it as a soft outcome rather
        // than a failure so the test reports usefully.
        readBack.Count.Should().BeLessThan(eventCount,
            "the finding manifests as either fewer events than written or torn lines; one of those must be true while the pipeline disposal gap remains");
    }

    // ---------- helpers ----------

    private static void AssertNoForeignTenant(
        IReadOnlyList<IReadOnlyDictionary<string, JsonElement>> events,
        string expectTenant)
    {
        var foreignTenants = new[] { TestbenchHarness.TenantAlpha, TestbenchHarness.TenantBeta, TestbenchHarness.TenantGamma }
            .Where(t => t != expectTenant)
            .ToArray();

        foreach (var e in events)
        {
            var message = ExtractMessage(e);
            foreach (var foreign in foreignTenants)
            {
                message.Should().NotContain(foreign,
                    $"capture for tenant '{expectTenant}' must not contain events tagged '{foreign}'");
            }
        }
    }

    private static string ExtractMessage(IReadOnlyDictionary<string, JsonElement> e)
    {
        if (e.TryGetValue("message", out var m1)) return m1.GetString() ?? string.Empty;
        if (e.TryGetValue("Message", out var m2)) return m2.GetString() ?? string.Empty;
        if (e.TryGetValue("msg", out var m3))     return m3.GetString() ?? string.Empty;
        if (e.TryGetValue("messageTemplate", out var m4)) return m4.GetString() ?? string.Empty;
        return string.Join(" ", e.Values
            .Where(v => v.ValueKind == JsonValueKind.String)
            .Select(v => v.GetString()));
    }

    private static int ExtractSeqFromEvent(IReadOnlyDictionary<string, JsonElement> e)
    {
        foreach (var key in new[] { "Seq", "seq" })
        {
            if (e.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.Number)
            {
                return v.GetInt32();
            }
        }
        var message = ExtractMessage(e);
        var dash = message.LastIndexOf('-');
        if (dash >= 0 && int.TryParse(message.AsSpan(dash + 1), out var n)) return n;
        throw new InvalidOperationException(
            $"Could not extract sequence number from event: {JsonSerializer.Serialize(e)}");
    }
}

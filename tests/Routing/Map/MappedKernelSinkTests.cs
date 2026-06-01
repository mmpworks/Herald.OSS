#nullable enable

using System;
using FluentAssertions;
using MMP.Herald.Events;
using MMP.Herald.Levels;
using MMP.Herald.OSS.Tests.Helpers;
using MMP.Herald.OSS.Tests.TestSupport;
using MMP.Herald.Pipeline.Kernel;
using MMP.Herald.Routing.Map;
using MMP.Herald.Templating;
using Xunit;

namespace MMP.Herald.OSS.Tests.Routing.Map;

/// <summary>
/// W7 — dynamic per-event sink routing (<c>Sinks.Map</c> equivalent). Pins the
/// route-isolation, keyless-destination, confidentiality, and open-key
/// cardinality-safety contracts from Echo's inventory and Iolo's red-team.
/// </summary>
public sealed class MappedKernelSinkTests
{
    private const string KeyProperty = "TenantId";
    private const string Template = "msg {TenantId}";

    // The canonical 0-alloc selector: slice the routing key string off the
    // buffer, never format. Returns empty for a keyless event.
    private static readonly LogEventBufferKeySelector ByTenant =
        static (in LogEventBuffer b) =>
            b.TryGetStringSpan(KeyProperty, out var k) ? k : ReadOnlySpan<char>.Empty;

    private static LogEventBuffer BufferForTenant(string? tenant)
    {
        var props = tenant is null
            ? Array.Empty<LogProperty>()
            : new[] { new LogProperty(KeyProperty, tenant) };
        return new LogEventBuffer(
            timeUtc: DateTimeOffset.UtcNow,
            level: KnownLogLevels.Information,
            category: LogCategory.App,
            messageTemplate: Template,
            message: "msg",
            properties: props);
    }

    // ── Closed-key route isolation ──────────────────────────────────────
    // The core confidentiality + correctness assertion: an event keyed "acme"
    // lands in file A and is ABSENT from file B. No cross-tenant bleed.

    [Fact]
    public void Closed_key_event_lands_in_its_route_and_nowhere_else()
    {
        var acme = new KernelSpySink(KeyProperty);
        var globex = new KernelSpySink(KeyProperty);

        var sink = MappedKernelSink.Route()
            .Add("acme", acme)
            .Add("globex", globex)
            .Build(ByTenant);

        var buffer = BufferForTenant("acme");
        sink.Log(in buffer);

        acme.BufferLogCount.Should().Be(1, "the acme event must land in the acme route");
        globex.BufferLogCount.Should().Be(0, "the acme event must be absent from the globex route");
    }

    [Fact]
    public void Closed_key_two_tenants_stay_isolated()
    {
        var acme = new KernelSpySink(KeyProperty);
        var globex = new KernelSpySink(KeyProperty);

        var sink = MappedKernelSink.Route()
            .Add("acme", acme)
            .Add("globex", globex)
            .Build(ByTenant);

        var a = BufferForTenant("acme");
        var g = BufferForTenant("globex");
        sink.Log(in a);
        sink.Log(in g);

        // Zero cross-tenant bleed: each spy saw exactly its own key, never the other's.
        acme.CapturedKeys.Should().ContainSingle().Which.Should().Be("acme");
        globex.CapturedKeys.Should().ContainSingle().Which.Should().Be("globex");
    }

    // ── Keyless-event destination ───────────────────────────────────────

    [Fact]
    public void Keyless_event_goes_to_default_never_to_a_tenant_route()
    {
        var acme = new KernelSpySink(KeyProperty);
        var fallback = new KernelSpySink(KeyProperty);

        var sink = MappedKernelSink.Route()
            .Add("acme", acme)
            .Default(fallback)
            .Build(ByTenant);

        var keyless = BufferForTenant(null);
        sink.Log(in keyless);

        fallback.BufferLogCount.Should().Be(1, "a keyless event routes to the default sink");
        acme.BufferLogCount.Should().Be(0, "a keyless event must never land in a tenant route");
    }

    [Fact]
    public void Keyless_event_with_no_default_is_dropped()
    {
        var acme = new KernelSpySink();
        var sink = MappedKernelSink.Route().Add("acme", acme).Build(ByTenant);

        var keyless = BufferForTenant(null);
        sink.Log(in keyless);

        acme.BufferLogCount.Should().Be(0, "no default means a keyless event is silently dropped");
    }

    [Fact]
    public void Unmatched_key_goes_to_default()
    {
        var acme = new KernelSpySink();
        var fallback = new KernelSpySink();
        var sink = MappedKernelSink.Route().Add("acme", acme).Default(fallback).Build(ByTenant);

        var unknown = BufferForTenant("unknown-tenant");
        sink.Log(in unknown);

        fallback.BufferLogCount.Should().Be(1);
        acme.BufferLogCount.Should().Be(0);
    }

    // ── Open-key: first-sight creation + seen routing ───────────────────

    [Fact]
    public void Open_key_auto_creates_a_route_on_first_sight()
    {
        var created = new System.Collections.Concurrent.ConcurrentDictionary<string, KernelSpySink>();
        var sink = MappedKernelSink.Route()
            .WithFactory(key => created.GetOrAdd(key, _ => new KernelSpySink()))
            .Build(ByTenant);

        var a = BufferForTenant("acme");
        sink.Log(in a);

        sink.DynamicRouteCount.Should().Be(1, "first sight of acme creates one route");
        created["acme"].BufferLogCount.Should().Be(1);
    }

    [Fact]
    public void Open_key_routes_seen_key_to_the_same_sink()
    {
        var created = new System.Collections.Concurrent.ConcurrentDictionary<string, KernelSpySink>();
        var sink = MappedKernelSink.Route()
            .WithFactory(key => created.GetOrAdd(key, _ => new KernelSpySink()))
            .Build(ByTenant);

        for (var i = 0; i < 5; i++)
        {
            var b = BufferForTenant("acme");
            sink.Log(in b);
        }

        sink.DynamicRouteCount.Should().Be(1, "five acme events create exactly one route");
        created["acme"].BufferLogCount.Should().Be(5, "all five land in the one acme sink");
    }

    [Fact]
    public void Open_key_keeps_two_dynamic_tenants_isolated()
    {
        var created = new System.Collections.Concurrent.ConcurrentDictionary<string, KernelSpySink>();
        var sink = MappedKernelSink.Route()
            .WithFactory(key => created.GetOrAdd(key, _ => new KernelSpySink(KeyProperty)))
            .Build(ByTenant);

        var a = BufferForTenant("acme");
        var g = BufferForTenant("globex");
        sink.Log(in a);
        sink.Log(in g);

        created["acme"].CapturedKeys.Should().ContainSingle().Which.Should().Be("acme");
        created["globex"].CapturedKeys.Should().ContainSingle().Which.Should().Be("globex");
    }

    // ── Open-key cardinality cap + overflow (Iolo) ──────────────────────

    [Fact]
    public void Open_key_cap_overflows_to_default_when_policy_is_route_to_default()
    {
        var fallback = new KernelSpySink();
        var sink = MappedKernelSink.Route()
            .Default(fallback)
            .WithFactory(_ => new KernelSpySink(),
                maxDynamicRoutes: 2,
                overflowPolicy: RouteOverflowPolicy.RouteToDefault)
            .Build(ByTenant);

        var t1 = BufferForTenant("t1"); sink.Log(in t1);
        var t2 = BufferForTenant("t2"); sink.Log(in t2);
        var t3 = BufferForTenant("t3"); sink.Log(in t3); // overflow

        sink.DynamicRouteCount.Should().Be(2, "the cap holds the dynamic route count at 2");
        sink.OverflowCount.Should().Be(1, "the third distinct key overflows");
        fallback.BufferLogCount.Should().Be(1, "the overflow event routes to default");
    }

    [Fact]
    public void Open_key_cap_drops_overflow_when_policy_is_drop()
    {
        var fallback = new KernelSpySink();
        var sink = MappedKernelSink.Route()
            .Default(fallback)
            .WithFactory(_ => new KernelSpySink(),
                maxDynamicRoutes: 1,
                overflowPolicy: RouteOverflowPolicy.Drop)
            .Build(ByTenant);

        var t1 = BufferForTenant("t1"); sink.Log(in t1);
        var t2 = BufferForTenant("t2"); sink.Log(in t2); // overflow, dropped

        sink.OverflowCount.Should().Be(1);
        fallback.BufferLogCount.Should().Be(0,
            "Drop policy does not co-mingle the overflow event into the default sink");
    }

    // ── Open-key: key injection is refused (Iolo) ───────────────────────

    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("..")]
    [InlineData("tenant:with:colons")]
    public void Open_key_invalid_key_is_refused_and_counted(string badKey)
    {
        var fallback = new KernelSpySink();
        var sink = MappedKernelSink.Route()
            .Default(fallback)
            .WithFactory(_ => new KernelSpySink())
            .Build(ByTenant);

        var buffer = BufferForTenant(badKey);
        sink.Log(in buffer);

        sink.DynamicRouteCount.Should().Be(0, "an injection-shaped key must never create a route");
        sink.InvalidKeyCount.Should().Be(1);
        fallback.BufferLogCount.Should().Be(1, "the refused event routes to default");
    }

    // ── Open-key: factory throw is one event's blast radius (Iolo) ──────

    [Fact]
    public void Open_key_factory_throw_is_isolated_and_counted()
    {
        var fallback = new KernelSpySink();
        var sink = MappedKernelSink.Route()
            .Default(fallback)
            .WithFactory(_ => throw new InvalidOperationException("disk full"))
            .Build(ByTenant);

        var buffer = BufferForTenant("acme");
        var act = () => { var b = BufferForTenant("acme"); sink.Log(in b); };

        act.Should().NotThrow("a factory throw must not escape to the producer thread");
        sink.FactoryFailureCount.Should().Be(1);
        fallback.BufferLogCount.Should().Be(1, "the event whose factory failed routes to default");
    }

    [Fact]
    public void Open_key_factory_returning_null_is_isolated()
    {
        var fallback = new KernelSpySink();
        var sink = MappedKernelSink.Route()
            .Default(fallback)
            .WithFactory(_ => null!)
            .Build(ByTenant);

        var buffer = BufferForTenant("acme");
        sink.Log(in buffer);

        sink.FactoryFailureCount.Should().Be(1);
        fallback.BufferLogCount.Should().Be(1);
    }

    // ── Heap twin routes identically to the buffer path ─────────────────

    [Fact]
    public void Heap_event_routes_to_the_same_route_as_the_buffer_path()
    {
        var acme = new KernelSpySink(KeyProperty);
        var globex = new KernelSpySink(KeyProperty);
        var sink = MappedKernelSink.Route().Add("acme", acme).Add("globex", globex).Build(ByTenant);

        var heapEvent = new LogEvent(
            TimeUtc: DateTimeOffset.UtcNow,
            Level: KnownLogLevels.Information,
            Category: LogCategory.App,
            MessageTemplate: Template,
            Message: "msg",
            Properties: new[] { new LogProperty(KeyProperty, "acme") },
            Context: LogEvent.EmptyContext);

        sink.Log(heapEvent);

        acme.TotalLogCount.Should().Be(1, "the heap event routes to acme just like the buffer path");
        globex.TotalLogCount.Should().Be(0);
    }

    [Fact]
    public void Closed_key_sink_reports_not_open_key()
    {
        var sink = MappedKernelSink.Route().Add("acme", new KernelSpySink()).Build(ByTenant);
        sink.IsOpenKey.Should().BeFalse();
        sink.DynamicRouteCount.Should().Be(0);
    }
}

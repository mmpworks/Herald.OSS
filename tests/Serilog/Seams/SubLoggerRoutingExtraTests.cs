#nullable enable
#if NET9_0_OR_GREATER

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using MMP.Herald.Serilog;
using MMP.Herald.Serilog.Configuration;
using MMP.Herald.Serilog.Core;
using MMP.Herald.Serilog.Filters;
using Xunit;
using NativeLogEvent = MMP.Herald.Events.LogEvent;
using MirrorLogEvent = MMP.Herald.Serilog.Events.LogEvent;

namespace MMP.Herald.OSS.Tests.Serilog.Seams;

/// <summary>
/// 0.12.7 — count-limit eviction, default-key fallback, WriteTo.Async pass-through,
/// Enrich.With&lt;T&gt;, and Serilog.Filters.Matching.WithProperty.
/// </summary>
public sealed class SubLoggerRoutingExtraTests
{
    private sealed class Recorder : ILogEventSink
    {
        public List<MirrorLogEvent> Events { get; } = new();
        public void Emit(MirrorLogEvent logEvent) => Events.Add(logEvent);
    }

    private sealed class TenantEnricher : ILogEventEnricher
    {
        public void Enrich(MirrorLogEvent logEvent, ILogEventPropertyFactory factory)
            => logEvent.AddPropertyIfAbsent(factory.CreateProperty("Tenant", "acme"));
    }

    private static string KeyFromRouteProperty(NativeLogEvent e)
        => e.GetProperty("Route")?.ResolvedValue?.ToString() ?? "default";

    // ── Count-limit eviction ───────────────────────────────────────────────────

    [Fact]
    public void WriteTo_Map_with_a_count_limit_still_routes_every_event()
    {
        // Pipeline-level smoke of Map under a count limit: routing stays correct
        // and never throws when more distinct keys than the limit arrive. Exact
        // eviction ORDER is proven deterministically (no pipeline-delivery
        // interleaving) by DirectRoute_eviction_builds_evicted_key_again.
        var sinksByKey = new Dictionary<string, Recorder>();

        var log = new LoggerConfiguration()
            .WriteTo.Map<string>(
                keySelector: KeyFromRouteProperty,
                configure: (key, wt) =>
                {
                    var rec = new Recorder();
                    sinksByKey[key] = rec;
                    wt.Sink(rec);
                },
                sinkMapCountLimit: 2)
            .CreateLogger();

        log.Information("1 {Route}", "a");
        log.Information("2 {Route}", "b");
        log.Information("3 {Route}", "c"); // exceeds the limit -> eviction happens internally

        ((IDisposable)log).Dispose();

        // Every event reached the sub-logger for its key while the key was live.
        sinksByKey.Should().ContainKeys("a", "b", "c");
        sinksByKey.Values.Sum(r => r.Events.Count).Should().Be(3,
            "all three events are routed even though the count limit forces eviction");
    }

    // ── Default-key fallback ───────────────────────────────────────────────────

    [Fact]
    public void WriteTo_Map_routes_missing_key_to_the_default_key()
    {
        var sinksByKey = new Dictionary<string, Recorder>();

        var log = new LoggerConfiguration()
            .WriteTo.Map(
                keyPropertyName: "Route",
                defaultKey: "FALLBACK",
                configure: (key, wt) =>
                {
                    var rec = new Recorder();
                    sinksByKey[key] = rec;
                    wt.Sink(rec);
                })
            .CreateLogger();

        log.Information("has key {Route}", "alpha");
        log.Information("no key at all"); // no Route property -> default

        ((IDisposable)log).Dispose();

        sinksByKey.Should().ContainKeys("alpha", "FALLBACK");
        sinksByKey["FALLBACK"].Events.Should().HaveCount(1,
            "an event without the key property routes to the default key");
    }

    // ── WriteTo.Async pass-through ─────────────────────────────────────────────

    [Fact]
    public void WriteTo_Async_delivers_events_to_the_wrapped_sink()
    {
        var inner = new Recorder();

        var log = new LoggerConfiguration()
            .WriteTo.Async(a => a.Sink(inner))
            .CreateLogger();

        log.Information("buffered {Id}", 9);

        ((IDisposable)log).Dispose();

        inner.Events.Should().HaveCount(1,
            "WriteTo.Async is a transparent wrapper; the wrapped sink still receives the event");
    }

    // ── Enrich.With<TEnricher>() ───────────────────────────────────────────────

    [Fact]
    public void Enrich_With_generic_registers_and_runs_the_enricher()
    {
        var received = new Recorder();

        var log = new LoggerConfiguration()
            .Enrich.With<TenantEnricher>()
            .WriteTo.Sink(received)
            .CreateLogger();

        log.Information("acted");

        var e = Assert.Single(received.Events);
        e.Properties.Should().ContainKey("Tenant",
            "Enrich.With<TenantEnricher>() must instantiate and run the enricher");
    }

    // ── Serilog.Filters.Matching.WithProperty ──────────────────────────────────

    [Fact]
    public void Matching_WithProperty_excludes_matching_events_via_Filter_ByExcluding()
    {
        var received = new Recorder();

        // Exclude any event that carries a non-empty "Drop" property; admit the rest.
        var log = new LoggerConfiguration()
            .Filter.ByExcluding(Matching.WithProperty<string>("Drop", x => !string.IsNullOrEmpty(x)))
            .WriteTo.Sink(received)
            .CreateLogger();

        log.Information("kept, no Drop property");
        log.Information("dropped {Drop}", "yes");
        log.Information("kept, empty {Drop}", "");

        ((IDisposable)log).Dispose();

        received.Events.Should().HaveCount(2,
            "only the event with a non-empty Drop property is excluded");
        received.Events.Select(e => e.RenderMessage())
            .Should().NotContain(m => m.Contains("dropped"));
    }

    [Fact]
    public void Matching_WithProperty_predicate_is_false_for_wrong_type()
    {
        // The property exists but is an int, while the predicate expects string.
        // Serilog parity: a type mismatch fails the match (no throw).
        var predicate = Matching.WithProperty<string>("Count", _ => true);

        var native = MakeEventWithProperty("Count", 42);

        predicate(native).Should().BeFalse(
            "a value whose type is not T must not match");
    }

    // Build a minimal native LogEvent carrying one structured property.
    private static NativeLogEvent MakeEventWithProperty(string name, object? value)
    {
        var props = new List<MMP.Herald.Templating.LogProperty>
        {
            new MMP.Herald.Templating.LogProperty(name, value)
        };
        return new NativeLogEvent(
            DateTimeOffset.UtcNow,
            MMP.Herald.Levels.KnownLogLevels.Information,
            MMP.Herald.Events.LogCategory.None,
            "t",
            "t",
            props,
            NativeLogEvent.EmptyContext);
    }

    [Fact]
    public void DirectRoute_eviction_builds_evicted_key_again()
    {
        var built = new System.Collections.Generic.List<string>();
        var route = new MMP.Herald.Serilog.Sinks.MapSubLoggerRoute<string>(
            keySelector: KeyFromRouteProperty,
            configure: (key, wt) => { built.Add(key); wt.Sink(new Recorder()); },
            countLimit: 2);

        route.Accept(MakeEventWithProperty("Route", "a"));
        route.Accept(MakeEventWithProperty("Route", "b"));
        route.Accept(MakeEventWithProperty("Route", "c"));
        route.Accept(MakeEventWithProperty("Route", "a"));

        built.Should().Equal("a", "b", "c", "a");
    }


    [Fact]
    public void WriteTo_Map_generic_by_property_name_routes_by_typed_key()
    {
        // The CLI's WriteTo.Map<bool>("PrintStderr", ...) shape: a typed key read
        // from a named property. Events route by the bool value of "Flag".
        var sinksByKey = new Dictionary<bool, Recorder>();

        var log = new LoggerConfiguration()
            .WriteTo.Map<bool>(
                keyPropertyName: "Flag",
                configure: (key, wt) =>
                {
                    var rec = new Recorder();
                    sinksByKey[key] = rec;
                    wt.Sink(rec);
                },
                sinkMapCountLimit: 4)
            .CreateLogger();

        log.Information("on {Flag}", true);
        log.Information("off {Flag}", false);
        log.Information("on again {Flag}", true);

        ((IDisposable)log).Dispose();

        sinksByKey.Should().ContainKeys(true, false);
        sinksByKey[true].Events.Should().HaveCount(2, "two events carried Flag=true");
        sinksByKey[false].Events.Should().HaveCount(1, "one event carried Flag=false");
    }

}
#endif

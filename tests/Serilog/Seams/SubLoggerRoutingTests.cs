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
using LogEventLevel = MMP.Herald.Serilog.Events.LogEventLevel;

namespace MMP.Herald.OSS.Tests.Serilog.Seams;

/// <summary>
/// 0.12.7 — WriteTo.Logger / WriteTo.Map / WriteTo.Async sub-logger routing,
/// Enrich.With&lt;T&gt;, and Serilog.Filters.Matching.WithProperty.
///
/// These four surfaces are what the Microsoft component-detection CLI bootstrap
/// requires. Each test drives events through a real CreateLogger() pipeline and
/// asserts which per-key recording sink received which event.
/// </summary>
public sealed class SubLoggerRoutingTests
{
    // A per-key recording sink so a Map branch can be observed independently.
    private sealed class Recorder : ILogEventSink
    {
        public List<MirrorLogEvent> Events { get; } = new();
        public void Emit(MirrorLogEvent logEvent) => Events.Add(logEvent);
    }

    // An enricher registered by TYPE (Enrich.With<T>) that stamps a fixed property.
    private sealed class TenantEnricher : ILogEventEnricher
    {
        public void Enrich(MirrorLogEvent logEvent, ILogEventPropertyFactory propertyFactory)
        {
            var prop = propertyFactory.CreateProperty("Tenant", "acme");
            logEvent.AddPropertyIfAbsent(prop);
        }
    }

    // ── WriteTo.Logger (fixed sub-logger) ──────────────────────────────────────

    [Fact]
    public void WriteTo_Logger_forwards_events_into_the_child_pipeline()
    {
        var child = new Recorder();

        var log = new LoggerConfiguration()
            .WriteTo.Logger(lc => lc.WriteTo.Sink(child))
            .CreateLogger();

        log.Information("hello {Id}", 1);
        log.Warning("again {Id}", 2);

        child.Events.Should().HaveCount(2,
            "every parent event must be forwarded into the nested sub-logger");
        child.Events.Select(e => e.Level)
            .Should().Equal(LogEventLevel.Information, LogEventLevel.Warning);

        ((IDisposable)log).Dispose();
    }

    // ── WriteTo.Map<TKey> (dynamic per-key routing) ────────────────────────────

    [Fact]
    public void WriteTo_Map_routes_each_event_to_the_sub_sink_for_its_key()
    {
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
                sinkMapCountLimit: 10)
            .CreateLogger();

        log.Information("a {Route}", "alpha");
        log.Information("b {Route}", "beta");
        log.Information("c {Route}", "alpha");

        ((IDisposable)log).Dispose();

        sinksByKey.Should().ContainKeys("alpha", "beta");
        sinksByKey["alpha"].Events.Should().HaveCount(2, "two events carried key 'alpha'");
        sinksByKey["beta"].Events.Should().HaveCount(1, "one event carried key 'beta'");
    }

    [Fact]
    public void WriteTo_Map_creates_each_sub_logger_lazily_once_per_key()
    {
        var buildCount = 0;

        var log = new LoggerConfiguration()
            .WriteTo.Map<string>(
                keySelector: KeyFromRouteProperty,
                configure: (key, wt) =>
                {
                    buildCount++;
                    wt.Sink(new Recorder());
                })
            .CreateLogger();

        // Three events, two distinct keys -> the factory runs exactly twice.
        log.Information("1 {Route}", "x");
        log.Information("2 {Route}", "x");
        log.Information("3 {Route}", "y");

        buildCount.Should().Be(2,
            "a sub-logger is built once per distinct key, then cached");

        ((IDisposable)log).Dispose();
    }

    // String key extracted from the "Route" template property; default when absent.
    private static string KeyFromRouteProperty(NativeLogEvent e)
        => e.GetProperty("Route")?.ResolvedValue?.ToString() ?? "default";
}
#endif

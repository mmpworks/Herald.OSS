#nullable enable

using System.Collections.Generic;
using FluentAssertions;
using Herald.OSS.Serilog.Expressions.Filtering;
using MMP.Herald.Serilog;
using MMP.Herald.Serilog.Core;
using MMP.Herald.Serilog.Events;
using Xunit;

namespace Herald.OSS.Serilog.Expressions.Tests;

/// <summary>
/// FIX 2 — the fluent <c>LoggerConfiguration.Filter</c> integration. Proves the
/// migrated call site <c>.Filter.ByExcluding("RequestPath like '/health%'")</c>
/// both COMPILES on the config chain and DROPS the matching event end-to-end
/// (the /health line never reaches the sink), which is the behaviour the Ref4
/// baseline relied on.
/// </summary>
public sealed class LoggerConfigurationFilterTests
{
    // Capturing sink: records every event the pipeline admits to it, so the test
    // can assert on what survived filtering (the emitted artifact, not config shape).
    private sealed class CapturingSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = new();
        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }

    [Fact]
    public void Filter_ByExcluding_string_dsl_drops_matching_events_end_to_end()
    {
        var sink = new CapturingSink();

        var logger = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            // The exact migrated call site: a string-DSL exclude filter wired
            // fluently onto the config chain via the expressions extension.
            .Filter.ByExcluding("RequestPath like '/health%'")
            .WriteTo.Sink(sink)
            .CreateLogger();

        logger.Information("Request {RequestPath} served", "/orders");      // admitted
        logger.Information("Request {RequestPath} served", "/health/live");  // EXCLUDED
        Log.CloseAndFlush();

        sink.Events.Should().HaveCount(1,
            "the /health line matches the exclude filter and must be dropped");
        sink.Events[0].RenderMessage().Should().Contain("/orders",
            "only the non-matching event survives to the sink");
    }

    [Fact]
    public void Filter_ByIncludingOnly_string_dsl_admits_only_matching_events()
    {
        var sink = new CapturingSink();

        var logger = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .Filter.ByIncludingOnly("RequestPath like '/api%'")
            .WriteTo.Sink(sink)
            .CreateLogger();

        logger.Information("Request {RequestPath} served", "/api/orders"); // admitted
        logger.Information("Request {RequestPath} served", "/health");      // dropped
        Log.CloseAndFlush();

        sink.Events.Should().HaveCount(1);
        sink.Events[0].RenderMessage().Should().Contain("/api/orders");
    }

    [Fact]
    public void Multiple_Filter_calls_compose_with_AND_semantics()
    {
        // Two excludes: an event must pass BOTH to survive. Serilog applies every
        // registered filter; the single-slot builder must not let the second
        // overwrite the first.
        var sink = new CapturingSink();

        var logger = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .Filter.ByExcluding("RequestPath like '/health%'")
            .Filter.ByExcluding("RequestPath like '/metrics%'")
            .WriteTo.Sink(sink)
            .CreateLogger();

        logger.Information("Request {RequestPath} served", "/orders");   // survives both
        logger.Information("Request {RequestPath} served", "/health/x");  // dropped by #1
        logger.Information("Request {RequestPath} served", "/metrics/y"); // dropped by #2
        Log.CloseAndFlush();

        sink.Events.Should().HaveCount(1,
            "both exclude filters must apply — neither overwrites the other");
        sink.Events[0].RenderMessage().Should().Contain("/orders");
    }
}

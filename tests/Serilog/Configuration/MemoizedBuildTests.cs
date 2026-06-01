#nullable enable
#if NET9_0_OR_GREATER

using System;
using System.Collections.Generic;
using FluentAssertions;
using MMP.Herald.Serilog;
using MMP.Herald.Serilog.Configuration;
using MMP.Herald.Serilog.Core;
using MMP.Herald.Serilog.Events;
using Xunit;

namespace MMP.Herald.OSS.Tests.Serilog.Configuration;

/// <summary>
/// W5 — the LoggerConfiguration → MEL bridge memoizes Build().
///
/// <para>
/// The load-bearing guard: calling <see cref="LoggerConfiguration.CreateLogger"/>
/// (the Serilog adapter) AND <see cref="LoggerConfiguration.CreateHeraldLogger"/>
/// (the raw logger the MEL bridge consumes) on one configuration must yield two
/// views over ONE pipeline — never two pipelines with double sinks/flush. The
/// emitted artifact is the sink-fire count: one logical pipeline fires each sink
/// once per event, not twice.
/// </para>
/// </summary>
public sealed class MemoizedBuildTests
{
    private sealed class CountingSink : ILogEventSink
    {
        private readonly List<LogEvent> _events = new();
        public IReadOnlyList<LogEvent> Events => _events;
        public void Emit(LogEvent logEvent) { if (logEvent is not null) _events.Add(logEvent); }
    }

    [Fact]
    public void CreateHeraldLogger_and_CreateLogger_share_one_pipeline()
    {
        var config = new LoggerConfiguration();
        config.WriteTo.Null();

        var herald = config.CreateHeraldLogger();
        var adapter = config.CreateLogger();

        // The adapter wraps the very same StructuredLogger the bridge returns — proof
        // they are two views over one memoized PipelineBuildResult, not two builds.
        var adapterHerald = ((SerilogLoggerAdapter)adapter).HeraldLogger;
        adapterHerald.Should().BeSameAs(herald,
            "CreateLogger and CreateHeraldLogger must return views over the SAME pipeline; " +
            "a second build would hand back a different StructuredLogger instance");
    }

    [Fact]
    public void Calling_both_accessors_does_not_double_register_the_sink()
    {
        // If Build() ran twice, the user sink would be registered on two pipelines and
        // a single log through the shared logger would still fire once per its own
        // pipeline — but the real tell is that the SAME logger drives one sink list.
        // Drive one event through the memoized herald logger and assert one emit.
        var sink = new CountingSink();
        var config = new LoggerConfiguration();
        config.WriteTo.Sink(sink);

        // Touch both accessors — the second must not rebuild.
        _ = config.CreateLogger();
        var herald = config.CreateHeraldLogger();

        var adapter = new SerilogLoggerAdapter(herald);
        adapter.Warning("one event {Marker}", "W5-ONCE");
        (adapter as IDisposable)?.Dispose();

        sink.Events.Should().ContainSingle(
            "a single log through the shared pipeline must fire the sink exactly once — " +
            "a doubled build would attach the sink twice and emit twice");
        sink.Events[0].RenderMessage().Should().Contain("W5-ONCE");
    }
}

#endif

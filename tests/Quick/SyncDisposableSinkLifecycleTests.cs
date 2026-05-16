#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MMP.Herald.Events;
using MMP.Herald.Pipeline;
using Xunit;

namespace MMP.Herald.OSS.Tests.Quick;

/// <summary>
/// Pins the contract closed by 0.2.3: when the pipeline assembly
/// resolves a sync <see cref="IDisposable"/> sink, the
/// <see cref="PipelineAssemblyBuilder"/> auto-tracks it on
/// SyncResources. The disposal walker on the bootstrap result then
/// reaches it during DisposeAsync.
///
/// <para>
/// The testbench branch's <c>FINDINGS.md</c> flagged this as
/// Finding 1 in 0.2.2; this test set is the regression coverage so
/// the gap can't re-open silently. Tests at the
/// <see cref="PipelineAssemblyBuilder"/> level rather than through
/// the full QuickLogBuilder so the surface stays small — the
/// auto-tracking code path is the load-bearing piece, and
/// constructing a custom-sink builder configuration would add a lot
/// of unrelated machinery without changing what's being tested.
/// </para>
/// </summary>
public sealed class SyncDisposableSinkLifecycleTests
{
    [Fact]
    public void Builder_tracks_sync_IDisposable_resource_on_SyncResources()
    {
        var sink = new CountingDisposableSink();
        var builder = new PipelineAssemblyBuilder(sink);
        builder.TrackSyncResource(sink);

        var composition = BuildSimpleComposition(builder);

        composition.SyncResources.Should().NotBeNull();
        composition.SyncResources!.Should().ContainSingle()
            .Which.Should().BeSameAs(sink);
    }

    [Fact]
    public void Builder_tracks_multiple_sync_disposables_in_registration_order()
    {
        var first  = new CountingDisposableSink();
        var second = new CountingDisposableSink();
        var third  = new CountingDisposableSink();

        var builder = new PipelineAssemblyBuilder(first);
        builder.TrackSyncResource(first);
        builder.TrackSyncResource(second);
        builder.TrackSyncResource(third);

        var composition = BuildSimpleComposition(builder);

        composition.SyncResources.Should().NotBeNull();
        composition.SyncResources!.Should().HaveCount(3);
        composition.SyncResources![0].Should().BeSameAs(first);
        composition.SyncResources![1].Should().BeSameAs(second);
        composition.SyncResources![2].Should().BeSameAs(third);
    }

    [Fact]
    public void Builder_skips_null_sync_resources()
    {
        var sink = new CountingDisposableSink();
        var builder = new PipelineAssemblyBuilder(sink);
        builder.TrackSyncResource(null);
        builder.TrackSyncResource(sink);
        builder.TrackSyncResource(null);

        var composition = BuildSimpleComposition(builder);
        composition.SyncResources.Should().NotBeNull();
        composition.SyncResources!.Should().ContainSingle()
            .Which.Should().BeSameAs(sink);
    }

    [Fact]
    public void Builder_with_no_sync_resources_yields_null_SyncResources()
    {
        var sink = new CountingDisposableSink();
        var builder = new PipelineAssemblyBuilder(sink);
        // No TrackSyncResource calls.

        var composition = BuildSimpleComposition(builder);
        composition.SyncResources.Should().BeNull(
            "the SyncResources collection must be null (not an empty list) when no sync resources were tracked");
    }

    // ---------- helpers ----------

    private static LoggerComposition BuildSimpleComposition(PipelineAssemblyBuilder builder)
    {
        var scopeProvider = new MMP.Herald.Pipeline.AsyncLocalLogScopeProvider();
        var eventFactory = new MMP.Herald.Events.LogEventFactory(
            new MMP.Herald.Time.SystemDateTimeProvider(),
            new MMP.Herald.Templating.MessageTemplateParser(),
            scopeProvider,
            new MMP.Herald.Enrichers.NullLogEnricher());

        var levelRegistry = MMP.Herald.Levels.LogLevelRegistry.CreateDefault();

        return builder.Build(
            eventFactory: eventFactory,
            scopeProvider: scopeProvider,
            includeCallerInfo: false,
            levelRegistry: levelRegistry,
            minimumLevel: MMP.Herald.Levels.KnownLogLevels.Trace);
    }

    private sealed class CountingDisposableSink : ILogger, IDisposable
    {
        private int _disposeCallCount;
        public int DisposeCallCount => Volatile.Read(ref _disposeCallCount);
        public bool ThrowOnDispose { get; set; }

        public void Log(LogEvent logEvent) { }

        public ValueTask LogAsync(LogEvent logEvent, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public void Dispose()
        {
            Interlocked.Increment(ref _disposeCallCount);
            if (ThrowOnDispose)
                throw new InvalidOperationException("test sink throws on dispose");
        }
    }
}

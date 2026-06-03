#nullable enable

// AddHerald shutdown-flush regression suite (the MEL flush-on-shutdown bug).
//
// This is a CLASS of bug: a provider that buffers events but never flushes on
// host shutdown silently drops whatever is still queued. The fix is the
// AddHerald(QuickLogResult) extension that registers a lifetime hosted service
// flushing the pipeline on ApplicationStopped. These tests pin:
//
//   1. HeraldLoggerProvider_HostShutdown_FlushesPipeline — an async-buffered
//      pipeline wired via AddHerald drains on ApplicationStopped WITHOUT a
//      manual DisposeAsync. Fails pre-fix (no flush hook), passes post-fix.
//   2. AddHerald_RegistersApplicationStoppedFlush — structural guard: the
//      lifetime hosted service is registered.
//   3. DoubleFlush_IsIdempotent — a manual DisposeAsync plus the hook does not
//      throw or corrupt.
//   4. ConsoleSink_HostShutdown_FlushesStdout — the shutdown path flushes
//      Console.Out so the last buffered line is present (Layer-B fix).

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MMP.Herald.Events;
using MMP.Herald.Pipeline;
using MMP.Herald.Quick;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Herald.OSS.Serilog.AspNetCore.Tests;

public sealed class AddHeraldTests
{
    // ------------------------------------------------------------------------
    // A sink that records every event it receives. Used as the pipeline bridge
    // target so we can assert what survived the shutdown drain.
    // ------------------------------------------------------------------------
    private sealed class RecordingSink : MMP.Herald.ILogger
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<LogEvent> _events = new();
        // LogAsync has a default interface implementation that delegates to Log,
        // so the synchronous Log override is enough for a capturing test sink.
        public void Log(LogEvent logEvent) => _events.Enqueue(logEvent);
        public int Count => _events.Count;
        public System.Collections.Generic.IReadOnlyList<LogEvent> Events => _events.ToList();
    }

    private static (QuickLogResult Result, RecordingSink Sink) BuildAsyncBuffered()
    {
        var sink = new RecordingSink();
        var result = QuickLogBuilder.Create()
            .WithBridge(sink)
            .WithAsyncLogging(capacity: 1024)
            .WithMinimumLevel("verbose")
            .BuildAndCommit();
        return (result, sink);
    }

    // ------------------------------------------------------------------------
    // 1. Host shutdown flushes the pipeline via AddHerald (the core regression).
    // ------------------------------------------------------------------------
    [Fact]
    public async Task HeraldLoggerProvider_HostShutdown_FlushesPipeline()
    {
        var (result, sink) = BuildAsyncBuffered();

        using var host = Host.CreateDefaultBuilder()
            .ConfigureLogging(b => b.ClearProviders().AddHerald(result))
            .Build();

        await host.StartAsync();

        host.Services.GetRequiredService<ILogger<AddHeraldTests>>()
            .LogInformation("buffered until shutdown {Id}", 7);

        // Stop the host WITHOUT a manual DisposeAsync. ApplicationStopped fires
        // the AddHerald flush hook, which drains the async pipeline. Pre-fix the
        // event is still in the async queue and is lost; post-fix it is drained.
        await host.StopAsync();

        // Assert OUR event survived. Host.CreateDefaultBuilder also emits its own
        // framework lifetime logs through the provider, so an exact total count is
        // brittle; the regression is specifically that the buffered application
        // event reaches the sink after shutdown.
        sink.Events.Should().Contain(
            e => e.MessageTemplate == "buffered until shutdown {Id}",
            "ApplicationStopped must drain the async pipeline so the buffered event survives shutdown");
    }

    // ------------------------------------------------------------------------
    // 2. Structural guard: the lifetime hosted service is registered.
    // ------------------------------------------------------------------------
    [Fact]
    public void AddHerald_RegistersApplicationStoppedFlush()
    {
        var (result, _) = BuildAsyncBuffered();

        var services = new ServiceCollection();
        services.AddLogging(b => b.ClearProviders().AddHerald(result));

        // Exactly one IHostedService whose implementation is the Herald flush
        // service must be present.
        var hostedServiceCount = services.Count(d =>
            d.ServiceType == typeof(IHostedService));

        hostedServiceCount.Should().Be(1,
            "AddHerald(QuickLogResult) must register the ApplicationStopped flush service");
    }

    // ------------------------------------------------------------------------
    // 3. Manual DisposeAsync + the hook is a safe double-flush.
    // ------------------------------------------------------------------------
    [Fact]
    public async Task DoubleFlush_IsIdempotent()
    {
        var (result, sink) = BuildAsyncBuffered();

        using var host = Host.CreateDefaultBuilder()
            .ConfigureLogging(b => b.ClearProviders().AddHerald(result))
            .Build();

        await host.StartAsync();

        host.Services.GetRequiredService<ILogger<AddHeraldTests>>()
            .LogWarning("double flush {Id}", 1);

        // The host stops first, firing the AddHerald hook (drain #1). Then the
        // consumer calls DisposeAsync on its own belt-and-suspenders shutdown
        // handler (drain #2). No log is emitted between the two drains — logging a
        // disposed pipeline is a separate abuse case, not what idempotency covers.
        // The Interlocked guard plus AsyncLogger.DisposeAsync's own _isDisposed
        // early-return make the second drain a safe no-op.
        await host.StopAsync();

        Func<Task> secondFlush = async () => await result.DisposeAsync();

        await secondFlush.Should().NotThrowAsync(
            "the ApplicationStopped hook plus a manual DisposeAsync must be a safe double-flush");

        // The warning drained exactly once across both flush paths.
        sink.Events.Should().ContainSingle(
            e => e.MessageTemplate == "double flush {Id}",
            "the event must be drained exactly once across both flush paths");
    }

    // ------------------------------------------------------------------------
    // 4. Console sink: shutdown flushes stdout so the last line is present.
    // ------------------------------------------------------------------------
    [Fact]
    public async Task ConsoleSink_HostShutdown_FlushesStdout()
    {
        var originalOut = Console.Out;
        var captured = new StringWriter();
        Console.SetOut(captured);
        try
        {
            var result = QuickLogBuilder.Create()
                .WithConsoleSink()
                .WithAsyncLogging(capacity: 1024)
                .WithMinimumLevel("verbose")
                .BuildAndCommit();

            using var host = Host.CreateDefaultBuilder()
                .ConfigureLogging(b => b.ClearProviders().AddHerald(result))
                .Build();

            await host.StartAsync();

            host.Services.GetRequiredService<ILogger<AddHeraldTests>>()
                .LogInformation("LAST-LINE-MARKER {Id}", 99);

            await host.StopAsync();

            // The shutdown path drains the pipeline AND flushes Console.Out, so
            // the rendered line is present in the redirected writer.
            captured.ToString().Should().Contain("LAST-LINE-MARKER",
                "the shutdown path must flush both the pipeline and stdout");
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }
}

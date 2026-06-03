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
    // 2b. REGRESSION (HIGH — double-drain): two AddHerald(result) calls must
    //     register exactly ONE HeraldLifetimeService. Pre-fix the shutdown-flush
    //     used AddSingleton<IHostedService>(factory), so a second AddHerald(result)
    //     registered a SECOND HeraldLifetimeService — each carries its own
    //     per-instance _flushed latch, so neither can dedupe the other and the
    //     pipeline drains twice on shutdown. Fails pre-fix (two services), passes
    //     post-fix (sentinel-marker guard registers the hosted service once).
    // ------------------------------------------------------------------------
    [Fact]
    public void AddHerald_CalledTwice_RegistersExactlyOneLifetimeService()
    {
        var (result, _) = BuildAsyncBuffered();

        var services = new ServiceCollection();
        // Two AddHerald(result) calls — the double-registration the fix dedupes.
        services.AddLogging(b => b.ClearProviders().AddHerald(result).AddHerald(result));

        // Count IHostedService descriptors whose implementation is the Herald
        // lifetime flush service specifically (other hosted services may exist in
        // a real host; here only Herald's should be present, and exactly once).
        var heraldHostedServiceCount = services.Count(d =>
            d.ServiceType == typeof(IHostedService)
            && IsHeraldLifetimeService(d));

        heraldHostedServiceCount.Should().Be(1,
            "two AddHerald(result) calls must register exactly one HeraldLifetimeService — " +
            "a duplicate would drain the pipeline twice on shutdown (each has its own latch)");
    }

    // The HeraldLifetimeService type is internal; identify its descriptor by the
    // factory's declaring type rather than a public type reference. The shutdown
    // flush is registered via a factory whose target lives in the AspNetCore
    // extension assembly, so any IHostedService descriptor carrying an
    // ImplementationFactory from that assembly is Herald's.
    private static bool IsHeraldLifetimeService(ServiceDescriptor descriptor)
    {
        // Post-fix the descriptor is a factory descriptor. Pre-fix it is also a
        // factory descriptor (AddSingleton<IHostedService>(sp => new ...)). Either
        // way the factory's method is declared in the Herald AspNetCore extension
        // assembly — the same assembly that owns AddHerald.
        var factory = descriptor.ImplementationFactory;
        if (factory is null) return false;
        var declaring = factory.Method.DeclaringType;
        return declaring is not null
            && declaring.Assembly == typeof(HeraldLoggingBuilderExtensions).Assembly;
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
    // A minimal IHostApplicationLifetime whose ApplicationStopped token can be
    // fired on demand. Firing invokes the registered ApplicationStopped callbacks
    // synchronously — which is how the real host runs the AddHerald flush hook on
    // the shutdown thread.
    // ------------------------------------------------------------------------
    private sealed class FakeLifetime : IHostApplicationLifetime, IDisposable
    {
        private readonly CancellationTokenSource _started = new();
        private readonly CancellationTokenSource _stopping = new();
        private readonly CancellationTokenSource _stopped = new();

        public CancellationToken ApplicationStarted => _started.Token;
        public CancellationToken ApplicationStopping => _stopping.Token;
        public CancellationToken ApplicationStopped => _stopped.Token;

        public void StopApplication() { }

        // Fire ApplicationStopped — runs the registered Flush callback inline.
        public void FireStopped() => _stopped.Cancel();

        public void Dispose()
        {
            _started.Dispose();
            _stopping.Dispose();
            _stopped.Dispose();
        }
    }

    // A TextWriter whose Flush throws — stands in for a stdout that faults on the
    // shutdown thread. The fix's try/catch around Console.Out.Flush() must swallow
    // this; pre-fix it propagates out of the ApplicationStopped callback.
    private sealed class FaultingWriter : TextWriter
    {
        public override System.Text.Encoding Encoding => System.Text.Encoding.UTF8;
        public override void Flush() =>
            throw new InvalidOperationException("stdout flush faulted");
    }

    // ------------------------------------------------------------------------
    // 3b. REGRESSION (MEDIUM — Flush fault-safety): a FAULTING flush on the
    //     ApplicationStopped path must NOT escape the shutdown callback and crash
    //     the process. The Flush method drains the pipeline, then flushes
    //     Console.Out. Pre-fix Console.Out.Flush() was unguarded, so a faulting
    //     stdout threw out of the ApplicationStopped callback (the shutdown
    //     thread, which has no logging surface left). Post-fix both the drain and
    //     the stdout flush are wrapped in try/catch. Driven through the internal
    //     HeraldLifetimeService + a fake lifetime so the fault surfaces exactly on
    //     the Flush path. Fails pre-fix (throws), passes post-fix (swallowed).
    // ------------------------------------------------------------------------
    [Fact]
    public async Task Flush_FaultingStdout_DoesNotEscapeShutdownCallback()
    {
        var (result, _) = BuildAsyncBuffered();
        using var lifetime = new FakeLifetime();

        // Wire the lifetime service exactly as AddHerald does: it registers Flush
        // on ApplicationStopped during StartAsync.
        var service = new HeraldLifetimeService(lifetime, result);
        await service.StartAsync(CancellationToken.None);

        var originalOut = Console.Out;
        Console.SetOut(new FaultingWriter());
        try
        {
            // Firing ApplicationStopped runs Flush inline. Pre-fix the faulting
            // Console.Out.Flush() throws here and escapes; post-fix it is swallowed.
            var fire = () => lifetime.FireStopped();
            fire.Should().NotThrow(
                "a faulting flush on the shutdown path must be swallowed, not crash the process");
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    // ------------------------------------------------------------------------
    // NET-NEW (missing test): cross-INSTANCE Flush idempotency. The per-instance
    // _flushed latch dedupes within ONE HeraldLifetimeService. Two DISTINCT
    // services over the SAME result (the shape the double-drain fix prevents from
    // ever being registered, but still a valid construction) must each be able to
    // flush without the second drain throwing — QuickLogResult.DisposeAsync owns
    // its own _isDisposed early-return, so a second drain of the same result is a
    // safe no-op even across instances. Pins that contract directly.
    // ------------------------------------------------------------------------
    [Fact]
    public async Task Flush_TwoServiceInstances_SameResult_SecondDrainIsSafe()
    {
        var (result, sink) = BuildAsyncBuffered();
        using var lifetimeA = new FakeLifetime();
        using var lifetimeB = new FakeLifetime();

        var serviceA = new HeraldLifetimeService(lifetimeA, result);
        var serviceB = new HeraldLifetimeService(lifetimeB, result);
        await serviceA.StartAsync(CancellationToken.None);
        await serviceB.StartAsync(CancellationToken.None);

        result.Logger.Information(MMP.Herald.Events.LogCategory.None, "cross-instance {Id}", 1);

        // First instance drains. Second instance drains the same (now-disposed)
        // result — must be a safe no-op, not a throw.
        var fireA = () => lifetimeA.FireStopped();
        var fireB = () => lifetimeB.FireStopped();

        fireA.Should().NotThrow("the first instance drains the result cleanly");
        fireB.Should().NotThrow(
            "a second service instance draining the already-disposed result must be a safe no-op");

        sink.Events.Should().ContainSingle(
            e => e.MessageTemplate == "cross-instance {Id}",
            "the event drains exactly once even across two service instances over one result");
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

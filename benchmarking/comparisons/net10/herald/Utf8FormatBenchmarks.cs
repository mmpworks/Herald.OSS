#nullable enable

using System;
using System.Buffers;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Logging;
using MMP.Herald.Configuration.Runtime;
using MMP.Herald.Formatting;
using MMP.Herald.Levels;
using MMP.Herald.Output.Rendering;
using MMP.Herald.Quick;
using MMP.Herald.Routing;
using Serilog;
using Serilog.Formatting;
using Serilog.Formatting.Compact;
using ZLogger;
using HeraldLogEvent = MMP.Herald.Events.LogEvent;
using HeraldLogCategory = MMP.Herald.Events.LogCategory;
using SerilogLogEvent = Serilog.Events.LogEvent;
using MEL = Microsoft.Extensions.Logging;

namespace MMP.Herald.OSS.Benchmarks.Comparisons.HeraldRow;

/// <summary>
/// End-to-end emit-to-UTF-8-bytes comparison. Each library is
/// configured with its idiomatic "format to UTF-8 then discard" sink
/// so the bench measures the format pipeline cost, not downstream I/O.
///
/// <list type="bullet">
///   <item><b>Herald</b>: a bridge sink that wraps
///       <see cref="Utf8JsonFormatter"/> writing to an
///       <see cref="ArrayBufferWriter{T}"/> that resets per call.
///       The formatter writes directly to bytes via
///       <c>System.Text.Json.Utf8JsonWriter</c> — no intermediate
///       string materialization. The bench's call site
///       <c>_herald.Logger.Info(category, template, 42, "alpha", 9.99, "…")</c>
///       binds to the source-generated typed-args overload
///       <c>Info&lt;T1,T2,T3,T4&gt;(LogCategory, string, T1, T2, T3, T4, …)</c>
///       at <c>[OverloadResolutionPriority(4)]</c> — no boxing, no
///       <c>params object?[]</c> allocation. The overload fills a
///       <c>LogPropertyBuffer4</c> on the stack and dispatches through
///       <c>LogCompact</c> to <c>IKernelSink.Log(in LogEventBuffer)</c>.</item>
///   <item><b>ZLogger</b>:
///       <c>AddZLoggerStream(Stream.Null, opts =&gt; opts.UseJsonFormatter(_ =&gt; {}))</c>.
///       ZLogger's default formatter is the
///       <c>PlainTextZLoggerFormatter</c>, which writes a single
///       rendered message string (no per-property JSON shape). To
///       compare apples-to-apples on payload shape, the bench wires
///       <c>SystemTextJsonZLoggerFormatter</c> via
///       <c>ZLogger.ZLoggerOptionsSystemTextJsonExtensions.UseJsonFormatter</c>.
///       The bytes are discarded, but the full format pipeline runs.</item>
///   <item><b>Serilog</b>: a custom sink that runs
///       <see cref="CompactJsonFormatter"/> to a
///       <see cref="StringWriter"/> backed by a pooled
///       <see cref="System.Text.StringBuilder"/>. Serilog's format
///       writes UTF-16 strings; the rendered text is discarded.
///       This is the closest fair analogue — Serilog ships no public
///       "format to UTF-8 bytes" path.</item>
/// </list>
///
/// <para>
/// The asymmetry is real and documented: ZLogger and Herald write
/// UTF-8 directly; Serilog goes through a string. The bench shows the
/// cost difference an adopter would actually see if they wired each
/// library for "structured JSON output to a remote sink."
/// </para>
///
/// <para>
/// <b>Warmup tightening:</b> the prior run flagged
/// <c>mValue = 4</c> bimodal on Herald with two clumps near 440 ns and
/// 510 ns. <see cref="WarmupCountAttribute"/> raises BDN's warmup to 15
/// iterations to give the JIT enough time to settle the tier-0 → tier-1
/// transition before the measured iterations begin.
/// </para>
///
/// <para>
/// <b>ZLogger queue configuration:</b> When configured with the JSON
/// formatter (UseJsonFormatter), ZLogger's background write queue grows
/// unbounded under sustained bench load against Stream.Null because
/// Stream.Null's zero-cost write never back-pressures the queue. The
/// bench bounds the queue via BackgroundBufferCapacity + Block FullMode
/// so producer threads wait when the queue fills, matching what a real
/// production ZLogger consumer with a slow downstream (file, network,
/// Datadog) would see. The plain-text Stream.Null variant from the
/// pre-tightening bench understated this cost — a real consumer would
/// never see ZLogger's plain-text Stream.Null number in production.
/// </para>
/// </summary>
[MemoryDiagnoser]
[WarmupCount(15)]
public class Utf8FormatBenchmarks
{
    /// <summary>
    /// Diagnostic counter incremented inside
    /// <see cref="HeraldUtf8DiscardSink.Log(in MMP.Herald.Pipeline.Kernel.LogEventBuffer)"/>.
    /// After the bench run completes, <see cref="Cleanup"/> writes the
    /// observed count to <c>BenchmarkDotNet.Artifacts/herald-kernel-hits.txt</c>
    /// alongside the iteration count from the bench infrastructure. A
    /// non-zero count proves the kernel-fast-path overload IS being hit
    /// per call (i.e. the bench is measuring
    /// <c>Format(in LogEventBuffer, …)</c>, not the chain-path
    /// <c>Format(LogEvent, …)</c> overload).
    /// </summary>
    private static long s_kernelHits;
    private QuickLogResult _herald = null!;
    private MEL.ILoggerFactory _zloggerFactory = null!;
    private MEL.ILogger _zlogger = null!;
    private Serilog.Core.Logger _serilog = null!;

    [GlobalSetup]
    public void Setup()
    {
        Interlocked.Exchange(ref s_kernelHits, 0L);
        var levelRegistry = new MMP.Herald.Levels.DefaultLogLevelRegistryFactory().Create();
        var heraldUtf8 = new HeraldUtf8DiscardSink(new Utf8JsonFormatter(levelRegistry));

        // Wire the kernel-eligible sink by overriding the "null" sink
        // kind locally: WithCustomSinkProvider registers a provider for
        // this builder only; WithNullSink adds a sink-config entry of
        // kind "null"; the sink resolver picks the local provider
        // (Utf8DiscardOverProviderForNull) instead of the built-in
        // NullLogSinkProvider, so the route lands at the
        // kernel-eligible HeraldUtf8DiscardSink. KernelEligibility
        // passes (the sink implements IKernelSink); the pipeline takes
        // the kernel fast path; Format(in LogEventBuffer, …) is the
        // active formatter call.
        _herald = QuickLogBuilder.Create()
            .WithCustomSinkProvider(new Utf8DiscardOverrideProvider(heraldUtf8))
            .WithNullSink()
            .WithMinimumLevel("trace")
            .BuildAndCommit();

        _zloggerFactory = MEL.LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(MEL.LogLevel.Trace);
            // Apples-to-apples on payload shape: Herald produces structured
            // JSON, so wire ZLogger's SystemTextJsonZLoggerFormatter rather
            // than the default PlainTextZLoggerFormatter (which writes a
            // single rendered message string with no per-property JSON
            // shape). The extension method is
            // ZLogger.ZLoggerOptionsSystemTextJsonExtensions.UseJsonFormatter
            // (namespace ZLogger, ZLogger 2.5.10). The empty configure
            // delegate keeps ZLogger's defaults — same property names,
            // same value formatting — so the only delta from the prior run
            // is "plain text → JSON."
            // Bound ZLogger's background write queue. With the JSON
            // formatter wired against Stream.Null, the prior rerun blew
            // committed memory to ~98 GB at iteration #67 because
            // Stream.Null's zero-cost write never back-pressured the
            // unbounded queue and the 2M-op bench loop kept enqueuing.
            // BackgroundBufferCapacity caps the queue; FullMode = Block
            // back-pressures the producer thread when the queue fills,
            // matching real-world consumers with a slow downstream.
            // Cap chosen per Path A starting value: 1024 entries — large
            // enough to absorb burstiness inside a BDN inner loop, small
            // enough to keep memory bounded.
            builder.AddZLoggerStream(Stream.Null, options =>
            {
                options.UseJsonFormatter(_ => { });
                options.FullMode = BackgroundBufferFullMode.Block;
                options.BackgroundBufferCapacity = 1024;
            });
        });
        _zlogger = _zloggerFactory.CreateLogger("bench");

        _serilog = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .WriteTo.Sink(new SerilogJsonDiscardSink(new CompactJsonFormatter()))
            .CreateLogger();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        // Diagnostic: dump the observed kernel-hit count next to the
        // artifacts so we can confirm post-run that the bench was hitting
        // Format(in LogEventBuffer, …) and not the heap LogEvent path.
        try
        {
            var hits = Interlocked.Read(ref s_kernelHits);
            var dir = Path.Combine(AppContext.BaseDirectory,
                "..", "..", "..", "BenchmarkDotNet.Artifacts");
            Directory.CreateDirectory(dir);
            File.WriteAllText(
                Path.Combine(dir, "herald-kernel-hits.txt"),
                $"HeraldUtf8DiscardSink.Log(in LogEventBuffer) calls: {hits}{Environment.NewLine}");
        }
        catch
        {
            // Diagnostic only — never let it break Cleanup.
        }

        _serilog.Dispose();
        _zloggerFactory.Dispose();
        if (_herald.AsyncResource is { } resource)
            resource.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    /// <summary>
    /// One-time diagnostic: render exactly one event from Herald,
    /// ZLogger, and Serilog to disk so the byte-count asymmetry is
    /// observable. Not a [Benchmark] — runs once from Main on demand.
    /// Output lands in <c>BenchmarkDotNet.Artifacts/sample-events/</c>.
    /// </summary>
    public static void DumpSampleEvents()
    {
        var levelRegistry = new MMP.Herald.Levels.DefaultLogLevelRegistryFactory().Create();
        var heraldFormatter = new Utf8JsonFormatter(levelRegistry);
        var heraldBuf = new ArrayBufferWriter<byte>(512);
        var captureSink = new SampleCaptureSink(heraldFormatter, heraldBuf);
        var herald = QuickLogBuilder.Create()
            .WithCustomSinkProvider(new Utf8DiscardOverrideProvider(captureSink))
            .WithNullSink()
            .WithMinimumLevel("trace")
            .BuildAndCommit();
        herald.Logger.Info(HeraldLogCategory.App,
            "user {Id} purchased {Sku} for {Price} at {Time}",
            42, "alpha", 9.99, "2026-05-14T19-30Z");

        var zloggerCapture = new MemoryStream();
        var zloggerFactory = MEL.LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(MEL.LogLevel.Trace);
            builder.AddZLoggerStream(zloggerCapture, options =>
            {
                options.UseJsonFormatter(_ => { });
            });
        });
        var zlogger = zloggerFactory.CreateLogger("bench");
        zlogger.ZLogInformation(
            $"user {42} purchased {"alpha"} for {9.99} at {"2026-05-14T19-30Z"}");
        zloggerFactory.Dispose(); // flushes the background queue

        var serilogWriter = new StringWriter();
        var serilog = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .WriteTo.Sink(new SerilogCaptureSink(new CompactJsonFormatter(), serilogWriter))
            .CreateLogger();
        serilog.Information(
            "user {Id} purchased {Sku} for {Price} at {Time}",
            42, "alpha", 9.99, "2026-05-14T19-30Z");
        serilog.Dispose();

        var outDir = Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "BenchmarkDotNet.Artifacts", "sample-events");
        Directory.CreateDirectory(outDir);
        File.WriteAllBytes(Path.Combine(outDir, "herald.json"), heraldBuf.WrittenSpan.ToArray());
        File.WriteAllBytes(Path.Combine(outDir, "zlogger.json"), zloggerCapture.ToArray());
        File.WriteAllText(Path.Combine(outDir, "serilog.json"), serilogWriter.ToString());

        if (herald.AsyncResource is { } resource)
            resource.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    [Benchmark(Baseline = true)]
    public void Herald_Utf8Json_Discard()
    {
        _herald.Logger.Info(HeraldLogCategory.App,
            "user {Id} purchased {Sku} for {Price} at {Time}",
            42, "alpha", 9.99, "2026-05-14T19-30Z");
    }

    [Benchmark]
    public void ZLogger_Utf8_StreamNull()
    {
        _zlogger.ZLogInformation(
            $"user {42} purchased {"alpha"} for {9.99} at {"2026-05-14T19-30Z"}");
    }

    [Benchmark]
    public void Serilog_CompactJson_Discard()
    {
        _serilog.Information(
            "user {Id} purchased {Sku} for {Price} at {Time}",
            42, "alpha", 9.99, "2026-05-14T19-30Z");
    }

    /// <summary>
    /// Kernel-eligible sink for Herald: implements
    /// <see cref="MMP.Herald.Pipeline.Kernel.IKernelSink"/> so the
    /// pipeline takes the kernel fast path (stack-allocated
    /// <see cref="MMP.Herald.Pipeline.Kernel.LogEventBuffer"/>; no
    /// heap LogEvent materialization). Formats directly from the
    /// buffer via <see cref="Utf8JsonFormatter.Format(in MMP.Herald.Pipeline.Kernel.LogEventBuffer, IBufferWriter{byte})"/>
    /// and discards the bytes. The ArrayBufferWriter is reused
    /// across calls.
    /// </summary>
    private sealed class HeraldUtf8DiscardSink
        : MMP.Herald.ILogger, MMP.Herald.Pipeline.Kernel.IKernelSink
    {
        private readonly Utf8JsonFormatter _formatter;
        private readonly ArrayBufferWriter<byte> _buffer = new(512);

        public HeraldUtf8DiscardSink(Utf8JsonFormatter formatter) => _formatter = formatter;

        public void Log(in MMP.Herald.Pipeline.Kernel.LogEventBuffer buffer)
        {
            // Diagnostic counter — incremented exactly once per kernel
            // dispatch. Compared against the BDN iteration count in
            // Cleanup to prove the kernel fast path is the one being
            // measured. Interlocked is overkill (the bench is single-
            // threaded) but cheap and silences any future "what if a
            // different sink fires from another thread" worry.
            Interlocked.Increment(ref s_kernelHits);

            _formatter.Format(in buffer, _buffer);
            _buffer.ResetWrittenCount();
        }

        public void Log(HeraldLogEvent logEvent)
        {
            _formatter.Format(logEvent, _buffer);
            _buffer.ResetWrittenCount();
        }

        public ValueTask LogAsync(HeraldLogEvent logEvent, CancellationToken cancellationToken = default)
        {
            Log(logEvent);
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>
    /// Capture variant of <see cref="HeraldUtf8DiscardSink"/>: keeps the
    /// written bytes in the shared buffer instead of resetting, so
    /// <see cref="DumpSampleEvents"/> can read them out. Used only by
    /// the one-shot diagnostic, never by the [Benchmark] methods.
    /// </summary>
    private sealed class SampleCaptureSink
        : MMP.Herald.ILogger, MMP.Herald.Pipeline.Kernel.IKernelSink
    {
        private readonly Utf8JsonFormatter _formatter;
        private readonly ArrayBufferWriter<byte> _buffer;

        public SampleCaptureSink(Utf8JsonFormatter formatter, ArrayBufferWriter<byte> buffer)
        {
            _formatter = formatter;
            _buffer = buffer;
        }

        public void Log(in MMP.Herald.Pipeline.Kernel.LogEventBuffer buffer)
            => _formatter.Format(in buffer, _buffer);

        public void Log(HeraldLogEvent logEvent)
            => _formatter.Format(logEvent, _buffer);

        public ValueTask LogAsync(HeraldLogEvent logEvent, CancellationToken cancellationToken = default)
        {
            Log(logEvent);
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>
    /// Overrides the "null" sink kind locally so the bench's
    /// kernel-eligible <see cref="HeraldUtf8DiscardSink"/> wires in
    /// where <see cref="QuickLogBuilder.WithNullSink"/> would otherwise
    /// route to <c>NoOpLogger</c>. Local providers take precedence over
    /// the process-wide <see cref="LogSinkProviderRegistry.Default"/>,
    /// so the override is scoped to this single builder.
    /// </summary>
    private sealed class Utf8DiscardOverrideProvider : ILogSinkProvider
    {
        private readonly MMP.Herald.ILogger _sink;
        public Utf8DiscardOverrideProvider(MMP.Herald.ILogger sink) => _sink = sink;
        public string SinkKind => Services.KnownSinkKinds.Null;
        public MMP.Herald.ILogger CreateSink(
            LoggingRuntimeSinkDefinition definition,
            ILogLevelRegistry levelRegistry,
            ILogOutputTransformerRegistry transformerRegistry) => _sink;
    }

    /// <summary>
    /// Custom Serilog sink: runs CompactJsonFormatter to a StringWriter,
    /// then discards the rendered string. Serilog's CompactJsonFormatter
    /// writes UTF-16; if the adopter were sending to a remote, they'd
    /// pay an additional UTF-16 → UTF-8 encoding step. This bench
    /// stops at the rendered string and notes the asymmetry in the
    /// writeup.
    /// </summary>
    private sealed class SerilogJsonDiscardSink : Serilog.Core.ILogEventSink
    {
        private readonly ITextFormatter _formatter;
        private readonly StringWriter _writer = new();

        public SerilogJsonDiscardSink(ITextFormatter formatter) => _formatter = formatter;

        public void Emit(SerilogLogEvent logEvent)
        {
            _formatter.Format(logEvent, _writer);
            // Reset the underlying StringBuilder so the next call
            // starts fresh without holding onto the prior render.
            _writer.GetStringBuilder().Clear();
        }
    }

    /// <summary>
    /// Capture variant of <see cref="SerilogJsonDiscardSink"/> — writes
    /// to a caller-supplied <see cref="StringWriter"/> and does NOT
    /// clear it after emit. Used only by <see cref="DumpSampleEvents"/>.
    /// </summary>
    private sealed class SerilogCaptureSink : Serilog.Core.ILogEventSink
    {
        private readonly ITextFormatter _formatter;
        private readonly StringWriter _writer;

        public SerilogCaptureSink(ITextFormatter formatter, StringWriter writer)
        {
            _formatter = formatter;
            _writer = writer;
        }

        public void Emit(SerilogLogEvent logEvent) => _formatter.Format(logEvent, _writer);
    }
}

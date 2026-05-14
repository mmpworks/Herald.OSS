#nullable enable

using System;
using System.Buffers;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Logging;
using MMP.Herald.Formatting;
using MMP.Herald.Quick;
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
///       string materialization.</item>
///   <item><b>ZLogger</b>:
///       <c>AddZLoggerStream(Stream.Null)</c>. ZLogger's headline
///       claim is "UTF-8 from input to output." The bytes are
///       discarded, but the full format pipeline runs.</item>
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
/// </summary>
[MemoryDiagnoser]
public class Utf8FormatBenchmarks
{
    private QuickLogResult _herald = null!;
    private MEL.ILoggerFactory _zloggerFactory = null!;
    private MEL.ILogger _zlogger = null!;
    private Serilog.Core.Logger _serilog = null!;

    [GlobalSetup]
    public void Setup()
    {
        var levelRegistry = new MMP.Herald.Levels.DefaultLogLevelRegistryFactory().Create();
        var heraldUtf8 = new HeraldUtf8DiscardSink(new Utf8JsonFormatter(levelRegistry));

        _herald = QuickLogBuilder.Create()
            .WithBridge(heraldUtf8)
            .WithMinimumLevel("trace")
            .BuildAndCommit();

        _zloggerFactory = MEL.LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(MEL.LogLevel.Trace);
            builder.AddZLoggerStream(Stream.Null);
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
        _serilog.Dispose();
        _zloggerFactory.Dispose();
        if (_herald.AsyncResource is { } resource)
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
    /// Bridge sink for Herald: receives a LogEvent, formats it to UTF-8
    /// bytes via Utf8JsonFormatter, and discards the buffer. The
    /// ArrayBufferWriter is reused per call (Reset between events) so
    /// the bench measures the format cost, not buffer allocation churn.
    /// </summary>
    private sealed class HeraldUtf8DiscardSink : MMP.Herald.ILogger
    {
        private readonly Utf8JsonFormatter _formatter;
        private readonly ArrayBufferWriter<byte> _buffer = new(512);

        public HeraldUtf8DiscardSink(Utf8JsonFormatter formatter) => _formatter = formatter;

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
}

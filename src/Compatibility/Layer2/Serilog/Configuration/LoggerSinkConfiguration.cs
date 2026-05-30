#nullable enable

// Layer-2 mirror of Serilog.Configuration.LoggerSinkConfiguration.
// Wraps the Layer-1 MMP.Herald.Serilog.Configuration.LoggerSinkConfiguration.
// Every method is a one-line forward.
//
// Layer-1 twin: MMP.Herald.Serilog.Configuration.LoggerSinkConfiguration
//
// ILogEventSink bridge:
//   L2 Serilog.Core.ILogEventSink and L1 MMP.Herald.Serilog.Core.ILogEventSink
//   are separate interfaces (different assembly identity). L2SinkBridge wraps an
//   L2 sink so it satisfies the L1 contract required by Layer-1's WriteTo.Sink().

using System;
using System.IO;
using L1Config = MMP.Herald.Serilog.Configuration;
using L1Core   = MMP.Herald.Serilog.Core;
using L1Events = MMP.Herald.Serilog.Events;

namespace Serilog.Configuration;

/// <summary>
/// Fluent <c>WriteTo.*</c> / <c>AuditTo.*</c> configuration surface for the
/// Layer-2 <see cref="Serilog.LoggerConfiguration"/> wrapper.
/// </summary>
public sealed class LoggerSinkConfiguration
{
    private readonly Serilog.LoggerConfiguration _root;
    private readonly L1Config.LoggerSinkConfiguration _inner;

    internal LoggerSinkConfiguration(
        Serilog.LoggerConfiguration root,
        L1Config.LoggerSinkConfiguration inner)
    {
        _root  = root;
        _inner = inner;
    }

    // ── Shared level conversion ───────────────────────────────────────────────

    private static L1Events.LogEventLevel ToL1Level(Events.LogEventLevel level)
        => (L1Events.LogEventLevel)(int)level;

    // ── Named sinks ──────────────────────────────────────────────────────────

    /// <summary>Add a standard console sink.</summary>
    public Serilog.LoggerConfiguration Console(
        Events.LogEventLevel restrictedToMinimumLevel = Events.LogEventLevel.Verbose)
    {
        _inner.Console(ToL1Level(restrictedToMinimumLevel));
        return _root;
    }

#if NET9_0_OR_GREATER
    /// <summary>
    /// Add a console sink that routes each event through a user-supplied
    /// <see cref="Formatting.ITextFormatter"/>.
    /// </summary>
    public Serilog.LoggerConfiguration Console(
        Formatting.ITextFormatter formatter,
        Events.LogEventLevel restrictedToMinimumLevel = Events.LogEventLevel.Verbose)
    {
        ArgumentNullException.ThrowIfNull(formatter);
        _inner.Console(new L2TextFormatterBridge(formatter), ToL1Level(restrictedToMinimumLevel));
        return _root;
    }
#endif

    /// <summary>Add a file sink.</summary>
    public Serilog.LoggerConfiguration File(string path,
        Events.LogEventLevel restrictedToMinimumLevel = Events.LogEventLevel.Verbose)
    {
        _inner.File(path, ToL1Level(restrictedToMinimumLevel));
        return _root;
    }

    /// <summary>Add an HTTP JSON sink.</summary>
    public Serilog.LoggerConfiguration Http(string requestUri,
        Events.LogEventLevel restrictedToMinimumLevel = Events.LogEventLevel.Verbose)
    {
        _inner.Http(requestUri, ToL1Level(restrictedToMinimumLevel));
        return _root;
    }

    /// <summary>Add a TCP JSON Line sink.</summary>
    public Serilog.LoggerConfiguration TCPSink(string host, int port,
        Events.LogEventLevel restrictedToMinimumLevel = Events.LogEventLevel.Verbose)
    {
        _inner.TCPSink(host, port, ToL1Level(restrictedToMinimumLevel));
        return _root;
    }

    /// <summary>Add a UDP JSON Line sink.</summary>
    public Serilog.LoggerConfiguration UDPSink(string host, int port,
        Events.LogEventLevel restrictedToMinimumLevel = Events.LogEventLevel.Verbose)
    {
        _inner.UDPSink(host, port, ToL1Level(restrictedToMinimumLevel));
        return _root;
    }

    /// <summary>Add an Elasticsearch sink.</summary>
    public Serilog.LoggerConfiguration Elasticsearch(string clusterUrl,
        Events.LogEventLevel restrictedToMinimumLevel = Events.LogEventLevel.Verbose)
    {
        _inner.Elasticsearch(clusterUrl, ToL1Level(restrictedToMinimumLevel));
        return _root;
    }

    /// <summary>Add an OTLP JSON sink for OpenTelemetry.</summary>
    public Serilog.LoggerConfiguration OpenTelemetry(string endpoint,
        Events.LogEventLevel restrictedToMinimumLevel = Events.LogEventLevel.Verbose)
    {
        _inner.OpenTelemetry(endpoint, ToL1Level(restrictedToMinimumLevel));
        return _root;
    }

    /// <summary>Add an OTLP Protobuf sink for OpenTelemetry.</summary>
    public Serilog.LoggerConfiguration OpenTelemetryProtobuf(string endpoint,
        Events.LogEventLevel restrictedToMinimumLevel = Events.LogEventLevel.Verbose)
    {
        _inner.OpenTelemetryProtobuf(endpoint, ToL1Level(restrictedToMinimumLevel));
        return _root;
    }

    /// <summary>
    /// Route events to a user-authored <see cref="Core.ILogEventSink"/>.
    /// The L2 sink is bridged into the L1 adapter path via <c>L2SinkBridge</c>.
    /// </summary>
    public Serilog.LoggerConfiguration Sink(
        Core.ILogEventSink sink,
        Events.LogEventLevel restrictedToMinimumLevel = Events.LogEventLevel.Verbose,
        bool? auditMode = null)
    {
        ArgumentNullException.ThrowIfNull(sink);
        _inner.Sink(new L2SinkBridge(sink), ToL1Level(restrictedToMinimumLevel), auditMode);
        return _root;
    }

    /// <summary>Add a null sink that silently drops every event.</summary>
    public Serilog.LoggerConfiguration Null(
        Events.LogEventLevel restrictedToMinimumLevel = Events.LogEventLevel.Verbose)
    {
        _inner.Null(ToL1Level(restrictedToMinimumLevel));
        return _root;
    }

    // ── Inner bridge types ────────────────────────────────────────────────────

    // Adapts a Layer-2 Serilog.Core.ILogEventSink into the Layer-1
    // MMP.Herald.Serilog.Core.ILogEventSink contract so it can be registered
    // with the Layer-1 sink adapter chain.
    private sealed class L2SinkBridge : L1Core.ILogEventSink
    {
        private readonly Core.ILogEventSink _l2Sink;
        internal L2SinkBridge(Core.ILogEventSink l2Sink) => _l2Sink = l2Sink;

        // Wrap the L1 LogEvent in the L2 wrapper before handing it to the user sink.
        public void Emit(L1Events.LogEvent logEvent)
            => _l2Sink.Emit(new Events.LogEvent(logEvent));
    }

#if NET9_0_OR_GREATER
    // Adapts a Layer-2 Serilog.Formatting.ITextFormatter into the Layer-1
    // MMP.Herald.Serilog.Formatting.ITextFormatter contract.
    private sealed class L2TextFormatterBridge : MMP.Herald.Serilog.Formatting.ITextFormatter
    {
        private readonly Formatting.ITextFormatter _l2Formatter;
        internal L2TextFormatterBridge(Formatting.ITextFormatter l2Formatter)
            => _l2Formatter = l2Formatter;

        public void Format(L1Events.LogEvent logEvent, TextWriter output)
            => _l2Formatter.Format(new Events.LogEvent(logEvent), output);
    }
#endif
}

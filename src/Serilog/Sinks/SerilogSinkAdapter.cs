#nullable enable

using System;
using System.Collections.Generic;
using MMP.Herald.Configuration.Runtime;
using MMP.Herald.Failures;
using MMP.Herald.Levels;
using MMP.Herald.Output.Rendering;
using MMP.Herald.Routing;
using MMP.Herald.Serilog.Core;
using MMP.Herald.Serilog.Destructuring;

namespace MMP.Herald.Serilog.Sinks;

internal sealed class SerilogSinkAdapter : ILogSinkProvider
{
    private readonly List<ILogEventSink> _sinks = new();
    private readonly bool _auditMode;
    private readonly SerilogDestructuringApplicator? _applicator;

    internal SerilogSinkAdapter(bool auditMode = false, SerilogDestructuringApplicator? applicator = null)
    {
        _auditMode = auditMode;
        _applicator = applicator;
    }

    internal void Add(ILogEventSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        _sinks.Add(sink);
    }

    public string SinkKind => MMP.Herald.Services.KnownSinkKinds.Null;

    public MMP.Herald.ILogger CreateSink(
        LoggingRuntimeSinkDefinition definition,
        ILogLevelRegistry levelRegistry,
        ILogOutputTransformerRegistry transformerRegistry)
        => new SerilogUserLogger(_sinks, _auditMode, _applicator);
}

internal sealed class SerilogUserLogger : MMP.Herald.ILogger
{
    private readonly IReadOnlyList<ILogEventSink> _sinks;
    private readonly bool _auditMode;
    private readonly SerilogDestructuringApplicator? _applicator;

    internal SerilogUserLogger(
        IReadOnlyList<ILogEventSink> sinks,
        bool auditMode,
        SerilogDestructuringApplicator? applicator)
    {
        _sinks = sinks;
        _auditMode = auditMode;
        _applicator = applicator;
    }

    public void Log(MMP.Herald.Events.LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);

        // S5 seam: if Serilog destructuring policies are registered, pass the
        // applicator so the mirror applies them at projection time (capture-time
        // semantics -- secrets stripped by the policy never appear in Properties).
        var mirror = _applicator is not null && _applicator.HasPolicies
            ? new Events.LogEvent(logEvent, _applicator)
            : new Events.LogEvent(logEvent);

        for (var i = 0; i < _sinks.Count; i++)
        {
            if (_auditMode)
            {
                try
                {
                    _sinks[i].Emit(mirror);
                }
                catch (Exception ex)
                {
                    throw new AuditLogFailureException(
                        sinkName: "serilog_user_sink",
                        failedEvent: logEvent,
                        innerException: ex);
                }
            }
            else
            {
                try
                {
                    _sinks[i].Emit(mirror);
                }
                catch
                {
                    // Normal mode: swallow so one broken sink cannot crash the app.
                }
            }
        }
    }
}

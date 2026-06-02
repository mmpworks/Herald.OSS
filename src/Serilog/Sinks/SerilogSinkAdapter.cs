#nullable enable

using System;
using System.Collections.Generic;
using MMP.Herald.Configuration.Runtime;
using MMP.Herald.Failures;
using MMP.Herald.Levels;
using MMP.Herald.Output.Rendering;
using MMP.Herald.Routing;
using MMP.Herald.Serilog.Core;
using MMP.Herald.Serilog.Debugging;
using MMP.Herald.Serilog.Destructuring;

namespace MMP.Herald.Serilog.Sinks;

/// <summary>
/// Bridges the Herald pipeline to user-authored Serilog <see cref="ILogEventSink"/>
/// instances registered via <c>WriteTo.Sink()</c> and <c>AuditTo.Sink()</c>.
///
/// <para>
/// A single shared adapter instance is owned by each <see cref="MMP.Herald.Serilog.LoggerConfiguration"/>
/// so that <c>WriteTo.Sink()</c> and <c>AuditTo.Sink()</c> can be combined in the
/// same pipeline without conflicting for the same JSON sink slot.  Write-mode sinks are
/// dispatched with swallow semantics; audit-mode sinks throw on failure so the caller
/// can detect delivery failures on compliance-critical paths.
/// </para>
/// </summary>
internal sealed class SerilogSinkAdapter : ILogSinkProvider
{
    // Two separate lists: normal (swallow) and audit (throw). Both receive every event.
    private readonly List<ILogEventSink> _writeSinks = new();
    private readonly List<ILogEventSink> _auditSinks = new();
    private readonly SerilogDestructuringApplicator? _applicator;

    internal SerilogSinkAdapter(SerilogDestructuringApplicator? applicator = null)
    {
        _applicator = applicator;
    }

    /// <summary>Add a sink to the swallow (WriteTo) list.</summary>
    internal void AddWrite(ILogEventSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        _writeSinks.Add(sink);
    }

    /// <summary>Add a sink to the audit (AuditTo) list.</summary>
    internal void AddAudit(ILogEventSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        _auditSinks.Add(sink);
    }

    /// <summary>True when at least one sink has been registered in either list.</summary>
    internal bool HasSinks => _writeSinks.Count > 0 || _auditSinks.Count > 0;

    public string SinkKind => MMP.Herald.Services.KnownSinkKinds.Null;

    public MMP.Herald.ILogger CreateSink(
        LoggingRuntimeSinkDefinition definition,
        ILogLevelRegistry levelRegistry,
        ILogOutputTransformerRegistry transformerRegistry)
        => new SerilogUserLogger(_writeSinks, _auditSinks, _applicator);
}

internal sealed class SerilogUserLogger : MMP.Herald.ILogger
{
    private readonly IReadOnlyList<ILogEventSink> _writeSinks;
    private readonly IReadOnlyList<ILogEventSink> _auditSinks;
    private readonly SerilogDestructuringApplicator? _applicator;

    internal SerilogUserLogger(
        IReadOnlyList<ILogEventSink> writeSinks,
        IReadOnlyList<ILogEventSink> auditSinks,
        SerilogDestructuringApplicator? applicator)
    {
        _writeSinks = writeSinks;
        _auditSinks = auditSinks;
        _applicator = applicator;
    }

    public void Log(MMP.Herald.Events.LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);

        // S5 seam: if Serilog destructuring policies are registered, pass the
        // applicator so the mirror applies them at projection time (capture-time
        // semantics — secrets stripped by the policy never appear in Properties).
        var mirror = _applicator is not null && _applicator.HasPolicies
            ? new Events.LogEvent(logEvent, _applicator)
            : new Events.LogEvent(logEvent);

        // Write-mode sinks: swallow exceptions so one broken sink cannot crash the app.
        for (var i = 0; i < _writeSinks.Count; i++)
        {
            try
            {
                _writeSinks[i].Emit(mirror);
            }
            catch (Exception ex)
            {
                // G-GAP.4 / SelfLog bridge: report to any registered SelfLog writer
                // so swallowed failures are observable when self-logging is enabled.
                SelfLog.Write(
                    $"[Herald.Serilog] Exception caught from sink {_writeSinks[i].GetType().Name}: " +
                    $"{ex.GetType().Name}: {ex.Message}");
            }
        }

        // Audit-mode sinks: propagate exceptions so delivery failure surfaces to the caller.
        for (var i = 0; i < _auditSinks.Count; i++)
        {
            try
            {
                _auditSinks[i].Emit(mirror);
            }
            catch (Exception ex)
            {
                throw new AuditLogFailureException(
                    sinkName: "serilog_user_sink",
                    failedEvent: logEvent,
                    innerException: ex);
            }
        }
    }
}

#nullable enable

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
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
/// Owns the single Serilog "null-slot" and fans every pipeline event out to the
/// Serilog-side secondary destinations registered on one
/// <see cref="MMP.Herald.Serilog.LoggerConfiguration"/>:
/// <list type="bullet">
///   <item>user <see cref="ILogEventSink"/> instances (<c>WriteTo.Sink</c> / <c>AuditTo.Sink</c>),</item>
///   <item>fixed sub-loggers (<c>WriteTo.Logger(lc =&gt; ...)</c>),</item>
///   <item>dynamic per-key sub-loggers (<c>WriteTo.Map(...)</c>).</item>
/// </list>
///
/// <para>
/// One shared adapter per configuration keeps all of these on the same null-slot
/// (the JSON config has exactly one), so they never fight for the slot. The
/// created <see cref="SerilogUserLogger"/> implements <see cref="IAsyncDisposable"/>;
/// the pipeline tracks it as an async resource, so the parent CloseAndFlush drains
/// every sub-logger route exactly once.
/// </para>
/// </summary>
internal sealed class SerilogSinkAdapter : ILogSinkProvider
{
    // User sinks: normal (swallow) and audit (throw). Both receive every event.
    private readonly List<ILogEventSink> _writeSinks = new();
    private readonly List<ILogEventSink> _auditSinks = new();

    // Sub-logger routes (WriteTo.Logger + WriteTo.Map). Each owns a child pipeline.
    private readonly List<ISubLoggerRoute> _routes = new();

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

    /// <summary>Add a sub-logger route (WriteTo.Logger / WriteTo.Map).</summary>
    internal void AddRoute(ISubLoggerRoute route)
    {
        ArgumentNullException.ThrowIfNull(route);
        _routes.Add(route);
    }

    /// <summary>True when at least one secondary destination has been registered.</summary>
    internal bool HasSinks => _writeSinks.Count > 0 || _auditSinks.Count > 0 || _routes.Count > 0;

    public string SinkKind => MMP.Herald.Services.KnownSinkKinds.Null;

    public MMP.Herald.ILogger CreateSink(
        LoggingRuntimeSinkDefinition definition,
        ILogLevelRegistry levelRegistry,
        ILogOutputTransformerRegistry transformerRegistry)
        => new SerilogUserLogger(_writeSinks, _auditSinks, _routes, _applicator);
}

/// <summary>
/// The null-slot logger created by <see cref="SerilogSinkAdapter"/>. Dispatches
/// every event to the user sinks and sub-logger routes, and flushes the routes
/// (which own child pipelines) on <see cref="DisposeAsync"/>.
/// </summary>
internal sealed class SerilogUserLogger : MMP.Herald.ILogger, IAsyncDisposable
{
    private readonly IReadOnlyList<ILogEventSink> _writeSinks;
    private readonly IReadOnlyList<ILogEventSink> _auditSinks;
    private readonly IReadOnlyList<ISubLoggerRoute> _routes;
    private readonly SerilogDestructuringApplicator? _applicator;

    internal SerilogUserLogger(
        IReadOnlyList<ILogEventSink> writeSinks,
        IReadOnlyList<ILogEventSink> auditSinks,
        IReadOnlyList<ISubLoggerRoute> routes,
        SerilogDestructuringApplicator? applicator)
    {
        _writeSinks = writeSinks;
        _auditSinks = auditSinks;
        _routes = routes;
        _applicator = applicator;
    }

    public void Log(MMP.Herald.Events.LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);

        // Sub-logger routes consume the native event directly (they own full
        // child pipelines that re-run their own filters/sinks). Done first so a
        // route is unaffected by user-sink mirror projection below.
        for (var i = 0; i < _routes.Count; i++)
        {
            try
            {
                _routes[i].Accept(logEvent);
            }
            catch (Exception ex)
            {
                SelfLog.Write(
                    $"[Herald.Serilog] Exception from sub-logger route {_routes[i].GetType().Name}: " +
                    $"{ex.GetType().Name}: {ex.Message}");
            }
        }

        // User ILogEventSink instances receive the Serilog-shaped mirror event.
        // Only build the mirror when there is at least one user sink to receive it.
        if (_writeSinks.Count == 0 && _auditSinks.Count == 0) return;

        var mirror = _applicator is not null && _applicator.HasPolicies
            ? new Events.LogEvent(logEvent, _applicator)
            : new Events.LogEvent(logEvent);

        for (var i = 0; i < _writeSinks.Count; i++)
        {
            try
            {
                _writeSinks[i].Emit(mirror);
            }
            catch (Exception ex)
            {
                SelfLog.Write(
                    $"[Herald.Serilog] Exception caught from sink {_writeSinks[i].GetType().Name}: " +
                    $"{ex.GetType().Name}: {ex.Message}");
            }
        }

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

    public async ValueTask DisposeAsync()
    {
        // Flush + release every sub-logger route's child pipeline. User
        // ILogEventSink instances are caller-owned (Serilog never disposes a
        // sink it did not construct), so they are not disposed here.
        for (var i = 0; i < _routes.Count; i++)
            await _routes[i].DisposeAsync().ConfigureAwait(false);
    }
}

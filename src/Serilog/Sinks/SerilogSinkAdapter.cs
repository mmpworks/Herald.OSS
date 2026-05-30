#nullable enable

using System;
using System.Collections.Generic;
using MMP.Herald.Configuration.Runtime;
using MMP.Herald.Failures;
using MMP.Herald.Levels;
using MMP.Herald.Output.Rendering;
using MMP.Herald.Routing;
using MMP.Herald.Serilog.Core;

namespace MMP.Herald.Serilog.Sinks;

/// <summary>
/// Herald sink provider that routes native events to one or more user-authored
/// <see cref="ILogEventSink"/> implementations via the Serilog-shaped P1 mirror.
///
/// <para>
/// Registered with kind <c>"null"</c> so it overrides the built-in
/// <see cref="MMP.Herald.Routing.Providers.NullLogSinkProvider"/> for the pipeline
/// that called <c>WriteTo.Sink(userSink)</c>. A <c>"null"</c> JSON sink entry is
/// emitted by <c>QuickLogBuilder.WithNullSink()</c> — that entry is the config hook
/// that causes this provider to be instantiated at pipeline-build time.
/// </para>
///
/// <para>
/// Multiple user sinks are fanned out in registration order. The first sink to throw
/// does not prevent subsequent sinks from receiving the event unless
/// <paramref name="auditMode"/> is true, in which case the first exception propagates
/// immediately (Serilog audit-sink semantics).
/// </para>
///
/// <para>
/// <strong>Hard boundary:</strong> this provider works only for sinks compiled from
/// source against <c>MMP.Herald.Serilog</c>. Pre-compiled NuGet sinks
/// (Serilog.Sinks.Seq, Serilog.Sinks.MSSqlServer, Serilog.Sinks.Datadog, etc.)
/// reference <c>Serilog.Core.ILogEventSink</c> from the real Serilog assembly
/// (<c>PublicKeyToken=24c2f752a8e58a10</c>) and will not satisfy this interface.
/// </para>
/// </summary>
internal sealed class SerilogSinkAdapter : ILogSinkProvider
{
    // The list of user sinks grows as WriteTo.Sink() is called multiple times.
    // Access is single-threaded during configuration so no lock is needed here.
    private readonly List<ILogEventSink> _sinks = new();
    private readonly bool _auditMode;

    internal SerilogSinkAdapter(bool auditMode = false)
    {
        _auditMode = auditMode;
    }

    /// <summary>
    /// Add a user sink to the fan-out list.
    /// Called once per <c>WriteTo.Sink(userSink)</c> call on the same builder.
    /// </summary>
    internal void Add(ILogEventSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        _sinks.Add(sink);
    }

    // Overrides the "null" kind so the JSON "null" entry routes here instead
    // of the built-in NullLogSinkProvider that discards events.
    public string SinkKind => MMP.Herald.Services.KnownSinkKinds.Null;

    public MMP.Herald.ILogger CreateSink(
        LoggingRuntimeSinkDefinition definition,
        ILogLevelRegistry levelRegistry,
        ILogOutputTransformerRegistry transformerRegistry)
        => new SerilogUserLogger(_sinks, _auditMode);
}

/// <summary>
/// Minimal <see cref="MMP.Herald.ILogger"/> that fans each native
/// <see cref="MMP.Herald.Events.LogEvent"/> out to every registered
/// <see cref="ILogEventSink"/>, wrapping each event in the P1 Serilog mirror
/// (<see cref="MMP.Herald.Serilog.Events.LogEvent"/>) before dispatch.
///
/// <para>
/// Error handling mirrors Serilog's own sink-error contract:
/// <list type="bullet">
///   <item>Normal mode: exceptions from individual sinks are swallowed and
///     reported via SelfLog (Task 5 wires SelfLog; until then they are silently
///     dropped so a misbehaving sink cannot crash the application).</item>
///   <item>Audit mode: the first exception from any sink propagates immediately,
///     matching Serilog's <c>AuditSink</c> semantics where delivery guarantees
///     are required.</item>
/// </list>
/// </para>
/// </summary>
internal sealed class SerilogUserLogger : MMP.Herald.ILogger
{
    private readonly IReadOnlyList<ILogEventSink> _sinks;
    private readonly bool _auditMode;

    internal SerilogUserLogger(IReadOnlyList<ILogEventSink> sinks, bool auditMode)
    {
        _sinks = sinks;
        _auditMode = auditMode;
    }

    public void Log(MMP.Herald.Events.LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);

        // Build the mirror once; all sinks in the fan-out share the same instance.
        // The mirror is immutable after construction (lazy-projected on first property
        // read), so sharing across sinks is safe.
        var mirror = new Events.LogEvent(logEvent);

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
                    // Audit mode: wrap in AuditLogFailureException so the Herald
                    // kernel's catch-only-audit path (KernelCompiler re-throw rule)
                    // surfaces the failure to the caller.
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
                    // Normal mode: swallow and continue so one broken sink does
                    // not prevent the remaining sinks from receiving the event.
                    // TODO(Task 5): report via SelfLog once the SelfLog seam lands.
                }
            }
        }
    }
}

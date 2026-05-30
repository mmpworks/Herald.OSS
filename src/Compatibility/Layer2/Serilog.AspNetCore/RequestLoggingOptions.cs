#nullable enable
extern alias P6;

// Layer-2 mirror: Serilog.AspNetCore.RequestLoggingOptions.
//
// Real Serilog.AspNetCore exposes this in the Serilog.AspNetCore namespace.
// P6's twin (MMP.Herald.Serilog.AspNetCore.RequestLoggingOptions) is sealed,
// so inheritance is not available. This wrapper holds a P6 inner instance and
// every property getter/setter is a one-line forward.
//
// The GetLevel property requires an enum cast between Serilog.Events.LogEventLevel
// (Layer-2, this assembly) and MMP.Herald.Serilog.Events.LogEventLevel (Layer-1,
// used by P6's options). Both enums share identical ordinal values (Verbose=0..Fatal=5)
// so the int-pivot cast is the one permitted enum-cast exception on this surface
// (DRY-tripwire allowlist: enum ordinal pivot, numerically confirmed).

using System;
using Microsoft.AspNetCore.Http;
using P6Opts = P6::MMP.Herald.Serilog.AspNetCore.RequestLoggingOptions;
using L1Events = MMP.Herald.Serilog.Events;

namespace Serilog.AspNetCore;

/// <summary>
/// Controls the behaviour of the per-request HTTP summary log line emitted
/// by <c>UseSerilogRequestLogging()</c>.
/// Mirrors the shape of <c>Serilog.AspNetCore.RequestLoggingOptions</c>;
/// every property forwards to the P6 Layer-1 twin.
/// </summary>
public sealed class RequestLoggingOptions
{
    // The P6 inner instance that this wrapper projects onto the Layer-2 surface.
    internal readonly P6Opts Inner;

    /// <summary>Initialises a new <see cref="RequestLoggingOptions"/> with P6 defaults.</summary>
    public RequestLoggingOptions() => Inner = new P6Opts();

    // Internal constructor: used by SerilogApplicationBuilderExtensions to lift
    // a P6 opts instance that P6's middleware has already constructed.
    internal RequestLoggingOptions(P6Opts inner) => Inner = inner;

    /// <summary>Message template for the per-request summary line.</summary>
    public string MessageTemplate
    {
        get => Inner.MessageTemplate;
        set => Inner.MessageTemplate = value;
    }

    /// <summary>Determines the log level for the summary line.</summary>
    public Func<HttpContext, double, Exception?, Serilog.Events.LogEventLevel> GetLevel
    {
        // Enum ordinal pivot: L1Events.LogEventLevel → Serilog.Events.LogEventLevel.
        // Identical ordinal values — this cast is the one permitted exception
        // on the DRY-tripwire allowlist for enum pivots.
        get => (ctx, elapsed, ex) => (Serilog.Events.LogEventLevel)(int)Inner.GetLevel(ctx, elapsed, ex);
        set => Inner.GetLevel = (ctx, elapsed, ex) => (L1Events.LogEventLevel)(int)value(ctx, elapsed, ex);
    }

    /// <summary>Optional callback to add extra properties to the request summary event.</summary>
    public Action<HttpContext>? EnrichDiagnosticContext
    {
        get => Inner.EnrichDiagnosticContext;
        set => Inner.EnrichDiagnosticContext = value;
    }
}

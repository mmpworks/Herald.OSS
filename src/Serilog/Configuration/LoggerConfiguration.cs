#nullable enable

using MMP.Herald.Pipeline;
using MMP.Herald.Quick;
using MMP.Herald.Serilog.Configuration;
using MMP.Herald.Serilog.Core;
using MMP.Herald.Serilog.Destructuring;
using MMP.Herald.Serilog.Sinks;

// LoggerConfiguration mirrors real Serilog's root namespace position
// (Serilog.LoggerConfiguration), so `using Serilog;` -> `using MMP.Herald.Serilog;`
// resolves it without a separate .Configuration import.
namespace MMP.Herald.Serilog;

public sealed class LoggerConfiguration
{
    internal QuickLogBuilder Builder { get; } = QuickLogBuilder.Create();

    // S5 seam: shared applicator for Serilog IDestructuringPolicy registrations.
    // LoggerDestructuringConfiguration.With() adds policies here;
    // LoggerSinkConfiguration passes this to SerilogSinkAdapter so the adapter
    // can thread it to the mirror LogEvent constructor at dispatch time.
    internal SerilogDestructuringApplicator SerilogPolicyApplicator { get; } = new();

    // Shared adapter: both WriteTo.Sink() and AuditTo.Sink() register their sinks
    // here so that mixed configurations (WriteTo + AuditTo) route through a single
    // null-kind slot without collision.  Lazily registered the first time either
    // channel calls Sink() — see LoggerSinkConfiguration.EnsureAdapterRegistered().
    internal SerilogSinkAdapter? SharedSinkAdapter { get; set; }

    public MinimumLevelConfiguration MinimumLevel { get; }
    public LoggerSinkConfiguration WriteTo { get; }

    /// <summary>
    /// Audit sink configuration — exceptions from sinks registered here propagate
    /// to the caller instead of being swallowed.  Use for compliance-critical paths
    /// where silent delivery failure is unacceptable.
    ///
    /// <para>
    /// Mirrors <c>Serilog.LoggerConfiguration.AuditTo</c>.  Every method on this
    /// object behaves identically to its <see cref="WriteTo"/> counterpart except
    /// that sink failures surface as <c>AuditLogFailureException</c> rather than
    /// being silently dropped.
    /// </para>
    /// </summary>
    public LoggerSinkConfiguration AuditTo { get; }

    public LoggerEnrichmentConfiguration Enrich { get; }
    public LoggerDestructuringConfiguration Destructure { get; }

    /// <summary>
    /// Fluent filter step. Mirrors <c>Serilog.LoggerConfiguration.Filter</c>:
    /// <c>.Filter.ByExcluding(...)</c> / <c>.Filter.ByIncludingOnly(...)</c> /
    /// <c>.Filter.With(ILogFilter)</c> gate the pipeline. String-DSL overloads
    /// (<c>ByExcluding(string)</c>) live in the MMP.Herald.Serilog.Expressions
    /// package as extensions on this accessor.
    /// </summary>
    public LoggerFilterConfiguration Filter { get; }
    public LoggerConfiguration ReadFrom => this;

    // W5 memoization (load-bearing, not a follow-up):
    // The pipeline is built at most once per LoggerConfiguration. Both
    // CreateLogger() (the Serilog-shaped adapter) and CreateHeraldLogger() (the
    // raw Herald StructuredLogger the MEL bridge consumes) return views over
    // this single PipelineBuildResult. Without it, the MEL bridge path — which
    // calls both — would build the pipeline twice and register every sink twice,
    // doubling flush and downstream logging.
    //
    // C# 12 note: kept the explicit null-coalescing assignment rather than the
    // C# 14 'field' keyword (Directory.Build.props pins net8/net9 to C# 13).
    private PipelineBuildResult? _built;

    public LoggerConfiguration()
    {
        MinimumLevel = new MinimumLevelConfiguration(this);
        WriteTo = new LoggerSinkConfiguration(this, defaultAuditMode: false);
        AuditTo = new LoggerSinkConfiguration(this, defaultAuditMode: true);
        Enrich = new LoggerEnrichmentConfiguration(this);
        Destructure = new LoggerDestructuringConfiguration(this);
        Filter = new LoggerFilterConfiguration(this);
    }

    // Build once, cache, and reuse. Not synchronised by design: a
    // LoggerConfiguration is a single-threaded build-time object (the Serilog
    // contract is "configure on one thread, then CreateLogger"). Two concurrent
    // first-callers would be a misuse that no Serilog config guards against.
    private PipelineBuildResult BuildOnce()
        => _built ??= Builder.Build();

    public ILogger CreateLogger()
        => SerilogLoggerAdapter.FromBuild(BuildOnce());

    /// <summary>
    /// Build (or reuse) the pipeline and return the raw Herald
    /// <see cref="StructuredLogger"/> behind it — the same pipeline
    /// <see cref="CreateLogger"/> wraps in a Serilog-shaped adapter.
    ///
    /// <para>
    /// This is the bridge accessor the MEL integration uses
    /// (<c>AddSerilog(LoggerConfiguration)</c>): MEL's <c>HeraldLoggerProvider</c>
    /// consumes the raw <see cref="StructuredLogger"/> directly. Because the
    /// build is memoized, calling both <see cref="CreateLogger"/> and this
    /// accessor on the same configuration yields two views over one pipeline —
    /// the sinks, async buffer, and flush path are shared, never duplicated.
    /// </para>
    /// </summary>
    public StructuredLogger CreateHeraldLogger() => BuildOnce().Logger;
}

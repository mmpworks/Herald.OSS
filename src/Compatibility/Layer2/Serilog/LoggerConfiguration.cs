#nullable enable

// Layer-2 mirror of Serilog.LoggerConfiguration.
// Wraps the Layer-1 MMP.Herald.Serilog.Configuration.LoggerConfiguration.
// Each property returns a Layer-2 Configuration object that wraps the Layer-1 counterpart.
// CreateLogger() returns Serilog.Core.Logger — a wrapper over the Layer-1 ILogger result.
//
// Layer-1 twin: MMP.Herald.Serilog.Configuration.LoggerConfiguration

using L1Config = MMP.Herald.Serilog.Configuration;

namespace Serilog;

/// <summary>
/// Fluent builder for a <see cref="Core.Logger"/>.
/// Entry point for all Serilog configuration: <c>new LoggerConfiguration()...CreateLogger()</c>.
/// </summary>
public sealed class LoggerConfiguration
{
    private readonly L1Config.LoggerConfiguration _inner = new();

    // ── Configuration surfaces ────────────────────────────────────────────────

    /// <summary>Level-filtering configuration.</summary>
    public Configuration.LoggerMinimumLevelConfiguration MinimumLevel { get; }

    /// <summary>Sink (write) configuration.</summary>
    public Configuration.LoggerSinkConfiguration WriteTo { get; }

    /// <summary>
    /// Audit sink configuration — exceptions from sinks registered here propagate
    /// to the caller instead of being swallowed.
    /// </summary>
    public Configuration.LoggerSinkConfiguration AuditTo { get; }

    /// <summary>Enrichment configuration.</summary>
    public Configuration.LoggerEnrichmentConfiguration Enrich { get; }

    /// <summary>Destructuring configuration.</summary>
    public Configuration.LoggerDestructuringConfiguration Destructure { get; }

    /// <summary>
    /// Read-from self — matches the Serilog pattern where <c>ReadFrom</c> returns
    /// the root configuration for extension-method attachment.
    /// </summary>
    public LoggerConfiguration ReadFrom => this;

    // ── Constructor ───────────────────────────────────────────────────────────

    public LoggerConfiguration()
    {
        MinimumLevel = new Configuration.LoggerMinimumLevelConfiguration(this, _inner.MinimumLevel);
        WriteTo      = new Configuration.LoggerSinkConfiguration(this, _inner.WriteTo);
        AuditTo      = new Configuration.LoggerSinkConfiguration(this, _inner.AuditTo);
        Enrich       = new Configuration.LoggerEnrichmentConfiguration(this, _inner.Enrich);
        Destructure  = new Configuration.LoggerDestructuringConfiguration(this, _inner.Destructure);
    }

    // ── Build ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Build and return a configured <see cref="Core.Logger"/>.
    ///
    /// <para>
    /// CRIT-FM-G2: Layer-1's <c>CreateLogger()</c> returns
    /// <c>MMP.Herald.Serilog.ILogger</c> (an interface). This method wraps that
    /// result in <c>Serilog.Core.Logger</c> — it is a wrapper construction, not
    /// a cast. <c>Logger</c> is sealed and holds the L1 instance as <c>_inner</c>.
    /// </para>
    /// </summary>
    public Core.Logger CreateLogger() => new(_inner.CreateLogger());
}

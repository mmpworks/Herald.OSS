#nullable enable

using MMP.Herald.Quick;
using MMP.Herald.Serilog.Core;

namespace MMP.Herald.Serilog.Configuration;

/// <summary>
/// Serilog-compatible configuration builder. Translates the familiar
/// <c>new LoggerConfiguration().MinimumLevel.*().WriteTo.*().Enrich.*().CreateLogger()</c>
/// fluent chain onto Herald's <see cref="QuickLogBuilder"/>.
///
/// <para>
/// Task 2: <c>MinimumLevel</c> and <c>CreateLogger()</c> are fully wired.
/// Task 3: <c>WriteTo</c> wired.
/// Task 4: <c>Enrich</c> wired.
/// </para>
/// </summary>
public sealed class LoggerConfiguration
{
    // The underlying Herald builder. Internal so configuration classes can
    // forward calls without exposing the Herald API to consumers.
    internal QuickLogBuilder Builder { get; } = QuickLogBuilder.Create();

    /// <summary>Fluent entry point for minimum-level configuration.</summary>
    public MinimumLevelConfiguration MinimumLevel { get; }

    /// <summary>Fluent entry point for sink registration.</summary>
    public LoggerSinkConfiguration WriteTo { get; }

    /// <summary>Fluent entry point for enrichment configuration.</summary>
    public LoggerEnrichmentConfiguration Enrich { get; }

    /// <summary>Initializes a new <see cref="LoggerConfiguration"/>.</summary>
    public LoggerConfiguration()
    {
        MinimumLevel = new MinimumLevelConfiguration(this);
        WriteTo = new LoggerSinkConfiguration(this);
        Enrich = new LoggerEnrichmentConfiguration(this);
    }

    /// <summary>
    /// Build the configured Herald pipeline and return a Serilog-compatible logger.
    /// </summary>
    public ILogger CreateLogger()
        => SerilogLoggerAdapter.FromBuild(Builder.Build());
}

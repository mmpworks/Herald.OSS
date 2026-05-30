#nullable enable

using MMP.Herald.Quick;
using MMP.Herald.Serilog.Core;

namespace MMP.Herald.Serilog.Configuration;

/// <summary>
/// Serilog-compatible configuration builder. Translates the familiar
/// <c>new LoggerConfiguration().MinimumLevel.*().WriteTo.*().CreateLogger()</c>
/// fluent chain onto Herald's <see cref="QuickLogBuilder"/>.
///
/// <para>
/// Task 2: <c>MinimumLevel</c> and <c>CreateLogger()</c> are fully wired.
/// <c>WriteTo</c> was wired by Task 3. <c>Enrich</c> is reserved for a later task.
/// </para>
/// </summary>
public sealed class LoggerConfiguration
{
    // The underlying Herald builder. Internal so LoggerSinkConfiguration and
    // MinimumLevelConfiguration can forward calls without exposing the Herald
    // API to consumers.
    internal QuickLogBuilder Builder { get; } = QuickLogBuilder.Create();

    /// <summary>Fluent entry point for minimum-level configuration.</summary>
    public MinimumLevelConfiguration MinimumLevel { get; }

    /// <summary>Fluent entry point for sink registration.</summary>
    public LoggerSinkConfiguration WriteTo { get; }

    /// <summary>Initializes a new <see cref="LoggerConfiguration"/>.</summary>
    public LoggerConfiguration()
    {
        MinimumLevel = new MinimumLevelConfiguration(this);
        WriteTo = new LoggerSinkConfiguration(this);
    }

    /// <summary>
    /// Build the configured Herald pipeline and return a Serilog-compatible logger.
    /// </summary>
    public ILogger CreateLogger()
        => SerilogLoggerAdapter.FromBuild(Builder.Build());
}

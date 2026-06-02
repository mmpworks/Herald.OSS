#nullable enable

using System;
using System.IO;
using MMP.Herald.Configuration.Runtime;
using MMP.Herald.Levels;
using MMP.Herald.Output.Rendering;
using MMP.Herald.Routing;

namespace MMP.Herald.Serilog.Formatting;

/// <summary>
/// A console-kind <see cref="ILogSinkProvider"/> that routes each event through
/// a user-supplied <see cref="ITextFormatter"/> instead of Herald's default
/// console output transformer.
///
/// <para>
/// Registered as an "additional provider" by
/// <see cref="MMP.Herald.Serilog.Configuration.LoggerSinkConfiguration.Console(ITextFormatter, Events.LogEventLevel)"/>
/// so it overrides the default <c>ConsoleSinkProvider</c> for the pipeline
/// that uses the formatter overload. The additional-provider path in
/// <c>JsonConfiguredLoggingBootstrapFactory.BuildSinkProviderRegistry</c>
/// runs after the built-in registration, so the last-write-wins indexer
/// overwrite takes effect.
/// </para>
///
/// <para>
/// The provider creates a <see cref="TextFormatterConsoleLogger"/> which does
/// the native→mirror→format→write flow directly, bypassing Herald's
/// transformer-registry and ANSI styling so the user formatter has full
/// control over the rendered text.
/// </para>
/// </summary>
internal sealed class TextFormatterConsoleSinkProvider : ILogSinkProvider
{
    private readonly ITextFormatter _formatter;

    internal TextFormatterConsoleSinkProvider(ITextFormatter formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);
        _formatter = formatter;
    }

    // Overrides the built-in "console" kind so the user formatter takes over.
    public string SinkKind => MMP.Herald.Services.KnownSinkKinds.Console;

    public MMP.Herald.ILogger CreateSink(
        LoggingRuntimeSinkDefinition definition,
        ILogLevelRegistry levelRegistry,
        ILogOutputTransformerRegistry transformerRegistry)
        => new TextFormatterConsoleLogger(_formatter);
}

/// <summary>
/// Minimal <see cref="MMP.Herald.ILogger"/> that routes each native
/// <see cref="MMP.Herald.Events.LogEvent"/> through a user-supplied
/// <see cref="ITextFormatter"/> and writes the result to
/// <see cref="System.Console.Write(string)"/>.
///
/// <para>
/// The native event is wrapped in the P1 Serilog mirror
/// (<see cref="MMP.Herald.Serilog.Events.LogEvent"/>) so the user formatter
/// receives the standard Serilog-shaped type. Herald's console styling pipeline
/// is bypassed — the formatter owns the entire rendered representation.
/// </para>
///
/// <para>
/// Implements only <c>Log(LogEvent)</c>; the async default on
/// <see cref="MMP.Herald.ILogger"/> delegates to it without additional
/// work needed here.
/// </para>
/// </summary>
internal sealed class TextFormatterConsoleLogger : MMP.Herald.ILogger
{
    private readonly ITextFormatter _formatter;

    internal TextFormatterConsoleLogger(ITextFormatter formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);
        _formatter = formatter;
    }

    public void Log(MMP.Herald.Events.LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);

        // Wrap native event in the Serilog-shaped P1 mirror.
        var mirror = new Events.LogEvent(logEvent);
        using var writer = new StringWriter();
        _formatter.Format(mirror, writer);
        System.Console.Write(writer.ToString());
    }
}

#nullable enable

using System;
using System.IO;
using MMP.Herald.Configuration.Runtime;
using MMP.Herald.Levels;
using MMP.Herald.Output.Rendering;
using MMP.Herald.Routing;

namespace MMP.Herald.Serilog.Formatting;

/// <summary>
/// A console-kind <see cref="ILogSinkProvider"/> that routes each event through a
/// <see cref="ThemedConsoleTextFormatter"/>. Registered by the W4
/// <c>WriteTo.Console(ConsoleTheme)</c> verb as an additional provider so it
/// overrides the default console provider for the pipeline that selects a theme.
///
/// <para>
/// Unlike <see cref="TextFormatterConsoleSinkProvider"/> (the net9-gated
/// user-formatter bridge), this provider carries no kernel-buffer dependency and so
/// compiles and runs on net8/net9/net10 — the W4 overload is reachable on every TFM.
/// </para>
/// </summary>
internal sealed class ThemedConsoleSinkProvider : ILogSinkProvider
{
    private readonly ThemedConsoleTextFormatter _formatter;

    internal ThemedConsoleSinkProvider(ThemedConsoleTextFormatter formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);
        _formatter = formatter;
    }

    // Overrides the built-in "console" kind so the themed formatter takes over.
    public string SinkKind => MMP.Herald.Services.KnownSinkKinds.Console;

    public MMP.Herald.ILogger CreateSink(
        LoggingRuntimeSinkDefinition definition,
        ILogLevelRegistry levelRegistry,
        ILogOutputTransformerRegistry transformerRegistry)
        => new ThemedConsoleLogger(_formatter);
}

/// <summary>
/// Minimal <see cref="MMP.Herald.ILogger"/> that renders each native event through a
/// <see cref="ThemedConsoleTextFormatter"/> and writes the (optionally ANSI-styled)
/// result to <see cref="System.Console.Write(string)"/>.
/// </summary>
internal sealed class ThemedConsoleLogger : MMP.Herald.ILogger
{
    private readonly ThemedConsoleTextFormatter _formatter;

    internal ThemedConsoleLogger(ThemedConsoleTextFormatter formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);
        _formatter = formatter;
    }

    public void Log(MMP.Herald.Events.LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);

        // Wrap native event in the Serilog-shaped P1 mirror, then format.
        var mirror = new Events.LogEvent(logEvent);
        using var writer = new StringWriter();
        _formatter.Format(mirror, writer);
        System.Console.Write(writer.ToString());
    }
}

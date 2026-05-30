#nullable enable

// Layer-2 mirror of Serilog.Configuration.LoggerMinimumLevelConfiguration.
// Wraps the Layer-1 MMP.Herald.Serilog.Configuration.MinimumLevelConfiguration.
// Every method is a one-line forward; the return type is Serilog.LoggerConfiguration (L2).
//
// Layer-1 twin: MMP.Herald.Serilog.Configuration.MinimumLevelConfiguration

using L1Config = MMP.Herald.Serilog.Configuration;

namespace Serilog.Configuration;

/// <summary>
/// Fluent <c>MinimumLevel.*</c> configuration surface for the Layer-2
/// <see cref="Serilog.LoggerConfiguration"/> wrapper.
/// </summary>
public sealed class LoggerMinimumLevelConfiguration
{
    private readonly Serilog.LoggerConfiguration _root;
    private readonly L1Config.MinimumLevelConfiguration _inner;

    internal LoggerMinimumLevelConfiguration(
        Serilog.LoggerConfiguration root,
        L1Config.MinimumLevelConfiguration inner)
    {
        _root = root;
        _inner = inner;
    }

    /// <summary>Set the pipeline minimum level to <c>verbose</c>.</summary>
    public Serilog.LoggerConfiguration Verbose() { _inner.Verbose(); return _root; }

    /// <summary>Set the pipeline minimum level to <c>debug</c>.</summary>
    public Serilog.LoggerConfiguration Debug() { _inner.Debug(); return _root; }

    /// <summary>Set the pipeline minimum level to <c>information</c>.</summary>
    public Serilog.LoggerConfiguration Information() { _inner.Information(); return _root; }

    /// <summary>Set the pipeline minimum level to <c>warning</c>.</summary>
    public Serilog.LoggerConfiguration Warning() { _inner.Warning(); return _root; }

    /// <summary>Set the pipeline minimum level to <c>error</c>.</summary>
    public Serilog.LoggerConfiguration Error() { _inner.Error(); return _root; }

    /// <summary>Set the pipeline minimum level to <c>fatal</c>.</summary>
    public Serilog.LoggerConfiguration Fatal() { _inner.Fatal(); return _root; }

    /// <summary>Set the pipeline minimum level to <paramref name="minimumLevel"/>.</summary>
    public Serilog.LoggerConfiguration Is(Events.LogEventLevel minimumLevel)
    {
        _inner.Is((MMP.Herald.Serilog.Events.LogEventLevel)(int)minimumLevel);
        return _root;
    }

    /// <summary>Wire a <see cref="Core.LoggingLevelSwitch"/> as the dynamic level controller.</summary>
    public Serilog.LoggerConfiguration ControlledBy(Core.LoggingLevelSwitch levelSwitch)
    {
        _inner.ControlledBy(levelSwitch.Inner);
        return _root;
    }

    /// <summary>Add a per-source-context level override. Calls accumulate.</summary>
    public Serilog.LoggerConfiguration Override(string source, Events.LogEventLevel minimumLevel)
    {
        _inner.Override(source, (MMP.Herald.Serilog.Events.LogEventLevel)(int)minimumLevel);
        return _root;
    }
}

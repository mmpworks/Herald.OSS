#nullable enable

using System;
using System.Diagnostics.CodeAnalysis;
using MMP.Herald.Serilog;
using MMP.Herald.Serilog.Core;
using MMP.Herald.Serilog.Destructuring;

namespace MMP.Herald.Serilog.Configuration;

public sealed class LoggerDestructuringConfiguration
{
    private readonly LoggerConfiguration _root;

    internal LoggerDestructuringConfiguration(LoggerConfiguration root) => _root = root;

    [RequiresUnreferencedCode(
        "IDestructuringPolicy implementations may walk arbitrary object graphs via reflection.")]
    public LoggerConfiguration With(IDestructuringPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        // Register on the native Herald chain (render-time, for message text).
        _root.Builder.WithDestructuringPolicy(new SerilogDestructuringPolicyBridge(policy));
        // Register on the Serilog applicator (capture-time, for Properties dict).
        _root.SerilogPolicyApplicator.Add(policy);
        return _root;
    }

    [RequiresUnreferencedCode(
        "ByTransforming projection may walk arbitrary object graphs via reflection.")]
    public LoggerConfiguration ByTransforming<T>(Func<T, object?> transform)
    {
        ArgumentNullException.ThrowIfNull(transform);
        // Register on the native Herald chain (render-time).
        _root.Builder.Destructure(transform);
        // Register on the applicator (capture-time, for Properties dict).
        _root.SerilogPolicyApplicator.Add(new ByTransformingPolicyAdapter<T>(transform));
        return _root;
    }

    public LoggerConfiguration AsScalar<T>()
    {
        _root.Builder.WithDestructuringAsScalar<T>();
        return _root;
    }
}

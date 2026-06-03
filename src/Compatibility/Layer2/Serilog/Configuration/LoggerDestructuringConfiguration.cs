#nullable enable

// Layer-2 mirror of Serilog.Configuration.LoggerDestructuringConfiguration.
// Wraps Layer-1 MMP.Herald.Serilog.Configuration.LoggerDestructuringConfiguration.
// Every method is a one-line forward.
//
// Layer-1 twin: MMP.Herald.Serilog.Configuration.LoggerDestructuringConfiguration
//
// IDestructuringPolicy bridge:
//   L2 Serilog.Core.IDestructuringPolicy and L1 MMP.Herald.Serilog.Core.IDestructuringPolicy
//   are separate interfaces. L2PolicyBridge adapts L2→L1 so With() compiles.

using System;
using System.Diagnostics.CodeAnalysis;
using L1Config  = MMP.Herald.Serilog.Configuration;
using L1Core    = MMP.Herald.Serilog.Core;
using L1Events  = MMP.Herald.Serilog.Events;

namespace Serilog.Configuration;

/// <summary>
/// Fluent <c>Destructure.*</c> configuration surface for the Layer-2
/// <see cref="Serilog.LoggerConfiguration"/> wrapper.
/// </summary>
public sealed class LoggerDestructuringConfiguration
{
    private readonly Serilog.LoggerConfiguration _root;
    private readonly L1Config.LoggerDestructuringConfiguration _inner;

    internal LoggerDestructuringConfiguration(
        Serilog.LoggerConfiguration root,
        L1Config.LoggerDestructuringConfiguration inner)
    {
        _root  = root;
        _inner = inner;
    }

    /// <summary>Register a user-authored <see cref="Core.IDestructuringPolicy"/>.</summary>
    [RequiresUnreferencedCode(
        "IDestructuringPolicy implementations may walk arbitrary object graphs via reflection.")]
    public Serilog.LoggerConfiguration With(Core.IDestructuringPolicy policy)
    {
        // Null-guard lives in Layer-1's With() — no redundant check here (CRIT-FM-L1 DRY rule).
        _inner.With(new L2PolicyBridge(policy));
        return _root;
    }

    /// <summary>Apply a projection transform when destructuring instances of <typeparamref name="T"/>.</summary>
    [RequiresUnreferencedCode(
        "ByTransforming projection may walk arbitrary object graphs via reflection.")]
    public Serilog.LoggerConfiguration ByTransforming<T>(Func<T, object?> transform)
    {
        // Null-guard lives in Layer-1's ByTransforming() — no redundant check here (CRIT-FM-L1 DRY rule).
        _inner.ByTransforming(transform);
        return _root;
    }

    /// <summary>Treat instances of <typeparamref name="T"/> as scalar (not destructured).</summary>
    public Serilog.LoggerConfiguration AsScalar<T>()
    {
        _inner.AsScalar<T>();
        return _root;
    }

    // Adapts a Layer-2 Serilog.Core.IDestructuringPolicy into the Layer-1
    // MMP.Herald.Serilog.Core.IDestructuringPolicy contract.
    private sealed class L2PolicyBridge : L1Core.IDestructuringPolicy
    {
        private readonly Core.IDestructuringPolicy _l2Policy;
        internal L2PolicyBridge(Core.IDestructuringPolicy l2Policy) => _l2Policy = l2Policy;

        [RequiresUnreferencedCode(
            "Destructuring policies may walk arbitrary object graphs via reflection.")]
        public bool TryDestructure(
            object value,
            L1Core.ILogEventPropertyValueFactory propertyValueFactory,
            out L1Events.LogEventPropertyValue result)
        {
            var l2Result = default(Events.LogEventPropertyValue);
            var handled = _l2Policy.TryDestructure(
                value,
                new L2PropertyValueFactoryBridge(propertyValueFactory),
                out l2Result!);

            result = handled ? l2Result.InnerValue : default!;
            return handled;
        }
    }

    // Adapts a Layer-1 ILogEventPropertyValueFactory into the Layer-2 contract.
    private sealed class L2PropertyValueFactoryBridge : Core.ILogEventPropertyValueFactory
    {
        private readonly L1Core.ILogEventPropertyValueFactory _inner;
        internal L2PropertyValueFactoryBridge(L1Core.ILogEventPropertyValueFactory inner)
            => _inner = inner;

        [RequiresUnreferencedCode("Value projection uses reflection on arbitrary user types.")]
        public Events.LogEventPropertyValue CreatePropertyValue(object? value, bool destructureObjects)
            => Events.ValueMap.ToL2(_inner.CreatePropertyValue(value, destructureObjects));
    }
}

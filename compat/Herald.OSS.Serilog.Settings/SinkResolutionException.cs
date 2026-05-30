// Copyright (c) 2026 MMPWorks LLC
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

#nullable enable

using System;

namespace Herald.OSS.Serilog.Settings;

/// <summary>
/// Thrown by the settings parser (Task 3) when a sink name in appsettings.json
/// cannot be resolved to a registered factory.
///
/// <para>
/// <b>Do not swallow this via SelfLog</b>. An unresolvable sink name means the
/// application will not log where it expects to — silent swallowing hides the
/// misconfiguration and is worse than a startup crash. (Pre-mortem Risk 2.)
/// </para>
///
/// <para>
/// Pre-compiled community NuGet sinks (Serilog.Sinks.Seq,
/// Serilog.Sinks.MSSqlServer, Serilog.Sinks.Datadog, etc.) are compiled against
/// the real Serilog assembly identity (<c>PublicKeyToken=24c2f752a8e58a10</c>),
/// which this shim does not and cannot match. Such sinks cannot be loaded via
/// appsettings.json; register a source-compiled alternative instead.
/// </para>
/// </summary>
public sealed class SinkResolutionException : Exception
{
    /// <summary>The name that could not be resolved.</summary>
    public string SinkName { get; }

    /// <summary>The reason the name could not be resolved.</summary>
    public string Reason { get; }

    /// <param name="sinkName">The name from the <c>WriteTo</c> entry.</param>
    /// <param name="reason">A short explanation (e.g. "not registered").</param>
    public SinkResolutionException(string sinkName, string reason)
        : base(
            $"Cannot resolve sink '{sinkName}': {reason}. " +
            "If this is a pre-compiled NuGet sink (e.g. Serilog.Sinks.Seq), it cannot be loaded — " +
            "the Herald shim is not strong-named and cannot satisfy the assembly identity the sink expects. " +
            "Register a source-compiled alternative via LoggerSinkRegistry.RegisterSink().")
    {
        SinkName = sinkName;
        Reason   = reason;
    }
}

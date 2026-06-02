// Copyright (c) 2026 MMPWorks LLC
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

#nullable enable

using Microsoft.Extensions.Configuration;
using MMP.Herald.Serilog;
using Herald.OSS.Serilog.Settings.Parsing;

namespace Herald.OSS.Serilog.Settings;

/// <summary>
/// Extends <see cref="LoggerConfiguration"/> (returned by
/// <c>LoggerConfiguration.ReadFrom</c>) with the <see cref="Configuration"/>
/// binding method so consumers can write:
///
/// <code>
/// new LoggerConfiguration()
///     .ReadFrom.Configuration(configuration)
///     .CreateLogger();
/// </code>
///
/// <para>
/// <b>Schema read:</b> the <c>Serilog</c> section of <paramref name="configuration"/>;
/// structure mirrors Serilog.Settings.Configuration.  Sink resolution uses
/// <see cref="LoggerSinkRegistry.Default"/> unless a custom registry is supplied.
/// </para>
///
/// <para>
/// <b>Apache-2.0 standalone project.</b> Reimplements the
/// Serilog.Settings.Configuration schema against Herald's engine — the real
/// add-on cannot bind to the unsigned shim.
/// </para>
/// </summary>
public static class ConfigurationLoggerConfigurationExtensions
{
    /// <summary>
    /// Reads the <c>Serilog</c> section from <paramref name="configuration"/>
    /// and applies it to the logger configuration.
    ///
    /// <para>
    /// Called as <c>loggerConfig.ReadFrom.Configuration(configuration)</c> because
    /// <c>LoggerConfiguration.ReadFrom</c> returns <c>this</c> — the extension
    /// target is the same <see cref="LoggerConfiguration"/> instance.
    /// </para>
    /// </summary>
    /// <param name="loggerConfig">
    ///   The <see cref="LoggerConfiguration"/> to configure (the instance returned
    ///   by <c>ReadFrom</c>).
    /// </param>
    /// <param name="configuration">
    ///   The root <see cref="IConfiguration"/> to read from. The reader looks under
    ///   the <c>Serilog</c> key; a missing key is a no-op.
    /// </param>
    /// <param name="sinkRegistry">
    ///   Optional custom registry for resolving <c>WriteTo[].Name</c> entries.
    ///   Defaults to <see cref="LoggerSinkRegistry.Default"/> (all Herald built-in
    ///   sinks pre-seeded). Pass a <see cref="LoggerSinkRegistry.CreateDefault"/>
    ///   copy with additional registrations to support in-house or third-party sinks.
    /// </param>
    /// <param name="enricherRegistry">
    ///   Optional custom registry for resolving <c>Enrich[].Name</c> entries.
    ///   Defaults to <see cref="LoggerEnricherRegistry.Default"/>.
    /// </param>
    /// <returns>
    ///   The same <paramref name="loggerConfig"/> instance for fluent chaining.
    /// </returns>
    public static LoggerConfiguration Configuration(
        this LoggerConfiguration loggerConfig,
        IConfiguration configuration,
        LoggerSinkRegistry? sinkRegistry = null,
        LoggerEnricherRegistry? enricherRegistry = null)
    {
        var reader = new SerilogConfigurationReader(
            loggerConfig,
            sinkRegistry ?? LoggerSinkRegistry.Default,
            enricherRegistry ?? LoggerEnricherRegistry.Default);

        reader.Apply(configuration);

        return loggerConfig;
    }
}

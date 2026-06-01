// Copyright (c) 2026 MMPWorks LLC
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.
#nullable enable

using System;
using Herald.Sinks.File.Providers;
using MMP.Herald.Output.Writers;
using MMP.Herald.Quick;
using MMP.Herald.Routing;

namespace Herald.Sinks.File;

/// <summary>
/// Opt-in registration helper for the file sink package. Mirrors the
/// shape of <c>SignalFxSinkRegistration</c> and the other sibling sinks
/// in this monorepo — Core does not register the file providers any
/// more, so consumers wire them in here.
/// </summary>
/// <example>
/// <code>
/// // Direct registry use:
/// var registry = new LogSinkProviderRegistry();
/// FileSinkRegistration.RegisterAll(registry);
///
/// // QuickLogBuilder fluent API:
/// var b = new QuickLogBuilder()
///     .WithFileSinkProviders()
///     .WithFileSink("logs/app.log");
/// </code>
/// </example>
public static class FileSinkRegistration
{
    /// <summary>
    /// Register the text and NDJSON file providers on the given
    /// <see cref="LogSinkProviderRegistry"/>. Idempotent —
    /// double-registration overwrites in place, so calling this more
    /// than once is safe.
    /// </summary>
    /// <param name="registry">Target sink provider registry.</param>
    /// <param name="pathResolver">
    /// Optional path resolver. Pass one when the host needs sandboxing
    /// (e.g. <see cref="ConfinedPathResolver"/>); otherwise the
    /// providers fall back to their default resolver.
    /// </param>
    public static void RegisterAll(
        LogSinkProviderRegistry registry,
        ILogFilePathResolver? pathResolver = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        registry.Register(new TextFileSinkProvider(pathResolver));
        registry.Register(new JsonFileSinkProvider(pathResolver));
    }

    /// <summary>
    /// QuickLogBuilder convenience: attach the text and NDJSON file
    /// providers to the builder so subsequent <c>WithFileSink</c> calls
    /// resolve at <c>Build()</c> time. Equivalent to two
    /// <see cref="QuickLogBuilder.WithCustomSinkProvider(ILogSinkProvider)"/>
    /// calls — the route through the builder's existing custom-provider
    /// hook keeps the registration path consistent with every other
    /// sibling sink package.
    /// </summary>
    /// <param name="builder">QuickLogBuilder under construction.</param>
    /// <param name="pathResolver">Optional path resolver, see <see cref="RegisterAll"/>.</param>
    /// <returns>The builder, to keep the call chain fluent.</returns>
    public static QuickLogBuilder WithFileSinkProviders(
        this QuickLogBuilder builder,
        ILogFilePathResolver? pathResolver = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder
            .WithCustomSinkProvider(new TextFileSinkProvider(pathResolver))
            .WithCustomSinkProvider(new JsonFileSinkProvider(pathResolver));
    }
}

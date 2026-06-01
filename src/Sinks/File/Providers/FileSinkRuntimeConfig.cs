// Copyright (c) 2026 MMPWorks LLC
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.
#nullable enable

using System;
using MMP.Herald.Configuration.Runtime;
using MMP.Herald.Configuration.Sinks;

namespace Herald.Sinks.File.Providers;

/// <summary>
/// Legacy-path helper for the File sink.
///
/// <para>
/// <b>Pass-4a realignment.</b> The v2 bag path now lives in
/// <see cref="FilesManagerPolicyMapper"/> (which consumes
/// <see cref="SinkPropertyBag"/>) and the factory routes
/// bag-populated definitions there directly. This helper is the
/// fall-through for definitions that still carry the pre-v2 typed
/// <see cref="LoggingRuntimeSinkDefinition.Path"/> +
/// <see cref="LoggingRuntimeSinkDefinition.RollingPolicy"/> fields
/// instead of a property bag — tests, hand-built hosts, and code-first
/// pipelines that haven't moved to QuickLogBuilder yet.
/// </para>
///
/// <para>
/// Stays internal so the legacy slot interpretation is owned by the
/// File package — Core never reaches into the typed slots either.
/// Once every File call site moves to the v2 bag-driven path, this
/// helper and the in-Core writers it feeds (<c>FileLineWriter</c> /
/// <c>RollingFileLineWriter</c>) retire together.
/// </para>
/// </summary>
internal static class FileSinkRuntimeConfig
{
    /// <summary>
    /// Resolved legacy-path config. <see cref="Path"/> stays nullable
    /// so the factory can fail with a field-named
    /// <see cref="ArgumentException"/> when neither the bag nor the
    /// legacy slot supplied a path.
    /// </summary>
    public readonly record struct Resolved(
        string Path,
        LoggingRuntimeFileRollingPolicy? Rolling);

    /// <summary>
    /// Resolve the legacy typed-slot pair into a non-null path and an
    /// optional rolling policy. Throws
    /// <see cref="ArgumentException"/> when the legacy
    /// <see cref="LoggingRuntimeSinkDefinition.Path"/> is missing —
    /// matches the 16-sink convention of provider-side field-named
    /// validation.
    /// </summary>
    public static Resolved From(LoggingRuntimeSinkDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var path = SinkPropertyBag.Nullify(definition.Path)
            ?? throw new ArgumentException(
                $"File sink '{definition.Name}' has no v2 properties bag and no legacy " +
                "Path field. Either fill the bag (set 'logDirectory' + 'logFileTemplate' in " +
                "the mmpform) or set Path on the LoggingRuntimeSinkDefinition.",
                nameof(definition));

        return new Resolved(path, definition.RollingPolicy);
    }
}

// Copyright (c) 2026 MMPWorks LLC
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.
#nullable enable

using System;
using MMP.Herald.Configuration.Runtime;
using MMP.Herald.Output.Writers;
using MMP.RollingFiles;

namespace Herald.Sinks.File.Providers;

/// <summary>
/// Shared helper for creating file writers from sink definitions.
/// Used by both <see cref="TextFileSinkProvider"/> and
/// <see cref="JsonFileSinkProvider"/> to avoid duplication.
///
/// <para>
/// v2 sinks (definitions carrying a <c>Properties</c> bag) route
/// every write through <see cref="FilesManager"/> from the
/// <c>MMP.RollingFiles</c> library; the shim
/// <see cref="FilesManagerLineWriter"/> keeps the in-Core
/// <see cref="ILineWriter"/> shape so the sink providers stay
/// unchanged. Phase 1 of the migration: project reference today,
/// package reference once the standalone repo publishes.
/// </para>
///
/// <para>
/// Legacy sinks (no <c>Properties</c> bag, just the typed <c>Path</c>
/// + <c>RollingPolicy</c> fields) still construct the old in-Core
/// writers via <see cref="FileSinkRuntimeConfig"/>. Those writers
/// stay in Core during the migration window; once every caller moves
/// to the v2 bag-driven path, this branch and the in-Core writers can
/// retire together.
/// </para>
///
/// <para>
/// <b>Pass-4a realignment.</b> Required-field validation lives here
/// (not in the mapper) and throws <see cref="ArgumentException"/> with
/// the operator-facing field name in the message — matching the shape
/// the other 16 already-migrated sinks use. Every File call site goes
/// through QuickLogBuilder, which fills the bag with mmpform defaults
/// (<c>logDirectory</c> defaults to <c>""</c> and <c>logFileTemplate</c>
/// to <c>"log-file"</c>); production deployments never see this throw
/// because the bag arrives populated. The factory enforces the
/// contract at the point operators most often debug from — a
/// hand-crafted definition that forgot a required key now fails with
/// a field name they can match against the mmpform.
/// </para>
/// </summary>
internal static class LogSinkFileWriterFactory
{
    public static ILineWriter Create(
        LoggingRuntimeSinkDefinition definition,
        ILogFilePathResolver? pathResolver = null)
    {
        ArgumentNullException.ThrowIfNull(definition);

        // v2 path: any definition with a populated Properties bag uses
        // MMP.RollingFiles. Bag interpretation lives in the
        // mapper so this factory stays a one-line dispatch. The shim
        // takes the policy (not a constructed manager) so filesystem
        // IO is deferred to the first WriteLine, matching the legacy
        // RollingFileLineWriter timing.
        if (definition.Properties is { Count: > 0 })
        {
            var resolved = FilesManagerPolicyMapper.From(definition);
            if (string.IsNullOrWhiteSpace(resolved.Directory))
            {
                throw new ArgumentException(
                    $"File sink '{definition.Name}' is missing the required 'logDirectory' " +
                    "property. Set it in configuration-text_file.mmpform / " +
                    "configuration-json_file.mmpform.",
                    nameof(definition));
            }
            if (string.IsNullOrWhiteSpace(resolved.FileNameTemplate))
            {
                throw new ArgumentException(
                    $"File sink '{definition.Name}' is missing the required 'logFileTemplate' " +
                    "property. Set it in configuration-text_file.mmpform / " +
                    "configuration-json_file.mmpform.",
                    nameof(definition));
            }

            var policy = FilesManagerPolicyMapper.ToPolicy(resolved);
            return new FilesManagerLineWriter(policy);
        }

        // Legacy path: callers that have not migrated to the property
        // bag still produce typed Path + RollingPolicy. Route them
        // through the old in-Core writers via FileSinkRuntimeConfig
        // (which collapses to (Path, Rolling) regardless of source).
        var legacy = FileSinkRuntimeConfig.From(definition);
        return legacy.Rolling is not null
            ? new RollingFileLineWriter(legacy.Path, legacy.Rolling, pathResolver)
            : new FileLineWriter(legacy.Path, pathResolver);
    }
}

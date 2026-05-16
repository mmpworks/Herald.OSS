// Copyright (c) 2026 MMPWorks LLC
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.
#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using MMP.Herald.Configuration.Json;

namespace MMP.Herald.Configuration.Sinks;

/// <summary>
/// Builds the "user values" dictionary the v2 contract layer feeds into
/// <see cref="SinkPropertyBagBuilder"/>. The same dictionary shape — keyed
/// by the mmpform binding names (logDirectory, logFileTemplate,
/// logExtension, rolling*, maxFileSize, etc.) — flows from two sources:
///
/// <list type="bullet">
///   <item><description><b>Serializer side</b> (<see cref="From(string?, JsonFileRollingConfig?)"/>):
///     reads off the live <see cref="MMP.Herald.Quick.QuickLogBuilder"/> when
///     <c>BuildJsonConfig</c> writes a sink JSON.</description></item>
///   <item><description><b>Publish side</b> (<see cref="From(FileSinkInspectionView)"/>):
///     reads off a <see cref="MMP.Herald.Quick.BuilderInspection"/> when the
///     management API publishes the live pipeline to the dashboard.</description></item>
/// </list>
///
/// <para>Both paths end up here so the dictionary shape, the rolling-key
/// guards, the path split, and the byte formatter all live once. Adding
/// a new contract key — or a new spelling — means one edit, not two.</para>
/// </summary>
public static class FileSinkUserValuesBuilder
{
    /// <summary>
    /// Build the user-values dictionary from a serializer-side view: the
    /// raw file path and the optional rolling-config record. Used by
    /// <c>FileSinkConfigSerializer</c> when emitting JSON from the live
    /// QuickLogBuilder.
    /// </summary>
    public static Dictionary<string, object?> From(string? filePath, JsonFileRollingConfig? rolling)
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        AddPathTriplet(values, filePath);

        if (rolling is null)
        {
            // Match the publish-side default: an absent rolling block
            // explicitly says "rolling is off" so the dashboard doesn't
            // see a stale value from the contract default.
            values["rollingLogsEnabled"] = false;
            return values;
        }

        values["rollingLogsEnabled"] = true;
        if (!string.IsNullOrEmpty(rolling.Interval))
            values["rollingInterval"] = rolling.Interval;
        if (rolling.MaxBytes is { } mb && mb > 0)
            values["maxFileSize"] = FormatBytes(mb);
        if (rolling.MaxRetainedFiles is { } mr)
            values["maxRetainedFiles"] = (long)mr;
        if (!string.IsNullOrEmpty(rolling.FileNameSuffix))
        {
            // text_file's mmpform binding is `namePattern`; json_file's
            // is `fileNamePattern`. Write both so SinkPropertyBagBuilder
            // can pick whichever name the active contract carries.
            values["namePattern"] = rolling.FileNameSuffix;
            values["fileNamePattern"] = rolling.FileNameSuffix;
        }
        if (rolling.TotalSizeCapBytes is { } tc && tc > 0)
            values["totalSizeCap"] = FormatBytes(tc);
        if (rolling.RetentionDays is { } rd)
            values["retentionDays"] = (long)rd;

        return values;
    }

    /// <summary>
    /// Build the user-values dictionary from a publish-side view: the
    /// fields a <c>BuilderInspection</c> exposes about the live pipeline.
    /// Used by <c>HeraldManagementApi.BuildFileSinkConfig</c> when it
    /// produces the dashboard's flow response.
    /// </summary>
    public static Dictionary<string, object?> From(FileSinkInspectionView inspection)
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        AddPathTriplet(values, inspection.FilePath);

        values["rollingLogsEnabled"] = inspection.HasFileRolling;
        if (!inspection.HasFileRolling) return values;

        values["rollingInterval"] = inspection.FileRollingInterval ?? "daily";
        if (inspection.FileMaxBytes is { } mb)        values["maxFileSize"]      = FormatBytes(mb);
        if (inspection.FileMaxRetainedFiles is { } mr) values["maxRetainedFiles"] = (long)mr;
        if (inspection.FileNamePattern is not null)
        {
            values["namePattern"] = inspection.FileNamePattern;
            values["fileNamePattern"] = inspection.FileNamePattern;
        }
        if (inspection.TotalSizeCapBytes is { } tc)   values["totalSizeCap"]     = FormatBytes(tc);
        if (inspection.RetentionDays is { } rd)       values["retentionDays"]    = (long)rd;

        return values;
    }

    /// <summary>
    /// Split a file path into the v2 binding triplet (logDirectory,
    /// logFileTemplate, logExtension). Empty / null inputs yield no
    /// entries — the contract default fills in.
    /// </summary>
    private static void AddPathTriplet(Dictionary<string, object?> values, string? filePath)
    {
        if (string.IsNullOrEmpty(filePath)) return;
        var dir = Path.GetDirectoryName(filePath)?.Replace('\\', '/');
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        var ext = Path.GetExtension(filePath)?.TrimStart('.');
        if (!string.IsNullOrEmpty(dir))      values["logDirectory"]    = dir;
        if (!string.IsNullOrEmpty(fileName)) values["logFileTemplate"] = fileName;
        if (!string.IsNullOrEmpty(ext))      values["logExtension"]    = ext;
    }

    /// <summary>
    /// Render a byte count as the human-readable shape the dashboard
    /// expects in the bag (e.g. <c>1MB</c>, <c>10GB</c>). Mirrors the
    /// shape <see cref="ParseHumanByteSize"/> consumes round-tripping
    /// out of the bag back into the runtime.
    /// </summary>
    public static string FormatBytes(long bytes)
    {
        if (bytes >= 1_073_741_824L) return (bytes / 1_073_741_824L) + "GB";
        if (bytes >= 1_048_576L)     return (bytes / 1_048_576L) + "MB";
        if (bytes >= 1_024L)         return (bytes / 1_024L) + "KB";
        return bytes + "B";
    }
}

/// <summary>
/// Snapshot of the file-sink fields <see cref="FileSinkUserValuesBuilder"/>
/// reads from a <see cref="MMP.Herald.Quick.BuilderInspection"/>. Lives
/// here so <c>Configuration.Sinks</c> doesn't take a hard reference on
/// the Quick namespace just to read these values.
/// </summary>
public readonly record struct FileSinkInspectionView(
    string? FilePath,
    bool HasFileRolling,
    string? FileRollingInterval,
    long? FileMaxBytes,
    int? FileMaxRetainedFiles,
    string? FileNamePattern,
    long? TotalSizeCapBytes,
    int? RetentionDays);

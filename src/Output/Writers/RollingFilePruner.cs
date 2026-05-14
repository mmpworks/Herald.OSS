#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MMP.Herald.Output.Writers;

/// <summary>
/// File pruning logic for rolling log files.
/// Handles per-period retention (MaxRetainedFiles), total queue cap (LogQueueSize),
/// age-based retention (RetentionDays), and total size cap (TotalSizeCapBytes).
/// Extracted from RollingFileLineWriter for single responsibility.
/// </summary>
internal static class RollingFilePruner
{
    /// <summary>
    /// Prune the oldest files until count is at or below maxRetained.
    /// </summary>
    public static void PruneToRetained(List<string> files, int maxRetained) {
        while (files.Count > maxRetained)
        {
            if (!TryDelete(files[0])) break;
            files.RemoveAt(0);
        }
    }

    /// <summary>
    /// Prune the oldest files until count is at or below maxQueueSize,
    /// but never delete the active file.
    /// </summary>
    public static void PruneToQueueSize(List<string> files, int maxQueueSize, string activeResolvedPath) {
        while (files.Count > maxQueueSize)
        {
            var oldest = files[0];
            if (string.Equals(oldest, activeResolvedPath, StringComparison.OrdinalIgnoreCase))
                break;
            if (!TryDelete(oldest)) break;
            files.RemoveAt(0);
        }
    }

    /// <summary>
    /// Delete files older than the specified retention period.
    /// Uses file last-write time for age comparison. Never deletes the active file.
    /// </summary>
    public static void PruneByAge(List<string> files, int retentionDays, string activeResolvedPath) {
        if (retentionDays <= 0) return;

        var cutoff = DateTimeOffset.UtcNow.AddDays(-retentionDays);

        // Walk from oldest (index 0) forward. Stop when we hit a file that's young enough.
        while (files.Count > 0)
        {
            var candidate = files[0];
            if (string.Equals(candidate, activeResolvedPath, StringComparison.OrdinalIgnoreCase))
                break;

            if (!TryGetLastWriteTimeUtc(candidate, out var lastWrite))
            {
                // Can't read metadata - skip this file to avoid data loss
                break;
            }

            if (lastWrite >= cutoff)
                break; // This file (and all after it) are within retention

            if (!TryDelete(candidate)) break;
            files.RemoveAt(0);
        }
    }

    /// <summary>
    /// Delete oldest files until the total size of all remaining files is at or below
    /// the specified cap. Never deletes the active file.
    /// </summary>
    public static void PruneByTotalSize(List<string> files, long totalSizeCapBytes, string activeResolvedPath) {
        if (totalSizeCapBytes <= 0) return;

        var totalSize = ComputeTotalSize(files);

        while (totalSize > totalSizeCapBytes && files.Count > 0)
        {
            var candidate = files[0];
            if (string.Equals(candidate, activeResolvedPath, StringComparison.OrdinalIgnoreCase))
                break;

            var fileSize = TryGetFileSize(candidate);
            if (!TryDelete(candidate)) break;
            files.RemoveAt(0);
            totalSize -= fileSize;
        }
    }

    /// <summary>
    /// Find all files matching the base path's name pattern in the same directory,
    /// sorted lexicographically (which is chronologically given date-in-name format).
    /// </summary>
    public static List<string> FindAllRolledFiles(string resolvedBasePath) {
        var directory = Path.GetDirectoryName(resolvedBasePath);

        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return new List<string>();

        var nameWithoutExt = Path.GetFileNameWithoutExtension(resolvedBasePath);
        var extension = Path.GetExtension(resolvedBasePath);

        return Directory
            .GetFiles(directory, $"{nameWithoutExt}*{extension}")
            .OrderBy(static f => f, StringComparer.Ordinal)
            .ToList();
    }

    public static bool TryDelete(string path) {
        try
        {
            if (File.Exists(path)) File.Delete(path);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }

    // -- Private helpers --

    private static bool TryGetLastWriteTimeUtc(string path, out DateTimeOffset lastWrite) {
        lastWrite = default;
        try
        {
            if (!File.Exists(path)) return false;
            lastWrite = new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static long TryGetFileSize(string path) {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? info.Length : 0;
        }
        catch (IOException)
        {
            return 0;
        }
    }

    private static long ComputeTotalSize(List<string> files) {
        long total = 0;
        foreach (var file in files)
        {
            total += TryGetFileSize(file);
        }
        return total;
    }
}

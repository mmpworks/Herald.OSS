#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MMP.Herald.Diagnostics;
using MMP.Herald.Events;

namespace MMP.Herald.Failures;

/// <summary>
/// Diagnostic failure sink that records logging pipeline failures to memory and
/// optionally mirrors them to a text file for inspection.
///
/// <para>
/// The bounded-buffer mechanics (enqueue with eviction, snapshot,
/// dropped-count tracking) come from
/// <see cref="BoundedNoticeBuffer{T}"/>. File mirroring is the
/// sink's own concern — the buffer stays generic and the mirror
/// stays local to the failure domain.
/// </para>
/// </summary>
public sealed class DiagnosticLogFailureSink : ILogFailureSink
{
    private readonly object _fileSync = new();
    private readonly string? _path;
    private readonly BoundedNoticeBuffer<FailureRecord> _buffer;

    public DiagnosticLogFailureSink(
        int maxEntries = 200,
        string? path = null)
    {
        _buffer = new BoundedNoticeBuffer<FailureRecord>(maxEntries);
        _path = path;
    }

    public void ReportFailure(LogEvent logEvent, Exception exception, string source)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        var record = new FailureRecord(
            TimeUtc: DateTime.UtcNow,
            Source: source,
            LevelKey: logEvent.Level.Key,
            Category: logEvent.Category.Value,
            Message: logEvent.Message,
            ExceptionType: exception.GetType().FullName ?? exception.GetType().Name,
            ExceptionMessage: exception.Message);

        _buffer.Enqueue(record);

        if (!string.IsNullOrWhiteSpace(_path))
        {
            // File mirroring keeps its own lock so writers don't
            // interleave lines. The in-memory buffer is already
            // thread-safe on its own lock; concurrent ReportFailure
            // calls land in the buffer in some order, then serialise
            // through the file write.
            lock (_fileSync)
            {
                AppendToFile(record);
            }
        }
    }

    /// <summary>
    /// Returns a snapshot of recent failure records in oldest-to-newest order.
    /// </summary>
    public IReadOnlyList<FailureRecord> GetEntries() => _buffer.Snapshot();

    /// <summary>
    /// Number of failure records evicted from the in-memory buffer
    /// because the buffer was full when a new failure was reported.
    /// Useful for diagnostics surfaces that want to indicate
    /// "you're seeing N of M total failures."
    /// </summary>
    public long DroppedEntryCount => _buffer.DroppedCount;

    private void AppendToFile(FailureRecord record)
    {
        var path = _path!;

        var directory = Path.GetDirectoryName(path);

        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.AppendAllText(path, BuildLine(record), Encoding.UTF8);
    }

    private static string BuildLine(FailureRecord record)
    {
        return
            $"{record.TimeUtc:O} " +
            $"source={record.Source} " +
            $"level={record.LevelKey} " +
            $"category={record.Category} " +
            $"message=\"{Escape(record.Message)}\" " +
            $"exceptionType=\"{Escape(record.ExceptionType)}\" " +
            $"exceptionMessage=\"{Escape(record.ExceptionMessage)}\"" +
            Environment.NewLine;
    }

    private static string Escape(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\t", "\\t", StringComparison.Ordinal);
    }

    public sealed record FailureRecord(
        DateTime TimeUtc,
        string Source,
        string LevelKey,
        string Category,
        string Message,
        string ExceptionType,
        string ExceptionMessage);
}
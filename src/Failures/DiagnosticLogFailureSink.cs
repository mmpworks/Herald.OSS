#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MMP.Herald.Events;

namespace MMP.Herald.Failures;

/// <summary>
/// Diagnostic failure sink that records logging pipeline failures to memory and
/// optionally mirrors them to a text file for inspection.
/// </summary>
public sealed class DiagnosticLogFailureSink : ILogFailureSink
{
    private readonly object _sync = new();
    private readonly int _maxEntries;
    private readonly string? _path;
    private readonly Queue<FailureRecord> _entries;

    public DiagnosticLogFailureSink(
        int maxEntries = 200,
        string? path = null)
    {
        if (maxEntries <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxEntries),
                maxEntries,
                "Max entries must be greater than zero.");
        }

        _maxEntries = maxEntries;
        _path = path;
        _entries = new Queue<FailureRecord>(maxEntries);
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

        lock (_sync)
        {
            _entries.Enqueue(record);

            while (_entries.Count > _maxEntries)
            {
                _entries.Dequeue();
            }

            if (!string.IsNullOrWhiteSpace(_path))
            {
                AppendToFile(record);
            }
        }
    }

    /// <summary>
    /// Returns a snapshot of recent failure records in oldest-to-newest order.
    /// </summary>
    public IReadOnlyList<FailureRecord> GetEntries()
    {
        lock (_sync)
        {
            return [.. _entries];
        }
    }

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
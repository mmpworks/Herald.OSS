#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using MMP.Herald.Events;
using MMP.Herald.Levels;

namespace MMP.Herald.Addons.Replay;

/// <summary>
/// Reads log events from Herald WAL files (NDJSON format) and protobuf files
/// (length-delimited records) for replay, debugging, and post-mortem analysis.
///
/// WAL format (from DurableBufferLogger): tab-separated lines:
///   sequence\ttimestamp\tlevel_key\tcategory\tmessage
///
/// Protobuf format (from ProtobufFileLogSink): 4-byte LE length prefix + OTLP protobuf payload.
///
/// Usage:
///   var events = LogReplayReader.ReadWal("logs/herald-wal.jsonl");
///   foreach (var evt in events) { logger.Log(evt); }
///
///   var rawRecords = LogReplayReader.ReadProtobufRecords("logs/output.pb");
///   // Send raw records to an OTLP-compatible backend
/// </summary>
public static class LogReplayReader
{
    /// <summary>
    /// Read log events from a Herald WAL file (NDJSON tab-separated format).
    /// Returns reconstructed LogEvent instances suitable for replay through the pipeline.
    /// </summary>
    public static IReadOnlyList<LogEvent> ReadWal(string walFilePath) {
        ArgumentException.ThrowIfNullOrWhiteSpace(walFilePath);

        if (!File.Exists(walFilePath))
        {
            return [];
        }

        var events = new List<LogEvent>();
        var lines = File.ReadAllLines(walFilePath);

        foreach (var line in lines)
        {
            var evt = TryParseWalEntry(line);
            if (evt is not null)
            {
                events.Add(evt);
            }
        }

        return events;
    }

    /// <summary>
    /// Read log events from a Herald WAL file, filtered by sequence range.
    /// Useful for replaying only undelivered events (sequence > lastDelivered).
    /// </summary>
    public static IReadOnlyList<LogEvent> ReadWal(string walFilePath, long afterSequence) {
        var all = ReadWal(walFilePath);
        var filtered = new List<LogEvent>();

        foreach (var evt in all)
        {
            if (evt.Context.TryGetValue(Services.LogContextKeys.ReplaySequence, out var seqObj) &&
                seqObj is long seq && seq > afterSequence)
            {
                filtered.Add(evt);
            }
        }

        return filtered;
    }

    /// <summary>
    /// Read raw protobuf records from a Herald protobuf file (length-delimited format).
    /// Each record is a complete OTLP protobuf payload that can be sent to any
    /// OTLP-compatible backend or decoded with Google.Protobuf.
    /// </summary>
    public static IReadOnlyList<byte[]> ReadProtobufRecords(string protobufFilePath) {
        ArgumentException.ThrowIfNullOrWhiteSpace(protobufFilePath);

        if (!File.Exists(protobufFilePath))
        {
            return [];
        }

        var records = new List<byte[]>();

        using var stream = new FileStream(protobufFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

        var lengthBuffer = new byte[4];

        while (stream.Position < stream.Length)
        {
            // Read 4-byte little-endian length prefix
            var bytesRead = stream.Read(lengthBuffer, 0, 4);
            if (bytesRead < 4)
            {
                break; // truncated record
            }

            var payloadLength = BitConverter.ToInt32(lengthBuffer, 0);
            if (payloadLength <= 0 || payloadLength > 10 * 1024 * 1024) // sanity: max 10MB per record
            {
                break; // corrupted
            }

            var payload = new byte[payloadLength];
            bytesRead = stream.Read(payload, 0, payloadLength);
            if (bytesRead < payloadLength)
            {
                break; // truncated
            }

            records.Add(payload);
        }

        return records;
    }

    /// <summary>
    /// Count events in a WAL file without fully parsing them.
    /// </summary>
    public static int CountWalEntries(string walFilePath) {
        if (!File.Exists(walFilePath)) return 0;

        var count = 0;
        foreach (var line in File.ReadLines(walFilePath))
        {
            if (!string.IsNullOrWhiteSpace(line) && line.Contains('\t'))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Count records in a protobuf file without fully reading them.
    /// </summary>
    public static int CountProtobufRecords(string protobufFilePath) {
        if (!File.Exists(protobufFilePath)) return 0;

        var count = 0;
        using var stream = new FileStream(protobufFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var lengthBuffer = new byte[4];

        while (stream.Position < stream.Length)
        {
            if (stream.Read(lengthBuffer, 0, 4) < 4) break;
            var len = BitConverter.ToInt32(lengthBuffer, 0);
            if (len <= 0 || len > 10 * 1024 * 1024) break;
            stream.Seek(len, SeekOrigin.Current);
            count++;
        }

        return count;
    }

    private static LogEvent? TryParseWalEntry(string line) {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        var parts = line.Split('\t', 5);
        if (parts.Length < 5)
        {
            return null;
        }

        if (!long.TryParse(parts[0], CultureInfo.InvariantCulture, out var seq))
        {
            return null;
        }

        var message = parts[4]
            .Replace("\\n", "\n", StringComparison.Ordinal)
            .Replace("\\r", "\r", StringComparison.Ordinal);

        return new LogEvent(
            TimeUtc: DateTimeOffset.TryParse(parts[1], CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var ts) ? ts : DateTimeOffset.UtcNow,
            Level: new LogLevel(parts[2], parts[2]),
            Category: new LogCategory(parts[3]),
            MessageTemplate: message,
            Message: message,
            Properties: LogEvent.EmptyProperties,
            Context: new Dictionary<string, object?>
            {
                [Services.LogContextKeys.Replayed] = true,
                [Services.LogContextKeys.ReplaySequence] = seq
            });
    }
}

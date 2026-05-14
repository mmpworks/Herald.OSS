#nullable enable

using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace MMP.Herald.Routing.Loopback;

/// <summary>
/// Rolling writer for one sink's loopback log file. Rolls every N
/// entries (configurable per pipeline, default 1000) so a long-running
/// loopback session does not produce a single multi-gigabyte file.
///
/// <para>File path follows the convention
/// <c>{logDir}/{suffix}-{sinkName}.{ext}</c>, where <c>{ext}</c> is
/// <c>ndjson</c> when <see cref="WriteAsNdjson"/> is true and
/// <c>log</c> when false. On rotation the current file is renamed to
/// <c>...{base}.1.{ext}</c>, with the previous <c>.1</c> shifting to
/// <c>.2</c> and so on up to a small retention cap.</para>
///
/// <para>Writes are synchronous + flushed per entry. The interceptor
/// runs on the sink hot path; loopback I/O is the cost of the dry-run
/// feature. A future optimisation could batch flushes on a worker
/// thread, but the cost is negligible at the event volumes loopback
/// targets (a developer dry-running a sink, not a production firehose).</para>
/// </summary>
public sealed class LoopbackFileWriter : IDisposable
{
    // Cap on numbered rotations kept on disk. Older rolls are deleted
    // so a forgotten loopback session does not eat the disk.
    private const int MaxRotations = 5;

    private readonly string _logDir;
    private readonly string _baseName;
    private readonly string _extension;
    private readonly int _entriesPerFile;
    private readonly bool _writeAsNdjson;
    private readonly object _lock = new();

    private StreamWriter? _writer;
    private int _entriesInCurrentFile;
    private bool _disposed;

    public LoopbackFileWriter(
        string logDir,
        string suffix,
        string sinkName,
        bool writeAsNdjson,
        int entriesPerFile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logDir);
        ArgumentException.ThrowIfNullOrWhiteSpace(sinkName);

        _logDir = logDir;
        // Suffix is optional. When empty, the filename is just
        // <sinkName>.{ext}; with a suffix it becomes <suffix>-<sinkName>.{ext}.
        _baseName = string.IsNullOrEmpty(suffix) ? sinkName : suffix + "-" + sinkName;
        _extension = writeAsNdjson ? "ndjson" : "log";
        _entriesPerFile = entriesPerFile > 0 ? entriesPerFile : 1000;
        _writeAsNdjson = writeAsNdjson;
    }

    /// <summary>True when the file format is NDJSON (one JSON event per line).</summary>
    public bool WriteAsNdjson => _writeAsNdjson;

    /// <summary>Append one entry. Rolls automatically when the per-file cap is hit.</summary>
    public void Write(LoopbackLogEntry entry, string? plainTextLine = null)
    {
        ArgumentNullException.ThrowIfNull(entry);

        // Format the line outside the lock so contention windows stay
        // tight. NDJSON: source-generated JSON. Plain text: caller
        // supplies the rendered line via plainTextLine.
        string line = _writeAsNdjson
            ? JsonSerializer.Serialize(entry, LoopbackJsonContext.Default.LoopbackLogEntry)
            : (plainTextLine ?? entry.Message);

        lock (_lock)
        {
            if (_disposed) return;

            EnsureWriter();
            _writer!.WriteLine(line);
            _writer.Flush();
            _entriesInCurrentFile++;

            if (_entriesInCurrentFile >= _entriesPerFile)
            {
                Roll();
            }
        }
    }

    private void EnsureWriter()
    {
        if (_writer is not null) return;
        Directory.CreateDirectory(_logDir);
        var path = CurrentPath();
        _writer = new StreamWriter(
            new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private void Roll()
    {
        // Close current writer, rotate filenames, reset state. Failure
        // here is logged-and-swallowed-by-caller territory; we leave
        // the writer null so the next Write recreates it.
        _writer?.Dispose();
        _writer = null;
        _entriesInCurrentFile = 0;

        var current = CurrentPath();
        if (!File.Exists(current)) return;

        // Shift .{N-1}.{ext} → .{N}.{ext} from the top down so the
        // current file's rotation slot opens up. Anything past
        // MaxRotations is deleted.
        for (var i = MaxRotations; i >= 1; i--)
        {
            var older = NumberedPath(i);
            if (!File.Exists(older)) continue;
            if (i == MaxRotations) { TryDelete(older); continue; }
            var next = NumberedPath(i + 1);
            TryDelete(next);
            try { File.Move(older, next); } catch { /* ignore */ }
        }
        try { File.Move(current, NumberedPath(1)); } catch { /* ignore */ }
    }

    private string CurrentPath() => Path.Combine(_logDir, _baseName + "." + _extension);
    private string NumberedPath(int n) => Path.Combine(_logDir, _baseName + "." + n + "." + _extension);
    private static void TryDelete(string path) { try { File.Delete(path); } catch { /* ignore */ } }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            _writer?.Dispose();
            _writer = null;
        }
    }
}

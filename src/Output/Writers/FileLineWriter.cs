#nullable enable

using System;
using System.IO;
using System.Text;

namespace MMP.Herald.Output.Writers;

/// <summary>
/// Append-only file writer backed by a long-lived <see cref="FileStream"/>.
///
/// <para>
/// Older revisions opened, appended, and closed the file on every
/// <see cref="WriteLine"/> via <see cref="File.AppendAllText(string,string)"/> —
/// three syscalls per event, plus the kernel-side path resolution. Wrapping
/// the file sink with <c>BatchingLogger</c> hid most of the cost in production,
/// but callers that bypass batching (game-loop direct sinks, console-style
/// flushes, low-volume diagnostic writers) paid it on every line.
/// </para>
///
/// <para>
/// The stream stays open for the writer's lifetime under a <see cref="FileShare.ReadWrite"/>
/// share so log-tailing tools (<c>tail -f</c>, the dashboard's live view, log
/// shippers) can read concurrently. Writes call <see cref="FileStream.Flush()"/>
/// without <c>flushToDisk</c> so bytes are visible to other readers right
/// away — that matches the implicit guarantee callers got from
/// <c>File.AppendAllText</c>. Durability-on-write is opt-in via the
/// <c>writeThrough</c> constructor flag, which sets <see cref="FileOptions.WriteThrough"/>.
/// </para>
///
/// <para>
/// Disposal flushes pending bytes with <c>flushToDisk: true</c> and closes
/// the stream. The class is safe to leak — <see cref="FileStream"/> has its
/// own finalizer that disposes on garbage collection — but explicit
/// <c>Dispose</c> remains the right thing to do at process shutdown.
/// </para>
/// </summary>
public sealed class FileLineWriter : ILineWriter, IDisposable
{
    private readonly string _filePath;
    private readonly ILogFilePathResolver _pathResolver;
    private readonly bool _writeThrough;
    private readonly object _sync = new();

    private FileStream? _stream;
    private string? _openedPath;
    private bool _disposed;

    public FileLineWriter(string filePath, ILogFilePathResolver? pathResolver = null, bool writeThrough = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = filePath;
        _pathResolver = pathResolver ?? DefaultLogFilePathResolver.Instance;
        _writeThrough = writeThrough;
    }

    public void WriteLine(string line)
    {
        ArgumentNullException.ThrowIfNull(line);

        // Escape control characters (newline, carriage return, C0 controls) so
        // attacker-controlled property values cannot forge additional log lines.
        // Allocation-free when the line is clean, which is the common case.
        var safeLine = LineSanitizer.EscapeControlCharacters(line);

        var resolvedPath = _pathResolver.Resolve(_filePath);
        EnsureDirectory(resolvedPath);

        // Encode + length captured outside the lock so the critical section
        // stays as short as possible. UTF-8 sizing is the same as
        // File.AppendAllText would have done.
        var bytes = Encoding.UTF8.GetBytes(safeLine + Environment.NewLine);

        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            EnsureStreamOpen(resolvedPath);
            _stream!.Write(bytes, 0, bytes.Length);
            // OS-level flush — bytes become visible to other readers
            // immediately. This matches the implicit guarantee of
            // File.AppendAllText (which closed after every append). It does
            // not fsync; durability lives on the WriteThrough opt-in.
            _stream.Flush();
        }
    }

    /// <summary>
    /// Visible for testing: returns the path of the currently-open stream, or
    /// <c>null</c> when no stream has been opened yet. Operators do not call
    /// this; tests use it to confirm rotation happened.
    /// </summary>
    internal string? OpenedPath
    {
        get { lock (_sync) return _openedPath; }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;

            if (_stream is null) return;

            try
            {
                // flushToDisk: true forces an fsync so bytes survive a process
                // crash or power loss between Flush() and the OS write-back.
                // Cheap on shutdown — we only do it once.
                _stream.Flush(true);
            }
            catch
            {
                // Swallow on shutdown: we still want the underlying handle
                // released even if the flush hits an I/O error.
            }

            _stream.Dispose();
            _stream = null;
            _openedPath = null;
        }
    }

    // Caller must hold _sync.
    private void EnsureStreamOpen(string resolvedPath)
    {
        if (_stream is not null && string.Equals(_openedPath, resolvedPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // The resolved path changed under us — likely a path resolver that
        // shifts based on date / tenant / scope. Drain the current stream
        // before pivoting so the previous file ends up complete on disk.
        if (_stream is not null)
        {
            try { _stream.Flush(true); } catch { /* see Dispose */ }
            _stream.Dispose();
        }

        var options = _writeThrough ? FileOptions.WriteThrough : FileOptions.None;
        _stream = new FileStream(
            resolvedPath,
            FileMode.Append,
            FileAccess.Write,
            FileShare.ReadWrite,
            bufferSize: 4096,
            options: options);
        _openedPath = resolvedPath;
    }

    private static void EnsureDirectory(string resolvedPath)
    {
        var directory = Path.GetDirectoryName(resolvedPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }
}

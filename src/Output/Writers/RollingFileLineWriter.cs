#nullable enable

using System;
using System.IO;
using System.Linq;
using System.Text;
using MMP.Herald.Configuration.Runtime;

namespace MMP.Herald.Output.Writers;

/// <summary>
/// File writer that rotates to a new file on a time interval, when a size limit is reached,
/// or both. Supports hourly, daily, and custom intervals (fixed-minute windows within the hour).
///
/// <para>
/// Backed by a long-lived <see cref="FileStream"/> per active path (same
/// rationale as <see cref="FileLineWriter"/>): keeping the file open between
/// writes saves the open + append + close syscall round-trip every line
/// would otherwise pay. Each rotation closes the current stream cleanly
/// (flush + dispose) before opening a new one for the rotated path, so the
/// just-closed file lands on disk complete.
/// </para>
///
/// Decomposed into focused helpers:
///   RollingFilePeriod    - pure period-start calculation
///   RollingFilePathBuilder - pure file name construction
///   RollingFilePruner    - file system pruning operations
///
/// This class orchestrates the write + rotate + prune lifecycle.
/// </summary>
public sealed class RollingFileLineWriter : ILineWriter, IDisposable
{
    private readonly string _baseFilePath;
    private readonly LoggingRuntimeFileRollingPolicy _policy;
    private readonly ILogFilePathResolver _pathResolver;
    private readonly bool _writeThrough;
    private readonly object _sync = new();

    private string _activePath;
    private DateTimeOffset _activePeriodStart;
    private int _sizeSequence;

    private FileStream? _stream;
    private string? _openedPath;
    private long _currentFileBytes;
    private bool _disposed;

    public RollingFileLineWriter(
        string baseFilePath,
        LoggingRuntimeFileRollingPolicy policy,
        ILogFilePathResolver? pathResolver = null,
        bool writeThrough = false) {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseFilePath);
        ArgumentNullException.ThrowIfNull(policy);

        _baseFilePath = baseFilePath;
        _policy = policy;
        _pathResolver = pathResolver ?? DefaultLogFilePathResolver.Instance;
        _writeThrough = writeThrough;
        _activePeriodStart = RollingFilePeriod.GetPeriodStart(DateTimeOffset.UtcNow, policy);
        _sizeSequence = 1;
        _activePath = RollingFilePathBuilder.Build(_baseFilePath, _activePeriodStart, policy, _sizeSequence);
    }

    public void WriteLine(string line) {
        ArgumentNullException.ThrowIfNull(line);

        // Escape control characters so attacker-controlled property values
        // cannot forge additional log lines on replay. Allocation-free when
        // the line is clean.
        var safeLine = LineSanitizer.EscapeControlCharacters(line);

        // Encode outside the lock — UTF-8 sizing is idempotent and the byte
        // array is local to this call.
        var bytes = Encoding.UTF8.GetBytes(safeLine + Environment.NewLine);

        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            AdvancePeriodIfNeeded();

            var resolved = _pathResolver.Resolve(_activePath);
            EnsureDirectory(resolved);

            GuardMaxBytes(bytes.LongLength, safeLine);
            RollIfSizeExceeded(ref resolved, bytes.LongLength);

            EnsureStreamOpen(resolved);
            _stream!.Write(bytes, 0, bytes.Length);
            _stream.Flush();
            _currentFileBytes += bytes.LongLength;

            ApplyRetentionPolicies(resolved);
        }
    }

    /// <summary>
    /// Removes all rolled log files except the currently active one.
    /// </summary>
    public void Flush(int? count = null) {
        lock (_sync)
        {
            var files = RollingFilePruner.FindAllRolledFiles(_pathResolver.Resolve(_baseFilePath));
            var activePath = _pathResolver.Resolve(_activePath);

            files.RemoveAll(f => string.Equals(f, activePath, StringComparison.OrdinalIgnoreCase));

            if (count.HasValue && count.Value < files.Count)
            {
                files = files.Take(count.Value).ToList();
            }

            foreach (var file in files)
            {
                RollingFilePruner.TryDelete(file);
            }
        }
    }

    /// <summary>
    /// Removes all log files including the active one and starts a fresh file.
    /// </summary>
    public void Clear() {
        lock (_sync)
        {
            // Drop the open stream first — Windows refuses to delete a file
            // while a handle still holds it open, even with FileShare.ReadWrite.
            CloseStreamCore();

            var files = RollingFilePruner.FindAllRolledFiles(_pathResolver.Resolve(_baseFilePath));
            var activePath = _pathResolver.Resolve(_activePath);

            foreach (var file in files)
            {
                RollingFilePruner.TryDelete(file);
            }

            RollingFilePruner.TryDelete(activePath);

            _activePeriodStart = RollingFilePeriod.GetPeriodStart(DateTimeOffset.UtcNow, _policy);
            _sizeSequence = 1;
            _activePath = RollingFilePathBuilder.Build(_baseFilePath, _activePeriodStart, _policy, _sizeSequence);
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            CloseStreamCore();
        }
    }

    /// <summary>
    /// Visible for testing: returns the path of the currently-open stream, or
    /// <c>null</c> when no stream has been opened yet (or it was closed by
    /// <see cref="Clear"/> / <see cref="Dispose"/>).
    /// </summary>
    internal string? OpenedPath
    {
        get { lock (_sync) return _openedPath; }
    }

    // -- Private orchestration --

    /// <summary>
    /// Apply all retention policies after each write: queue size, age-based, and total size cap.
    /// Each policy prunes independently - the most restrictive combination wins.
    /// </summary>
    private void ApplyRetentionPolicies(string activeResolvedPath) {
        var needsPruning = _policy.LogQueueSize.HasValue ||
                           _policy.RetentionDays.HasValue ||
                           (_policy.TotalSizeCapBytes.HasValue && _policy.TotalSizeCapBytes.Value > 0);

        if (!needsPruning) return;

        var files = RollingFilePruner.FindAllRolledFiles(_pathResolver.Resolve(_baseFilePath));

        if (_policy.RetentionDays.HasValue)
        {
            RollingFilePruner.PruneByAge(files, _policy.RetentionDays.Value, activeResolvedPath);
        }

        if (_policy.LogQueueSize.HasValue)
        {
            RollingFilePruner.PruneToQueueSize(files, _policy.LogQueueSize.Value, activeResolvedPath);
        }

        if (_policy.TotalSizeCapBytes is > 0)
        {
            RollingFilePruner.PruneByTotalSize(files, _policy.TotalSizeCapBytes.Value, activeResolvedPath);
        }
    }

    private void AdvancePeriodIfNeeded() {
        if (_policy.Interval == LogFileRollingInterval.None) return;

        var currentPeriodStart = RollingFilePeriod.GetPeriodStart(DateTimeOffset.UtcNow, _policy);
        if (currentPeriodStart == _activePeriodStart) return;

        _activePeriodStart = currentPeriodStart;
        _sizeSequence = 1;
        _activePath = RollingFilePathBuilder.Build(_baseFilePath, _activePeriodStart, _policy, _sizeSequence);
        // The next EnsureStreamOpen pivots the FileStream to the new period's
        // path. Pre-emptively closing here would force a re-open on the very
        // next write anyway; the path-comparison branch in EnsureStreamOpen
        // handles it cleanly.

        if (_policy.MaxRetainedFiles.HasValue)
        {
            var files = RollingFilePruner.FindAllRolledFiles(_pathResolver.Resolve(_baseFilePath));
            RollingFilePruner.PruneToRetained(files, _policy.MaxRetainedFiles.Value);
        }
    }

    private void GuardMaxBytes(long lineBytes, string line) {
        if (_policy.MaxBytes.HasValue && lineBytes > _policy.MaxBytes.Value)
        {
            throw new InvalidOperationException(
                $"A single log message ({lineBytes:N0} bytes) exceeds the configured " +
                $"MaxBytes file size limit ({_policy.MaxBytes.Value:N0} bytes). " +
                $"Either increase MaxBytes or reduce the message size. " +
                $"Message preview: \"{Truncate(line, 200)}\"");
        }
    }

    private void RollIfSizeExceeded(ref string resolved, long lineBytes) {
        if (!_policy.MaxBytes.HasValue) return;

        var projected = ProjectedFileLength(resolved) + lineBytes;
        if (projected <= _policy.MaxBytes.Value) return;

        _sizeSequence += 1;
        _activePath = RollingFilePathBuilder.Build(_baseFilePath, _activePeriodStart, _policy, _sizeSequence);
        resolved = _pathResolver.Resolve(_activePath);
        EnsureDirectory(resolved);
    }

    // The cached _currentFileBytes counter is authoritative while the stream
    // is open. After a rotation the counter is reset for the fresh file.
    // For the very first write of the writer's lifetime — or after Clear —
    // we fall back to FileInfo so an existing on-disk file (e.g. the previous
    // process left one) participates in the size budget.
    private long ProjectedFileLength(string resolved)
    {
        if (_stream is not null && string.Equals(_openedPath, resolved, StringComparison.OrdinalIgnoreCase))
        {
            return _currentFileBytes;
        }

        var info = new FileInfo(resolved);
        return info.Exists ? info.Length : 0;
    }

    // Caller must hold _sync.
    private void EnsureStreamOpen(string resolvedPath)
    {
        if (_stream is not null && string.Equals(_openedPath, resolvedPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // Path changed — drain the previous file before pivoting so the
        // closed file is complete on disk for downstream tooling.
        if (_stream is not null)
        {
            try { _stream.Flush(true); } catch { /* swallow on rotation */ }
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
        // FileMode.Append seeks to end, so the existing length is the
        // starting offset. Pick it up so the size-budget calculation stays
        // correct across reopens.
        _currentFileBytes = _stream.Length;
    }

    // Caller must hold _sync.
    private void CloseStreamCore()
    {
        if (_stream is null) return;

        try { _stream.Flush(true); } catch { /* swallow on shutdown */ }
        _stream.Dispose();
        _stream = null;
        _openedPath = null;
        _currentFileBytes = 0;
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength] + "...";

    private static void EnsureDirectory(string resolvedPath) {
        var directory = Path.GetDirectoryName(resolvedPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }
}

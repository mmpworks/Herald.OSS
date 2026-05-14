#nullable enable

using System;
using System.Globalization;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Text;
using MMP.Herald.Events;
using MMP.Herald.Pipeline;

namespace MMP.Herald.Addons.GamePerformance;

/// <summary>
/// Memory-mapped file ring buffer that survives process crashes.
/// Stores the most recent N log events in a fixed-size memory-mapped file.
/// On crash, the file persists and can be read by a recovery tool.
///
/// Uses a fixed-size circular buffer with a write pointer stored at offset 0.
/// Each entry is a fixed-width slot (padded/truncated to fit).
/// </summary>
public sealed class CrashSafeRingBuffer : ILogger, IDisposable, MMP.Herald.Pipeline.IComponentMetadata
{
    private readonly ILogger? _inner;
    private readonly MemoryMappedFile _mmf;
    private readonly MemoryMappedViewAccessor _accessor;
    private readonly int _slotCount;
    private readonly int _slotSize;
    private readonly int _headerSize = 8; // 4 bytes write index + 4 bytes slot count
    private readonly object _sync = new();
    private int _writeIndex;

    /// <summary>
    /// Create a crash-safe ring buffer backed by a memory-mapped file.
    /// </summary>
    /// <param name="filePath">Path to the memory-mapped file</param>
    /// <param name="slotCount">Number of event slots in the ring buffer</param>
    /// <param name="slotSize">Max bytes per event slot (events are truncated to fit)</param>
    /// <param name="inner">Optional inner logger to forward events to (in addition to ring buffer)</param>
    public CrashSafeRingBuffer(
        string filePath,
        int slotCount = 1000,
        int slotSize = 1024,
        ILogger? inner = null) {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        if (slotCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(slotCount), "Slot count must be positive.");
        }

        _inner = inner;
        _slotCount = slotCount;
        _slotSize = slotSize;

        var totalSize = _headerSize + (long)slotCount * slotSize;

        Directory.CreateDirectory(Path.GetDirectoryName(filePath) ?? ".");
        _mmf = MemoryMappedFile.CreateFromFile(filePath, FileMode.OpenOrCreate,
            null, totalSize, MemoryMappedFileAccess.ReadWrite);
        _accessor = _mmf.CreateViewAccessor(0, totalSize, MemoryMappedFileAccess.ReadWrite);

        // Read existing write index (survives crash)
        _writeIndex = _accessor.ReadInt32(0);
        if (_writeIndex < 0 || _writeIndex >= slotCount)
        {
            _writeIndex = 0;
        }
    }

    public void Log(LogEvent logEvent) {
        ArgumentNullException.ThrowIfNull(logEvent);

        lock (_sync)
        {
            WriteToSlot(logEvent);
        }

        _inner?.Log(logEvent);
    }

    /// <summary>
    /// Read all events currently in the ring buffer (for crash recovery).
    /// Returns events in chronological order (oldest first).
    /// </summary>
    public string[] ReadAllSlots() {
        lock (_sync)
        {
        var results = new string[_slotCount];
        var readIndex = _writeIndex; // start from oldest

        for (var i = 0; i < _slotCount; i++)
        {
            var slotOffset = _headerSize + readIndex * _slotSize;
            var bytes = new byte[_slotSize];
            _accessor.ReadArray(slotOffset, bytes, 0, _slotSize);

            var nullIndex = Array.IndexOf<byte>(bytes, 0);
            var length = nullIndex >= 0 ? nullIndex : _slotSize;
            var text = Encoding.UTF8.GetString(bytes, 0, length).TrimEnd('\0');

            results[i] = text;
            readIndex = (readIndex + 1) % _slotCount;
        }

        return results;
        } // lock
    }

    public void Dispose() {
        _accessor.Dispose();
        _mmf.Dispose();
    }

    private void WriteToSlot(LogEvent logEvent) {
        var slotOffset = _headerSize + _writeIndex * _slotSize;

        // Format event as compact string
        var content = $"{logEvent.TimeUtc:O}\t{logEvent.Level.Key}\t{logEvent.Category.Value}\t{logEvent.Message}";
        var bytes = Encoding.UTF8.GetBytes(content);

        // Truncate if too large, pad with zeros if smaller
        var writeLength = Math.Min(bytes.Length, _slotSize - 1); // leave room for null terminator
        _accessor.WriteArray(slotOffset, bytes, 0, writeLength);

        // Null-terminate
        if (writeLength < _slotSize)
        {
            _accessor.Write(slotOffset + writeLength, (byte)0);
        }

        // Advance write pointer (wraps around)
        _writeIndex = (_writeIndex + 1) % _slotCount;
        _accessor.Write(0, _writeIndex); // persist write index
    }

    // -- IComponentMetadata --
    string Pipeline.IComponentMetadata.ComponentName => "crashSafeBuffer";
    string Pipeline.IComponentMetadata.DisplayName => "Crash-Safe Ring Buffer";
    string Pipeline.IComponentMetadata.Description => "Memory-mapped ring buffer that survives process crashes.";
    string Pipeline.IComponentMetadata.Help => "Fixed-size circular buffer in a memory-mapped file. On crash, the file contains the last N events. Read with LogReplayReader on restart.";
    Pipeline.VendorInfo Pipeline.IComponentMetadata.Vendor => Pipeline.VendorInfo.MMP;
    System.Collections.Generic.IReadOnlyList<Routing.SinkConfigField> Pipeline.IComponentMetadata.ConfigurationSchema => [];
}

#nullable enable

using System.Collections.Generic;
using System.Threading;
using MMP.Herald.Events;
using MMP.Herald.Pipeline;
using MMP.Herald.Pipeline.Kernel;

namespace MMP.Herald.OSS.Tests.Helpers;

/// <summary>
/// An <see cref="IKernelSink"/> spy for kernel-decorator tests. Records both
/// entry points: the buffer hot path and the heap twin. Because a
/// <see cref="LogEventBuffer"/> is a <c>ref struct</c> that cannot be retained
/// past the call, the spy captures a snapshot — the event level plus an
/// optional named property value — rather than the buffer itself.
/// </summary>
public sealed class KernelSpySink : ILogger, IKernelSink
{
    private readonly string? _captureProperty;
    private readonly List<string?> _capturedKeys = new();
    private readonly object _sync = new();
    private int _bufferLogCount;
    private int _heapLogCount;

    /// <param name="captureProperty">
    /// Property name to snapshot from each event (e.g. the routing key) so a
    /// test can assert which events landed here. Null to record counts only.
    /// </param>
    public KernelSpySink(string? captureProperty = null) => _captureProperty = captureProperty;

    public int BufferLogCount => Volatile.Read(ref _bufferLogCount);
    public int HeapLogCount => Volatile.Read(ref _heapLogCount);
    public int TotalLogCount => BufferLogCount + HeapLogCount;

    /// <summary>Snapshot of the captured property value for each event that landed here.</summary>
    public IReadOnlyList<string?> CapturedKeys
    {
        get { lock (_sync) return new List<string?>(_capturedKeys); }
    }

    public void Log(in LogEventBuffer buffer)
    {
        Interlocked.Increment(ref _bufferLogCount);
        Capture(buffer.GetProperty(_captureProperty ?? string.Empty));
    }

    public void Log(LogEvent logEvent)
    {
        Interlocked.Increment(ref _heapLogCount);
        var value = _captureProperty is null ? null : Find(logEvent, _captureProperty);
        Capture(value);
    }

    private void Capture(object? value)
    {
        if (_captureProperty is null) return;
        lock (_sync) _capturedKeys.Add(value?.ToString());
    }

    private static object? Find(LogEvent logEvent, string name)
    {
        foreach (var p in logEvent.Properties)
        {
            if (p.Name == name) return p.Value;
        }
        return null;
    }
}

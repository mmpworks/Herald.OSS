#nullable enable

namespace MMP.Herald.Pipeline;

/// <summary>
/// Flush policy for <see cref="DurableBufferLogger"/>'s write-ahead log.
/// Controls how aggressively WAL writes land on disk. The wrong choice
/// is a trade-off, not a bug — operators pick based on whether they
/// care more about throughput or about surviving a host-level power
/// loss between <c>Flush()</c> and the OS write-back.
///
/// <para><b>Why this matters.</b> The WAL is the safety net for at-least-once
/// delivery when a downstream sink fails: a crash right after the
/// forward call must leave the event recorded on disk so the next
/// start-up can replay it. Without fsync, a host-level power loss
/// between <c>Flush()</c> and the OS write-back silently truncates the
/// tail and loses the events that were in flight. Per-write fsync
/// closes that window at the cost of a syscall per event.</para>
/// </summary>
public enum WalFlushPolicy
{
    /// <summary>
    /// Flush to the OS write buffer on every event (default, matches
    /// the pre-P3-C behavior). Survives a process crash without loss
    /// because the OS keeps the buffer until write-back completes. Does
    /// NOT survive a host-level power loss between the flush and the
    /// OS committing the page to disk — that window is typically
    /// seconds on a busy host.
    /// </summary>
    OsBuffered = 0,

    /// <summary>
    /// Call <see cref="System.IO.FileStream.Flush(bool)"/> with
    /// <c>flushToDisk=true</c> on every event. Each WAL line is durable
    /// against both process crash and host-level power loss by the time
    /// <c>Log</c> returns. The cost is one extra syscall per event;
    /// high-volume sinks should stay on <see cref="OsBuffered"/> and
    /// rely on the batching decorator for durability amortisation.
    /// </summary>
    FlushPerWrite = 1,
}
